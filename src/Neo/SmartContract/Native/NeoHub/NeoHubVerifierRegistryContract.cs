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

    public sealed class NeoHubVerifierRegistryContract : NeoHubNativeContract
    {
        private const byte PrefixVerifier = 0x01;
        private const byte KeyGovernanceController = 0x02;
        private const byte PrefixConsumedProposal = 0x03;
        private const byte KeyOwner = 0xff;

        public const int ProofTypeOffset = 316;

        [ContractEvent(0, name: "VerifierRegistered", "proofType", ContractParameterType.Integer, "verifier", ContractParameterType.Hash160)]
        internal NeoHubVerifierRegistryContract() : base(-105) { }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
        private void Configure(ApplicationEngine engine, UInt160 owner)
        {
            RequireNonZero(owner, nameof(owner));
            AssertOwnerOrCommittee(engine);
            WriteUInt160(engine.SnapshotCache, KeyOwner, owner);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public UInt160 GetOwner(IReadOnlyStore snapshot) => ReadUInt160(snapshot, KeyOwner);

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
        private void RegisterVerifier(ApplicationEngine engine, byte proofType, UInt160 verifier)
        {
            AssertOwner(engine);
            WriteVerifier(engine, proofType, verifier);
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
        private void SetGovernanceController(ApplicationEngine engine, UInt160 governanceController)
        {
            AssertOwner(engine);
            RequireNonZero(governanceController, nameof(governanceController));
            WriteUInt160(engine.SnapshotCache, KeyGovernanceController, governanceController);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public UInt160 GetGovernanceController(IReadOnlyStore snapshot) => ReadUInt160(snapshot, KeyGovernanceController);

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowCall | CallFlags.AllowNotify)]
        private async ContractTask RegisterVerifierViaProposal(ApplicationEngine engine, byte proofType, UInt160 verifier, ulong proposalId)
        {
            var governanceController = GetGovernanceController(engine.SnapshotCache);
            RequireNonZero(governanceController, nameof(governanceController));

            var consumedKey = ProposalKey(proposalId);
            if (engine.SnapshotCache.Contains(consumedKey)) throw new InvalidOperationException("proposal already consumed");
            var approved = await engine.CallFromNativeContractAsync<bool>(Hash, governanceController, "isApprovedAndTimelocked", proposalId);
            if (!approved) throw new InvalidOperationException("proposal not approved + timelocked (council multisig + timelock not satisfied)");

            Put(engine.SnapshotCache, consumedKey, [1]);
            WriteVerifier(engine, proofType, verifier);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public UInt160 GetVerifier(IReadOnlyStore snapshot, byte proofType)
        {
            return ReadUInt160(snapshot, CreateStorageKey(PrefixVerifier, proofType));
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates | CallFlags.AllowCall)]
        private async ContractTask<bool> VerifyCommitment(ApplicationEngine engine, byte[] commitmentBytes)
        {
            if (commitmentBytes.Length <= ProofTypeOffset) throw new ArgumentException("commitment too small.", nameof(commitmentBytes));
            var proofType = commitmentBytes[ProofTypeOffset];
            var verifier = GetVerifier(engine.SnapshotCache, proofType);
            RequireNonZero(verifier, nameof(verifier));
            return await engine.CallFromNativeContractAsync<bool>(Hash, verifier, "verify", commitmentBytes);
        }

        private void WriteVerifier(ApplicationEngine engine, byte proofType, UInt160 verifier)
        {
            RequireNonZero(verifier, nameof(verifier));
            if (proofType < 1 || proofType > 3)
                throw new InvalidOperationException("proofType must be 1..3 (Multisig/Optimistic/Zk)");
            WriteUInt160(engine.SnapshotCache, CreateStorageKey(PrefixVerifier, proofType), verifier);
            Notify(engine, "VerifierRegistered", proofType, verifier);
        }

        private StorageKey ProposalKey(ulong proposalId)
        {
            return CreateStorageKey(PrefixConsumedProposal, U64Le(proposalId));
        }
    }

}
