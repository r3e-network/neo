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

    public sealed class NeoHubDARegistryContract : NeoHubNativeContract
    {
        private const byte PrefixCommitment = 0x01;
        private const byte PrefixMode = 0x02;
        private const byte PrefixSettlementManager = 0xfd;
        private const byte KeyOwner = 0xff;

        [ContractEvent(0, name: "CommitmentRecorded", "chainId", ContractParameterType.Integer, "batchNumber", ContractParameterType.Integer, "commitment", ContractParameterType.Hash256, "daMode", ContractParameterType.Integer)]
        internal NeoHubDARegistryContract() : base(-103) { }

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

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
        private void Record(ApplicationEngine engine, uint chainId, ulong batchNumber, UInt256 commitment, byte daMode)
        {
            var settlementManager = ReadUInt160(engine.SnapshotCache, PrefixSettlementManager);
            if (settlementManager == UInt160.Zero) throw new InvalidOperationException("sm unset");
            if (!engine.CheckWitnessInternal(settlementManager) && engine.CallingScriptHash != settlementManager)
                throw new InvalidOperationException("not settlement manager");
            if (daMode > 3) throw new ArgumentOutOfRangeException(nameof(daMode), "daMode must be 0..3 (L1/NeoFS/External/DAC).");

            Put(engine.SnapshotCache, Key(PrefixCommitment, chainId, batchNumber), commitment.ToArray());
            Put(engine.SnapshotCache, Key(PrefixMode, chainId, batchNumber), [daMode]);
            Notify(engine, "CommitmentRecorded", chainId, batchNumber, commitment, daMode);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public UInt256 GetCommitment(IReadOnlyStore snapshot, uint chainId, ulong batchNumber)
        {
            return snapshot.TryGet(Key(PrefixCommitment, chainId, batchNumber), out var item) ? new UInt256(item.Value.Span) : UInt256.Zero;
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public byte GetMode(IReadOnlyStore snapshot, uint chainId, ulong batchNumber)
        {
            return snapshot.TryGet(Key(PrefixMode, chainId, batchNumber), out var item) ? item.Value.Span[0] : (byte)0;
        }
    }

}
