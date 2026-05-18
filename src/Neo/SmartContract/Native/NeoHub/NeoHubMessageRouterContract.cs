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

    public sealed class NeoHubMessageRouterContract : NeoHubNativeContract
    {
        private const byte PrefixL1ToL2Nonce = 0x01;
        private const byte PrefixL1ToL2Msg = 0x02;
        private const byte PrefixL2ToL1Root = 0x03;
        private const byte PrefixL2ToL2Root = 0x04;
        private const byte PrefixGlobalRoot = 0x05;
        private const byte PrefixConsumed = 0x06;
        private const byte PrefixL1TxFilter = 0x07;
        private const byte PrefixSettlementManager = 0xfd;
        private const byte KeyOwner = 0xff;

        [ContractEvent(0, name: "L1ToL2Enqueued", "targetChainId", ContractParameterType.Integer, "nonce", ContractParameterType.Integer, "sender", ContractParameterType.Hash160, "receiver", ContractParameterType.Hash160)]
        [ContractEvent(1, name: "L2ToL1Consumed", "sourceChainId", ContractParameterType.Integer, "messageHash", ContractParameterType.Hash256)]
        [ContractEvent(2, name: "GlobalRootPublished", "batchEpoch", ContractParameterType.Integer, "globalRoot", ContractParameterType.Hash256)]
        [ContractEvent(3, name: "L1TxFilterSet", "targetChainId", ContractParameterType.Integer, "filter", ContractParameterType.Hash160)]
        internal NeoHubMessageRouterContract() : base(-106) { }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
        private void Configure(ApplicationEngine engine, UInt160 owner, UInt160 settlementManager)
        {
            RequireNonZero(owner, nameof(owner));
            RequireNonZero(settlementManager, nameof(settlementManager));
            AssertOwnerOrCommittee(engine);
            WriteUInt160(engine.SnapshotCache, KeyOwner, owner);
            WriteUInt160(engine.SnapshotCache, PrefixSettlementManager, settlementManager);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public UInt160 GetOwner(IReadOnlyStore snapshot) => ReadUInt160(snapshot, KeyOwner);

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowCall | CallFlags.AllowNotify)]
        private async ContractTask<ulong> EnqueueL1ToL2(ApplicationEngine engine, uint targetChainId, UInt160 receiver, byte messageType, byte[] payload)
        {
            if (targetChainId == 0) throw new ArgumentOutOfRangeException(nameof(targetChainId), "targetChainId 0 is reserved for L1.");
            RequireNonZero(receiver, nameof(receiver));
            var sender = engine.CallingScriptHash ?? throw new InvalidOperationException("calling script hash unavailable");

            await ApplyL1TxFilter(engine, targetChainId, sender, receiver, messageType, payload);

            var nonceKey = Key(PrefixL1ToL2Nonce, targetChainId);
            var nonceItem = engine.SnapshotCache.GetAndChange(nonceKey, () => new StorageItem(BigInteger.Zero));
            var nonce = (ulong)(BigInteger)nonceItem + 1UL;
            nonceItem.Set(new BigInteger(nonce));

            Put(engine.SnapshotCache, Key(PrefixL1ToL2Msg, targetChainId, nonce),
                EncodeMessage(0, targetChainId, nonce, sender, receiver, messageType, payload));
            Notify(engine, "L1ToL2Enqueued", targetChainId, nonce, sender, receiver);
            return nonce;
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public byte[] GetL1ToL2(IReadOnlyStore snapshot, uint chainId, ulong nonce)
        {
            return snapshot.TryGet(Key(PrefixL1ToL2Msg, chainId, nonce), out var item) ? item.Value.ToArray() : EmptyBytes;
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
        private void SetL1TxFilter(ApplicationEngine engine, uint targetChainId, UInt160 filter)
        {
            AssertOwner(engine);
            if (targetChainId == 0) throw new ArgumentOutOfRangeException(nameof(targetChainId), "targetChainId 0 is reserved for L1.");
            RequireNonZero(filter, nameof(filter));
            WriteUInt160(engine.SnapshotCache, FilterKey(targetChainId), filter);
            Notify(engine, "L1TxFilterSet", targetChainId, filter);
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
        private void ClearL1TxFilter(ApplicationEngine engine, uint targetChainId)
        {
            AssertOwner(engine);
            if (targetChainId == 0) throw new ArgumentOutOfRangeException(nameof(targetChainId), "targetChainId 0 is reserved for L1.");
            engine.SnapshotCache.Delete(FilterKey(targetChainId));
            Notify(engine, "L1TxFilterSet", targetChainId, UInt160.Zero);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public UInt160 GetL1TxFilter(IReadOnlyStore snapshot, uint targetChainId)
        {
            return ReadUInt160(snapshot, FilterKey(targetChainId));
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
        private void PublishMessageRoots(ApplicationEngine engine, uint chainId, ulong batchNumber, UInt256 l2ToL1Root, UInt256 l2ToL2Root)
        {
            AssertSettlementManager(engine);
            Put(engine.SnapshotCache, Key(PrefixL2ToL1Root, chainId, batchNumber), l2ToL1Root.ToArray());
            Put(engine.SnapshotCache, Key(PrefixL2ToL2Root, chainId, batchNumber), l2ToL2Root.ToArray());
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public UInt256 GetL2ToL1Root(IReadOnlyStore snapshot, uint chainId, ulong batchNumber)
        {
            return snapshot.TryGet(Key(PrefixL2ToL1Root, chainId, batchNumber), out var item) ? new UInt256(item.Value.Span) : UInt256.Zero;
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public UInt256 GetL2ToL2Root(IReadOnlyStore snapshot, uint chainId, ulong batchNumber)
        {
            return snapshot.TryGet(Key(PrefixL2ToL2Root, chainId, batchNumber), out var item) ? new UInt256(item.Value.Span) : UInt256.Zero;
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
        private void PublishGlobalRoot(ApplicationEngine engine, ulong batchEpoch, UInt256 globalRoot)
        {
            AssertSettlementManager(engine);
            if (globalRoot == UInt256.Zero) throw new ArgumentException("global root must be non-zero.", nameof(globalRoot));
            var key = CreateStorageKey(PrefixGlobalRoot, U64Le(batchEpoch));
            if (engine.SnapshotCache.Contains(key)) throw new InvalidOperationException("global root already published for this epoch");
            Put(engine.SnapshotCache, key, globalRoot.ToArray());
            Notify(engine, "GlobalRootPublished", batchEpoch, globalRoot);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public UInt256 GetGlobalRoot(IReadOnlyStore snapshot, ulong batchEpoch)
        {
            return snapshot.TryGet(CreateStorageKey(PrefixGlobalRoot, U64Le(batchEpoch)), out var item) ? new UInt256(item.Value.Span) : UInt256.Zero;
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
        private void MarkConsumed(ApplicationEngine engine, uint sourceChainId, UInt256 messageHash)
        {
            AssertSettlementManager(engine);
            var key = ConsumedKey(messageHash);
            if (engine.SnapshotCache.Contains(key)) throw new InvalidOperationException("already consumed");
            Put(engine.SnapshotCache, key, [1]);
            Notify(engine, "L2ToL1Consumed", sourceChainId, messageHash);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public bool IsConsumed(IReadOnlyStore snapshot, UInt256 messageHash)
        {
            return snapshot.TryGet(ConsumedKey(messageHash), out _);
        }

        private async ContractTask ApplyL1TxFilter(ApplicationEngine engine, uint targetChainId, UInt160 sender, UInt160 receiver, byte messageType, byte[] payload)
        {
            var filter = GetL1TxFilter(engine.SnapshotCache, targetChainId);
            if (filter == UInt160.Zero) return;
            var accepted = await engine.CallFromNativeContractAsync<bool>(
                Hash, filter, "acceptL1ToL2", targetChainId, sender.ToArray(), receiver.ToArray(), messageType, payload);
            if (!accepted) throw new InvalidOperationException("L1->L2 message rejected by filter");
        }

        private void AssertSettlementManager(ApplicationEngine engine)
        {
            var settlementManager = ReadUInt160(engine.SnapshotCache, PrefixSettlementManager);
            if (settlementManager == UInt160.Zero) throw new InvalidOperationException("sm unset");
            if (!engine.CheckWitnessInternal(settlementManager) && engine.CallingScriptHash != settlementManager)
                throw new InvalidOperationException("not settlement manager");
        }

        private StorageKey FilterKey(uint chainId) => Key(PrefixL1TxFilter, chainId);

        private StorageKey ConsumedKey(UInt256 hash) => CreateStorageKey(PrefixConsumed, hash.ToArray());

        private static byte[] EncodeMessage(uint sourceChainId, uint targetChainId, ulong nonce, UInt160 sender, UInt160 receiver, byte messageType, byte[] payload)
        {
            var bytes = new byte[4 + 4 + 8 + UInt160.Length + UInt160.Length + 1 + 4 + payload.Length];
            var offset = 0;
            U32Le(sourceChainId).CopyTo(bytes, offset);
            offset += 4;
            U32Le(targetChainId).CopyTo(bytes, offset);
            offset += 4;
            U64Le(nonce).CopyTo(bytes, offset);
            offset += 8;
            sender.ToArray().CopyTo(bytes, offset);
            offset += UInt160.Length;
            receiver.ToArray().CopyTo(bytes, offset);
            offset += UInt160.Length;
            bytes[offset++] = messageType;
            U32Le((uint)payload.Length).CopyTo(bytes, offset);
            offset += 4;
            payload.CopyTo(bytes, offset);
            return bytes;
        }
    }
}
