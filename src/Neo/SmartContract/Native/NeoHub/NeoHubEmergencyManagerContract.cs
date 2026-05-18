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

    public sealed class NeoHubEmergencyManagerContract : NeoHubNativeContract
    {
        private const byte KeyPaused = 0x01;
        private const byte KeyEmergencyCouncil = 0x02;
        private const byte PrefixEscapeConsumed = 0x03;
        private const byte KeySettlementManager = 0x04;
        private const byte KeyOwner = 0xff;

        [ContractEvent(0, name: "PauseStateChanged", "paused", ContractParameterType.Boolean)]
        [ContractEvent(1, name: "EscapeHatchExit", "chainId", ContractParameterType.Integer, "sender", ContractParameterType.Hash160, "leafHash", ContractParameterType.Hash256)]
        internal NeoHubEmergencyManagerContract() : base(-110) { }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
        private void Configure(ApplicationEngine engine, UInt160 owner, UInt160 emergencyCouncil, UInt160 settlementManager)
        {
            RequireNonZero(owner, nameof(owner));
            RequireNonZero(emergencyCouncil, nameof(emergencyCouncil));
            RequireNonZero(settlementManager, nameof(settlementManager));
            AssertOwnerOrCommittee(engine);
            WriteUInt160(engine.SnapshotCache, KeyOwner, owner);
            WriteUInt160(engine.SnapshotCache, KeyEmergencyCouncil, emergencyCouncil);
            WriteUInt160(engine.SnapshotCache, KeySettlementManager, settlementManager);
            Put(engine.SnapshotCache, KeyPaused, [0]);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public UInt160 GetOwner(IReadOnlyStore snapshot) => ReadUInt160(snapshot, KeyOwner);

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public UInt160 GetEmergencyCouncil(IReadOnlyStore snapshot) => ReadUInt160(snapshot, KeyEmergencyCouncil);

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public UInt160 GetSettlementManager(IReadOnlyStore snapshot) => ReadUInt160(snapshot, KeySettlementManager);

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public bool IsPaused(IReadOnlyStore snapshot)
        {
            return snapshot.TryGet(CreateStorageKey(KeyPaused), out var item) && item.Value.Span[0] == 1;
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
        private void Pause(ApplicationEngine engine)
        {
            var council = GetEmergencyCouncil(engine.SnapshotCache);
            RequireNonZero(council, nameof(council));
            AssertWitness(engine, council, "not council");
            Put(engine.SnapshotCache, KeyPaused, [1]);
            Notify(engine, "PauseStateChanged", true);
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
        private void Resume(ApplicationEngine engine)
        {
            AssertOwner(engine);
            Put(engine.SnapshotCache, KeyPaused, [0]);
            Notify(engine, "PauseStateChanged", false);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public bool IsEscapeConsumed(IReadOnlyStore snapshot, uint chainId, UInt256 leafHash)
        {
            return snapshot.TryGet(EscapeKey(chainId, leafHash), out _);
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowCall | CallFlags.AllowNotify)]
        private async ContractTask EscapeHatchExit(ApplicationEngine engine, uint chainId, UInt160 sender, UInt256 leafHash)
        {
            AssertEscapeArgs(engine, chainId, sender, leafHash);
            var key = AssertEscapeNotConsumed(engine, chainId, leafHash);

            var settlementManager = GetSettlementManager(engine.SnapshotCache);
            RequireNonZero(settlementManager, nameof(settlementManager));
            var canonicalRoot = await engine.CallFromNativeContractAsync<UInt256>(
                Hash, settlementManager, "getCanonicalStateRoot", chainId);
            if (canonicalRoot is null || !canonicalRoot.Equals(leafHash))
                throw new InvalidOperationException("leaf does not match latest finalized state root");

            ConsumeEscape(engine, key, chainId, sender, leafHash);
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowCall | CallFlags.AllowNotify)]
        private async ContractTask EscapeHatchExitWithProof(
            ApplicationEngine engine,
            uint chainId,
            UInt160 sender,
            UInt256 leafHash,
            byte[][] siblings,
            ulong leafIndex)
        {
            AssertEscapeArgs(engine, chainId, sender, leafHash);
            var key = AssertEscapeNotConsumed(engine, chainId, leafHash);

            var settlementManager = GetSettlementManager(engine.SnapshotCache);
            RequireNonZero(settlementManager, nameof(settlementManager));
            var verified = await engine.CallFromNativeContractAsync<bool>(
                Hash, settlementManager, "verifyStateLeafWithProof", chainId, leafHash.ToArray(), ToStackArray(siblings), leafIndex);
            if (!verified) throw new InvalidOperationException("leaf does not Merkle-verify against latest finalized state root");

            ConsumeEscape(engine, key, chainId, sender, leafHash);
        }

        private void AssertEscapeArgs(ApplicationEngine engine, uint chainId, UInt160 sender, UInt256 leafHash)
        {
            if (!IsPaused(engine.SnapshotCache)) throw new InvalidOperationException("escape hatch only valid while paused");
            if (chainId == 0) throw new ArgumentOutOfRangeException(nameof(chainId), "chainId 0 is reserved for L1.");
            RequireNonZero(sender, nameof(sender));
            if (leafHash == UInt256.Zero) throw new ArgumentException("leaf hash must be non-zero.", nameof(leafHash));
            AssertWitness(engine, sender, "no witness");
        }

        private StorageKey AssertEscapeNotConsumed(ApplicationEngine engine, uint chainId, UInt256 leafHash)
        {
            var key = EscapeKey(chainId, leafHash);
            if (engine.SnapshotCache.Contains(key)) throw new InvalidOperationException("escape leaf already consumed");
            return key;
        }

        private void ConsumeEscape(ApplicationEngine engine, StorageKey key, uint chainId, UInt160 sender, UInt256 leafHash)
        {
            Put(engine.SnapshotCache, key, [1]);
            Notify(engine, "EscapeHatchExit", chainId, sender, leafHash);
        }

        private StorageKey EscapeKey(uint chainId, UInt256 leafHash)
        {
            var data = new byte[4 + UInt256.Length];
            U32Le(chainId).CopyTo(data, 0);
            leafHash.ToArray().CopyTo(data, 4);
            return CreateStorageKey(PrefixEscapeConsumed, data);
        }

        private static Neo.VM.Types.Array ToStackArray(byte[][] values)
        {
            ArgumentNullException.ThrowIfNull(values);
            var items = new StackItem[values.Length];
            for (var i = 0; i < values.Length; i++)
                items[i] = values[i] ?? throw new ArgumentException("sibling cannot be null.", nameof(values));
            return new Neo.VM.Types.Array(items);
        }
    }

}
