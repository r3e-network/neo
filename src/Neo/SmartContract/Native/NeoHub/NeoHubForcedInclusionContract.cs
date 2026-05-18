// Copyright (C) 2015-2026 The Neo Project.
//
// NeoHub native contracts are maintained by r3e-network in the r3e/neo-n3-core
// branch. They embed the Neo Elastic Network L1 anchor surface into the r3e Neo
// core fork so production L1 networks do not deploy these system contracts after
// genesis.

#pragma warning disable IDE0051

using Neo.Cryptography;
using Neo.Cryptography.ECC;
using Neo.Extensions;
using Neo.Persistence;
using Neo.SmartContract.Manifest;
using Neo.VM.Types;
using System;
using System.Numerics;

namespace Neo.SmartContract.Native
{

    public sealed class NeoHubForcedInclusionContract : NeoHubNativeContract
    {
        private const byte PrefixNonce = 0x01;
        private const byte PrefixEntry = 0x02;
        private const byte PrefixConsumed = 0x03;
        private const byte KeyDeadlineSeconds = 0x04;
        private const byte KeyFeeAmount = 0x05;
        private const byte KeyFeeRecipient = 0x06;
        private const byte KeyGasToken = 0x07;
        private const byte PrefixReported = 0x08;
        private const byte KeySettlementManager = 0xfd;
        private const byte KeyOwner = 0xff;

        public const uint DefaultDeadlineSeconds = 7200;

        [ContractEvent(0, name: "ForcedTxEnqueued", "chainId", ContractParameterType.Integer, "nonce", ContractParameterType.Integer, "sender", ContractParameterType.Hash160, "txHash", ContractParameterType.Hash256)]
        [ContractEvent(1, name: "ForcedTxConsumed", "chainId", ContractParameterType.Integer, "nonce", ContractParameterType.Integer)]
        [ContractEvent(2, name: "SequencerCensorshipReported", "chainId", ContractParameterType.Integer, "nonce", ContractParameterType.Integer, "sequencer", ContractParameterType.Hash160)]
        [ContractEvent(3, name: "ForcedInclusionFeeCharged", "payer", ContractParameterType.Hash160, "recipient", ContractParameterType.Hash160, "amount", ContractParameterType.Integer)]
        internal NeoHubForcedInclusionContract() : base(-114) { }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
        private void Configure(ApplicationEngine engine, UInt160 owner, UInt160 settlementManager)
        {
            RequireNonZero(owner, nameof(owner));
            RequireNonZero(settlementManager, nameof(settlementManager));
            AssertOwnerOrCommittee(engine);
            WriteUInt160(engine.SnapshotCache, KeyOwner, owner);
            WriteUInt160(engine.SnapshotCache, KeySettlementManager, settlementManager);
            PutInteger(engine.SnapshotCache, CreateStorageKey(KeyDeadlineSeconds), DefaultDeadlineSeconds);
            PutInteger(engine.SnapshotCache, CreateStorageKey(KeyFeeAmount), BigInteger.Zero);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public UInt160 GetOwner(IReadOnlyStore snapshot) => ReadUInt160(snapshot, KeyOwner);

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public UInt160 GetSettlementManager(IReadOnlyStore snapshot) => ReadUInt160(snapshot, KeySettlementManager);

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public uint GetDeadlineSeconds(IReadOnlyStore snapshot)
        {
            var seconds = ReadUInt32(snapshot, CreateStorageKey(KeyDeadlineSeconds));
            return seconds == 0 ? DefaultDeadlineSeconds : seconds;
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
        private void SetDeadlineSeconds(ApplicationEngine engine, uint seconds)
        {
            if (seconds < 60 || seconds > 86400) throw new ArgumentOutOfRangeException(nameof(seconds), "deadline out of bounds [60, 86400].");
            AssertOwner(engine);
            PutInteger(engine.SnapshotCache, CreateStorageKey(KeyDeadlineSeconds), seconds);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public BigInteger GetFee(IReadOnlyStore snapshot) => ReadInteger(snapshot, CreateStorageKey(KeyFeeAmount));

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public UInt160 GetFeeRecipient(IReadOnlyStore snapshot) => ReadUInt160(snapshot, KeyFeeRecipient);

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public UInt160 GetGasToken(IReadOnlyStore snapshot) => ReadUInt160(snapshot, KeyGasToken);

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
        private void SetFee(ApplicationEngine engine, BigInteger amount)
        {
            if (amount < BigInteger.Zero) throw new ArgumentOutOfRangeException(nameof(amount), "fee must be non-negative.");
            AssertOwner(engine);
            if (amount > BigInteger.Zero)
            {
                if (GetFeeRecipient(engine.SnapshotCache) == UInt160.Zero)
                    throw new InvalidOperationException("set feeRecipient before non-zero fee");
                if (GetGasToken(engine.SnapshotCache) == UInt160.Zero)
                    throw new InvalidOperationException("set gasToken before non-zero fee");
            }
            PutInteger(engine.SnapshotCache, CreateStorageKey(KeyFeeAmount), amount);
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
        private void SetFeeRecipient(ApplicationEngine engine, UInt160 recipient)
        {
            RequireNonZero(recipient, nameof(recipient));
            AssertOwner(engine);
            WriteUInt160(engine.SnapshotCache, KeyFeeRecipient, recipient);
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
        private void SetGasToken(ApplicationEngine engine, UInt160 gasContract)
        {
            RequireNonZero(gasContract, nameof(gasContract));
            AssertOwner(engine);
            WriteUInt160(engine.SnapshotCache, KeyGasToken, gasContract);
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowCall | CallFlags.AllowNotify)]
        private async ContractTask<ulong> EnqueueForcedTransaction(ApplicationEngine engine, uint chainId, byte[] encodedTx, UInt256 txHash)
        {
            if (chainId == 0) throw new ArgumentOutOfRangeException(nameof(chainId), "chainId 0 is reserved for L1.");
            ArgumentNullException.ThrowIfNull(encodedTx);
            if (encodedTx.Length == 0) throw new ArgumentException("empty tx.", nameof(encodedTx));
            var caller = engine.CallingScriptHash ?? throw new InvalidOperationException("calling script hash unavailable");

            var fee = GetFee(engine.SnapshotCache);
            if (fee > BigInteger.Zero)
                await ChargeFee(engine, caller, fee);

            var nonce = NextNonce(engine.SnapshotCache, chainId);
            var deadline = checked(RuntimeTimeSeconds(engine) + GetDeadlineSeconds(engine.SnapshotCache));
            Put(engine.SnapshotCache, EntryKey(chainId, nonce), EncodeEntry(caller, txHash, encodedTx, deadline));
            Notify(engine, "ForcedTxEnqueued", chainId, nonce, caller, txHash);
            return nonce;
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public byte[] GetEntry(IReadOnlyStore snapshot, uint chainId, ulong nonce)
        {
            return snapshot.TryGet(EntryKey(chainId, nonce), out var item) ? item.Value.ToArray() : EmptyBytes;
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
        private void MarkConsumed(ApplicationEngine engine, uint chainId, ulong nonce)
        {
            AssertSettlementManager(engine);
            if (!engine.SnapshotCache.Contains(EntryKey(chainId, nonce))) throw new InvalidOperationException("entry not found");
            var key = ConsumedKey(chainId, nonce);
            if (engine.SnapshotCache.Contains(key)) throw new InvalidOperationException("already consumed");
            Put(engine.SnapshotCache, key, [1]);
            Notify(engine, "ForcedTxConsumed", chainId, nonce);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public bool IsConsumed(IReadOnlyStore snapshot, uint chainId, ulong nonce)
        {
            return snapshot.TryGet(ConsumedKey(chainId, nonce), out _);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public bool IsReported(IReadOnlyStore snapshot, uint chainId, ulong nonce)
        {
            return snapshot.TryGet(ReportedKey(chainId, nonce), out _);
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
        private bool ReportCensorship(ApplicationEngine engine, uint chainId, ulong nonce, UInt160 sequencer)
        {
            if (chainId == 0) throw new ArgumentOutOfRangeException(nameof(chainId), "chainId 0 is reserved for L1.");
            RequireNonZero(sequencer, nameof(sequencer));
            if (IsConsumed(engine.SnapshotCache, chainId, nonce)) throw new InvalidOperationException("already consumed");
            var reportKey = ReportedKey(chainId, nonce);
            if (engine.SnapshotCache.Contains(reportKey)) throw new InvalidOperationException("already reported");

            var entry = GetEntry(engine.SnapshotCache, chainId, nonce);
            if (entry.Length == 0) throw new InvalidOperationException("entry not found");
            var deadline = ReadEntryDeadline(entry);
            if (RuntimeTimeSeconds(engine) < deadline) return false;

            Put(engine.SnapshotCache, reportKey, [1]);
            Notify(engine, "SequencerCensorshipReported", chainId, nonce, sequencer);
            return true;
        }

        private async ContractTask ChargeFee(ApplicationEngine engine, UInt160 payer, BigInteger fee)
        {
            var recipient = GetFeeRecipient(engine.SnapshotCache);
            var gas = GetGasToken(engine.SnapshotCache);
            RequireNonZero(recipient, nameof(recipient));
            RequireNonZero(gas, nameof(gas));

            var transferred = await engine.CallFromNativeContractAsync<bool>(
                Hash, gas, "transfer", payer.ToArray(), recipient.ToArray(), fee, StackItem.Null);
            if (!transferred) throw new InvalidOperationException("fee transfer failed");
            Notify(engine, "ForcedInclusionFeeCharged", payer, recipient, fee);
        }

        private ulong NextNonce(DataCache snapshot, uint chainId)
        {
            var key = NonceKey(chainId);
            var item = snapshot.GetAndChange(key, () => new StorageItem(BigInteger.Zero));
            var next = (ulong)(BigInteger)item + 1UL;
            item.Set(new BigInteger(next));
            return next;
        }

        private void AssertSettlementManager(ApplicationEngine engine)
        {
            var settlementManager = GetSettlementManager(engine.SnapshotCache);
            RequireNonZero(settlementManager, nameof(settlementManager));
            if (!engine.CheckWitnessInternal(settlementManager) && engine.CallingScriptHash != settlementManager)
                throw new InvalidOperationException("not settlement manager");
        }

        private StorageKey NonceKey(uint chainId) => Key(PrefixNonce, chainId);

        private StorageKey EntryKey(uint chainId, ulong nonce) => Key(PrefixEntry, chainId, nonce);

        private StorageKey ConsumedKey(uint chainId, ulong nonce) => Key(PrefixConsumed, chainId, nonce);

        private StorageKey ReportedKey(uint chainId, ulong nonce) => Key(PrefixReported, chainId, nonce);

        private static byte[] EncodeEntry(UInt160 sender, UInt256 txHash, byte[] encodedTx, uint deadlineUnixSeconds)
        {
            var bytes = new byte[UInt160.Length + UInt256.Length + 4 + encodedTx.Length + 4];
            var offset = 0;
            sender.ToArray().CopyTo(bytes, offset);
            offset += UInt160.Length;
            txHash.ToArray().CopyTo(bytes, offset);
            offset += UInt256.Length;
            U32Le((uint)encodedTx.Length).CopyTo(bytes, offset);
            offset += 4;
            encodedTx.CopyTo(bytes, offset);
            offset += encodedTx.Length;
            U32Le(deadlineUnixSeconds).CopyTo(bytes, offset);
            return bytes;
        }

        private static uint ReadEntryDeadline(byte[] entry)
        {
            if (entry.Length < UInt160.Length + UInt256.Length + 4 + 4)
                throw new InvalidOperationException("corrupt forced-inclusion entry");
            var txLength = ReadU32Le(entry.AsSpan(UInt160.Length + UInt256.Length, 4));
            var expectedLength = UInt160.Length + UInt256.Length + 4 + checked((int)txLength) + 4;
            if (entry.Length != expectedLength) throw new InvalidOperationException("corrupt forced-inclusion entry");
            return ReadU32Le(entry.AsSpan(expectedLength - 4, 4));
        }

        private static BigInteger ReadInteger(IReadOnlyStore snapshot, StorageKey key)
        {
            return snapshot.TryGet(key, out var item) ? (BigInteger)item : BigInteger.Zero;
        }

        private static uint ReadUInt32(IReadOnlyStore snapshot, StorageKey key)
        {
            return snapshot.TryGet(key, out var item) ? (uint)(BigInteger)item : 0U;
        }

        private static void PutInteger(DataCache snapshot, StorageKey key, BigInteger value)
        {
            snapshot.GetAndChange(key, () => new StorageItem(BigInteger.Zero)).Set(value);
        }

        private static uint RuntimeTimeSeconds(ApplicationEngine engine)
        {
            return checked((uint)((engine.PersistingBlock?.Timestamp ?? 0UL) / 1000UL));
        }
    }

}
