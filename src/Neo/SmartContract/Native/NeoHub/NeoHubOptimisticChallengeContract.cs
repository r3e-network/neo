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

    public sealed class NeoHubOptimisticChallengeContract : NeoHubNativeContract
    {
        private const byte PrefixDeadline = 0x01;
        private const byte PrefixAcceptedFraud = 0x02;
        private const byte PrefixSequencer = 0x03;
        private const byte KeyChallengeWindowSeconds = 0x04;
        private const byte KeyChallengerRewardBps = 0x05;
        private const byte KeySettlementManager = 0xfc;
        private const byte KeySequencerBond = 0xfd;
        private const byte KeyOwner = 0xff;

        public const uint DefaultWindowSeconds = 3600;
        public const ushort DefaultChallengerRewardBps = 5000;
        public const ushort BasisPointsTotal = 10_000;

        [ContractEvent(0, name: "WindowOpened", "chainId", ContractParameterType.Integer, "batchNumber", ContractParameterType.Integer, "deadline", ContractParameterType.Integer, "sequencer", ContractParameterType.Hash160)]
        [ContractEvent(1, name: "ChallengeAccepted", "chainId", ContractParameterType.Integer, "batchNumber", ContractParameterType.Integer, "challenger", ContractParameterType.Hash160, "slashedAmount", ContractParameterType.Integer)]
        [ContractEvent(2, name: "WindowFinalized", "chainId", ContractParameterType.Integer, "batchNumber", ContractParameterType.Integer)]
        internal NeoHubOptimisticChallengeContract() : base(-115) { }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
        private void Configure(ApplicationEngine engine, UInt160 owner, UInt160 settlementManager, UInt160 sequencerBond)
        {
            RequireNonZero(owner, nameof(owner));
            RequireNonZero(settlementManager, nameof(settlementManager));
            RequireNonZero(sequencerBond, nameof(sequencerBond));
            AssertOwnerOrCommittee(engine);

            WriteUInt160(engine.SnapshotCache, KeyOwner, owner);
            WriteUInt160(engine.SnapshotCache, KeySettlementManager, settlementManager);
            WriteUInt160(engine.SnapshotCache, KeySequencerBond, sequencerBond);
            PutInteger(engine.SnapshotCache, CreateStorageKey(KeyChallengeWindowSeconds), DefaultWindowSeconds);
            PutInteger(engine.SnapshotCache, CreateStorageKey(KeyChallengerRewardBps), DefaultChallengerRewardBps);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public UInt160 GetOwner(IReadOnlyStore snapshot) => ReadUInt160(snapshot, KeyOwner);

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public UInt160 GetSettlementManager(IReadOnlyStore snapshot) => ReadUInt160(snapshot, KeySettlementManager);

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public UInt160 GetSequencerBond(IReadOnlyStore snapshot) => ReadUInt160(snapshot, KeySequencerBond);

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public uint GetWindowSeconds(IReadOnlyStore snapshot)
        {
            var seconds = ReadInteger(snapshot, CreateStorageKey(KeyChallengeWindowSeconds));
            return seconds == BigInteger.Zero ? DefaultWindowSeconds : (uint)seconds;
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public ushort GetChallengerRewardBps(IReadOnlyStore snapshot)
        {
            var bps = ReadInteger(snapshot, CreateStorageKey(KeyChallengerRewardBps));
            return bps == BigInteger.Zero ? DefaultChallengerRewardBps : (ushort)bps;
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
        private void SetWindowSeconds(ApplicationEngine engine, uint seconds)
        {
            if (seconds < 60 || seconds > 7 * 86400)
                throw new ArgumentOutOfRangeException(nameof(seconds), "window out of bounds [60s, 7d].");
            AssertOwner(engine);
            PutInteger(engine.SnapshotCache, CreateStorageKey(KeyChallengeWindowSeconds), seconds);
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
        private void SetChallengerRewardBps(ApplicationEngine engine, ushort bps)
        {
            if (bps == 0 || bps > BasisPointsTotal)
                throw new ArgumentOutOfRangeException(nameof(bps), "bps out of (0, 10000].");
            AssertOwner(engine);
            PutInteger(engine.SnapshotCache, CreateStorageKey(KeyChallengerRewardBps), bps);
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
        private uint OpenWindow(ApplicationEngine engine, uint chainId, ulong batchNumber, UInt160 sequencer)
        {
            AssertSettlementManager(engine);
            if (chainId == 0) throw new ArgumentOutOfRangeException(nameof(chainId), "chainId 0 is reserved for L1.");
            RequireNonZero(sequencer, nameof(sequencer));

            var key = DeadlineKey(chainId, batchNumber);
            if (engine.SnapshotCache.Contains(key)) throw new InvalidOperationException("window already open");

            var deadline = checked(RuntimeTimeSeconds(engine) + GetWindowSeconds(engine.SnapshotCache));
            Put(engine.SnapshotCache, key, U32Le(deadline));
            WriteUInt160(engine.SnapshotCache, SequencerKey(chainId, batchNumber), sequencer);
            Notify(engine, "WindowOpened", chainId, batchNumber, deadline, sequencer);
            return deadline;
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowCall | CallFlags.AllowNotify)]
        private async ContractTask Challenge(ApplicationEngine engine, uint chainId, ulong batchNumber, UInt160 challenger, byte[] fraudProofBytes, UInt160 fraudVerifier)
        {
            AssertWitness(engine, challenger, "no witness for challenger");
            RequireNonZero(challenger, nameof(challenger));
            ArgumentNullException.ThrowIfNull(fraudProofBytes);
            if (fraudProofBytes.Length == 0) throw new ArgumentException("empty fraud proof.", nameof(fraudProofBytes));
            RequireNonZero(fraudVerifier, nameof(fraudVerifier));

            var deadline = ReadDeadlineOrThrow(engine.SnapshotCache, chainId, batchNumber);
            if (RuntimeTimeSeconds(engine) > deadline) throw new InvalidOperationException("challenge window closed");

            var acceptedKey = AcceptedFraudKey(chainId, batchNumber);
            if (engine.SnapshotCache.Contains(acceptedKey)) throw new InvalidOperationException("already accepted");

            var verified = await engine.CallFromNativeContractAsync<bool>(
                Hash, fraudVerifier, "verifyFraud", chainId, batchNumber, fraudProofBytes);
            if (!verified) throw new InvalidOperationException("fraud proof rejected");

            var sequencer = GetSequencer(engine.SnapshotCache, chainId, batchNumber);
            RequireNonZero(sequencer, nameof(sequencer));
            var bondContract = GetSequencerBond(engine.SnapshotCache);
            RequireNonZero(bondContract, nameof(bondContract));
            var bondBalance = await engine.CallFromNativeContractAsync<BigInteger>(
                Hash, bondContract, "getBalance", chainId, sequencer.ToArray());
            if (bondBalance <= BigInteger.Zero) throw new InvalidOperationException("no bond to slash");

            var challengerCut = bondBalance * GetChallengerRewardBps(engine.SnapshotCache) / BasisPointsTotal;
            Put(engine.SnapshotCache, acceptedKey, challenger.ToArray());

            await engine.CallFromNativeContractAsync(
                Hash, bondContract, "slash", chainId, sequencer.ToArray(), challengerCut, challenger.ToArray());
            var remaining = bondBalance - challengerCut;
            if (remaining > BigInteger.Zero)
            {
                await engine.CallFromNativeContractAsync(
                    Hash, bondContract, "slash", chainId, sequencer.ToArray(), remaining, UInt160.Zero.ToArray());
            }

            var settlementManager = GetSettlementManager(engine.SnapshotCache);
            RequireNonZero(settlementManager, nameof(settlementManager));
            await engine.CallFromNativeContractAsync(Hash, settlementManager, "revertBatch", chainId, batchNumber);

            Notify(engine, "ChallengeAccepted", chainId, batchNumber, challenger, bondBalance);
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowCall | CallFlags.AllowNotify)]
        private async ContractTask FinalizeIfPastWindow(ApplicationEngine engine, uint chainId, ulong batchNumber)
        {
            var deadline = ReadDeadlineOrThrow(engine.SnapshotCache, chainId, batchNumber);
            if (RuntimeTimeSeconds(engine) <= deadline) throw new InvalidOperationException("challenge window still open");
            if (engine.SnapshotCache.Contains(AcceptedFraudKey(chainId, batchNumber)))
                throw new InvalidOperationException("batch was challenged; cannot finalize");

            var settlementManager = GetSettlementManager(engine.SnapshotCache);
            RequireNonZero(settlementManager, nameof(settlementManager));
            await engine.CallFromNativeContractAsync(Hash, settlementManager, "finalizeBatch", chainId, batchNumber);
            Notify(engine, "WindowFinalized", chainId, batchNumber);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public bool IsWindowOpen(IReadOnlyStore snapshot, uint chainId, ulong batchNumber, uint nowUnixSeconds)
        {
            var deadline = GetDeadline(snapshot, chainId, batchNumber);
            return deadline != 0 && nowUnixSeconds <= deadline;
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public uint GetDeadline(IReadOnlyStore snapshot, uint chainId, ulong batchNumber)
        {
            return snapshot.TryGet(DeadlineKey(chainId, batchNumber), out var item)
                ? DecodeDeadline(item.Value.Span)
                : 0U;
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public UInt160 GetSequencer(IReadOnlyStore snapshot, uint chainId, ulong batchNumber)
        {
            return ReadUInt160(snapshot, SequencerKey(chainId, batchNumber));
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public bool IsFraudAccepted(IReadOnlyStore snapshot, uint chainId, ulong batchNumber)
        {
            return snapshot.TryGet(AcceptedFraudKey(chainId, batchNumber), out _);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public UInt160 GetAcceptedFraud(IReadOnlyStore snapshot, uint chainId, ulong batchNumber)
        {
            return ReadUInt160(snapshot, AcceptedFraudKey(chainId, batchNumber));
        }

        private void AssertSettlementManager(ApplicationEngine engine)
        {
            var settlementManager = GetSettlementManager(engine.SnapshotCache);
            RequireNonZero(settlementManager, nameof(settlementManager));
            if (!engine.CheckWitnessInternal(settlementManager) && engine.CallingScriptHash != settlementManager)
                throw new InvalidOperationException("not settlement manager");
        }

        private uint ReadDeadlineOrThrow(IReadOnlyStore snapshot, uint chainId, ulong batchNumber)
        {
            if (!snapshot.TryGet(DeadlineKey(chainId, batchNumber), out var item))
                throw new InvalidOperationException("no open window");
            return DecodeDeadline(item.Value.Span);
        }

        private StorageKey DeadlineKey(uint chainId, ulong batchNumber) => Key(PrefixDeadline, chainId, batchNumber);

        private StorageKey AcceptedFraudKey(uint chainId, ulong batchNumber) => Key(PrefixAcceptedFraud, chainId, batchNumber);

        private StorageKey SequencerKey(uint chainId, ulong batchNumber) => Key(PrefixSequencer, chainId, batchNumber);

        private static uint DecodeDeadline(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length != 4) throw new InvalidOperationException("corrupt challenge deadline");
            return ReadU32Le(bytes);
        }

        private static BigInteger ReadInteger(IReadOnlyStore snapshot, StorageKey key)
        {
            return snapshot.TryGet(key, out var item) ? (BigInteger)item : BigInteger.Zero;
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
