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

    public sealed class NeoHubSettlementManagerContract : NeoHubNativeContract
    {
        private const byte PrefixBatchStatus = 0x01;
        private const byte PrefixBatchHeader = 0x02;
        private const byte PrefixCanonicalRoot = 0x03;
        private const byte PrefixLatestBatch = 0x04;
        private const byte PrefixWithdrawalRoot = 0x05;
        private const byte PrefixOptimisticChallenge = 0x06;
        private const byte PrefixDARegistry = 0x07;
        private const byte PrefixDAValidator = 0x08;
        private const byte PrefixChainRegistry = 0xfc;
        private const byte PrefixVerifierRegistry = 0xfd;
        private const byte KeyOwner = 0xff;

        private const int DACommitmentOffset = 252;
        private const int ProofTypeOffset = 316;
        private const int ProofLenOffset = 317;
        private const int ProofBytesOffset = 321;
        private const int OptimisticSequencerOffsetInProof = 61;
        private const int OptimisticMinProofBytes = 85;
        private const int OptimisticMaxProofBytes = 1024 * 1024;
        private const byte ProofTypeOptimistic = 2;

        public const byte StatusUnknown = 0;
        public const byte StatusPending = 1;
        public const byte StatusChallengeable = 2;
        public const byte StatusFinalized = 3;
        public const byte StatusReverted = 4;
        public const int MaxProofDepth = 64;

        [ContractEvent(0, name: "BatchSubmitted", "chainId", ContractParameterType.Integer, "batchNumber", ContractParameterType.Integer, "postStateRoot", ContractParameterType.Hash256)]
        [ContractEvent(1, name: "BatchFinalized", "chainId", ContractParameterType.Integer, "batchNumber", ContractParameterType.Integer, "postStateRoot", ContractParameterType.Hash256)]
        [ContractEvent(2, name: "BatchReverted", "chainId", ContractParameterType.Integer, "batchNumber", ContractParameterType.Integer)]
        internal NeoHubSettlementManagerContract() : base(-107) { }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
        private void Configure(ApplicationEngine engine, UInt160 owner, UInt160 chainRegistry, UInt160 verifierRegistry)
        {
            RequireNonZero(owner, nameof(owner));
            RequireNonZero(chainRegistry, nameof(chainRegistry));
            RequireNonZero(verifierRegistry, nameof(verifierRegistry));
            AssertOwnerOrCommittee(engine);
            WriteUInt160(engine.SnapshotCache, KeyOwner, owner);
            WriteUInt160(engine.SnapshotCache, PrefixChainRegistry, chainRegistry);
            WriteUInt160(engine.SnapshotCache, PrefixVerifierRegistry, verifierRegistry);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public UInt160 GetOwner(IReadOnlyStore snapshot) => ReadUInt160(snapshot, KeyOwner);

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public UInt160 GetOptimisticChallenge(IReadOnlyStore snapshot) => ReadUInt160(snapshot, PrefixOptimisticChallenge);

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
        private void SetOptimisticChallenge(ApplicationEngine engine, UInt160 optimisticChallenge)
        {
            AssertOwner(engine);
            RequireNonZero(optimisticChallenge, nameof(optimisticChallenge));
            WriteUInt160(engine.SnapshotCache, PrefixOptimisticChallenge, optimisticChallenge);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public UInt160 GetDARegistry(IReadOnlyStore snapshot) => ReadUInt160(snapshot, PrefixDARegistry);

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
        private void SetDARegistry(ApplicationEngine engine, UInt160 daRegistry)
        {
            AssertOwner(engine);
            RequireNonZero(daRegistry, nameof(daRegistry));
            WriteUInt160(engine.SnapshotCache, PrefixDARegistry, daRegistry);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public UInt160 GetDAValidator(IReadOnlyStore snapshot) => ReadUInt160(snapshot, PrefixDAValidator);

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
        private void SetDAValidator(ApplicationEngine engine, UInt160 daValidator)
        {
            AssertOwner(engine);
            RequireNonZero(daValidator, nameof(daValidator));
            WriteUInt160(engine.SnapshotCache, PrefixDAValidator, daValidator);
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowCall | CallFlags.AllowNotify)]
        private async ContractTask SubmitBatch(ApplicationEngine engine, byte[] commitmentBytes)
        {
            if (commitmentBytes.Length < ProofTypeOffset + 1)
                throw new ArgumentException("commitment too small.", nameof(commitmentBytes));

            var chainId = ReadU32Le(commitmentBytes);
            var batchNumber = ReadU64Le(commitmentBytes.AsSpan(4));
            var chainRegistry = ReadUInt160(engine.SnapshotCache, PrefixChainRegistry);
            RequireNonZero(chainRegistry, nameof(chainRegistry));

            var isActive = await engine.CallFromNativeContractAsync<bool>(Hash, chainRegistry, "isActive", chainId);
            if (!isActive) throw new InvalidOperationException("chain inactive");

            var latest = GetLatestFinalizedBatch(engine.SnapshotCache, chainId);
            if (batchNumber != latest + 1UL) throw new InvalidOperationException("batch number out of sequence");

            var statusKey = StatusKey(chainId, batchNumber);
            if (engine.SnapshotCache.Contains(statusKey)) throw new InvalidOperationException("batch already submitted");

            var verifierRegistry = ReadUInt160(engine.SnapshotCache, PrefixVerifierRegistry);
            RequireNonZero(verifierRegistry, nameof(verifierRegistry));
            var verified = await engine.CallFromNativeContractAsync<bool>(Hash, verifierRegistry, "verifyCommitment", commitmentBytes);
            if (!verified) throw new InvalidOperationException("verifier rejected commitment");

            var daCommitment = ReadUInt256(commitmentBytes, DACommitmentOffset);
            var daMode = await GetChainDAMode(engine, chainRegistry, chainId);
            await RecordDataAvailability(engine, chainId, batchNumber, daCommitment, daMode);

            var proofType = commitmentBytes[ProofTypeOffset];
            var status = proofType == ProofTypeOptimistic ? StatusChallengeable : StatusPending;
            Put(engine.SnapshotCache, statusKey, [status]);
            Put(engine.SnapshotCache, BatchHeaderKey(chainId, batchNumber), commitmentBytes);

            var withdrawalRoot = ReadUInt256(commitmentBytes, 4 + 8 + 8 + 8 + UInt256.Length * 4);
            Put(engine.SnapshotCache, WithdrawalRootKey(chainId, batchNumber), withdrawalRoot.ToArray());

            if (proofType == ProofTypeOptimistic)
            {
                var optimisticChallenge = GetOptimisticChallenge(engine.SnapshotCache);
                RequireNonZero(optimisticChallenge, nameof(optimisticChallenge));
                var sequencer = ReadOptimisticSequencer(commitmentBytes);
                await engine.CallFromNativeContractAsync(Hash, optimisticChallenge, "openWindow", chainId, batchNumber, sequencer.ToArray());
            }

            var postStateRoot = ReadUInt256(commitmentBytes, 4 + 8 + 8 + 8 + UInt256.Length);
            Notify(engine, "BatchSubmitted", chainId, batchNumber, postStateRoot);
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowCall | CallFlags.AllowNotify)]
        private async ContractTask FinalizeBatch(ApplicationEngine engine, uint chainId, ulong batchNumber)
        {
            var key = StatusKey(chainId, batchNumber);
            var item = engine.SnapshotCache.TryGet(key) ?? throw new InvalidOperationException("batch unknown");
            var status = item.Value.Span[0];
            if (status != StatusPending && status != StatusChallengeable)
                throw new InvalidOperationException("batch not finalizable");

            if (status == StatusChallengeable)
            {
                var optimisticChallenge = GetOptimisticChallenge(engine.SnapshotCache);
                RequireNonZero(optimisticChallenge, nameof(optimisticChallenge));
                if (!engine.CheckWitnessInternal(optimisticChallenge))
                    throw new InvalidOperationException("challengeable batch finalization must come from OptimisticChallenge");
            }

            var header = engine.SnapshotCache.TryGet(BatchHeaderKey(chainId, batchNumber))?.Value.ToArray()
                ?? throw new InvalidOperationException("header missing");
            await ValidateDataAvailability(engine, chainId, batchNumber, header);
            var postStateRoot = ReadUInt256(header, 4 + 8 + 8 + 8 + UInt256.Length);

            Put(engine.SnapshotCache, key, [StatusFinalized]);
            Put(engine.SnapshotCache, CanonicalRootKey(chainId), postStateRoot.ToArray());
            SetLatestFinalizedBatch(engine.SnapshotCache, chainId, batchNumber);
            Notify(engine, "BatchFinalized", chainId, batchNumber, postStateRoot);
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
        private void RevertBatch(ApplicationEngine engine, uint chainId, ulong batchNumber)
        {
            var ownerAuthorized = engine.CheckWitnessInternal(GetOwner(engine.SnapshotCache));
            var optimisticChallenge = GetOptimisticChallenge(engine.SnapshotCache);
            var challengeAuthorized = optimisticChallenge != UInt160.Zero && engine.CheckWitnessInternal(optimisticChallenge);
            if (!ownerAuthorized && !challengeAuthorized) throw new InvalidOperationException("not authorized");

            if (challengeAuthorized && !ownerAuthorized)
            {
                var item = engine.SnapshotCache.TryGet(StatusKey(chainId, batchNumber))
                    ?? throw new InvalidOperationException("batch unknown");
                if (item.Value.Span[0] != StatusChallengeable)
                    throw new InvalidOperationException("OptimisticChallenge can only revert challengeable batches");
            }

            Put(engine.SnapshotCache, StatusKey(chainId, batchNumber), [StatusReverted]);
            Notify(engine, "BatchReverted", chainId, batchNumber);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public UInt256 GetCanonicalStateRoot(IReadOnlyStore snapshot, uint chainId)
        {
            return snapshot.TryGet(CanonicalRootKey(chainId), out var item) ? new UInt256(item.Value.Span) : UInt256.Zero;
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public byte GetBatchStatus(IReadOnlyStore snapshot, uint chainId, ulong batchNumber)
        {
            return snapshot.TryGet(StatusKey(chainId, batchNumber), out var item) ? item.Value.Span[0] : StatusUnknown;
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public ulong GetLatestFinalizedBatch(IReadOnlyStore snapshot, uint chainId)
        {
            return snapshot.TryGet(LatestBatchKey(chainId), out var item) ? (ulong)(BigInteger)item : 0UL;
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public bool VerifyWithdrawalLeaf(IReadOnlyStore snapshot, uint chainId, UInt256 leafHash)
        {
            var latest = GetLatestFinalizedBatch(snapshot, chainId);
            return VerifyWithdrawalLeafAt(snapshot, chainId, latest, leafHash);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public bool VerifyWithdrawalLeafAt(IReadOnlyStore snapshot, uint chainId, ulong batchNumber, UInt256 leafHash)
        {
            if (GetBatchStatus(snapshot, chainId, batchNumber) != StatusFinalized) return false;
            return snapshot.TryGet(WithdrawalRootKey(chainId, batchNumber), out var item)
                && new UInt256(item.Value.Span).Equals(leafHash);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public bool VerifyWithdrawalLeafWithProof(IReadOnlyStore snapshot, uint chainId, ulong batchNumber, UInt256 leafHash, byte[][] siblings, ulong leafIndex)
        {
            if (GetBatchStatus(snapshot, chainId, batchNumber) != StatusFinalized) return false;
            if (!snapshot.TryGet(WithdrawalRootKey(chainId, batchNumber), out var item)) return false;
            var storedRoot = new UInt256(item.Value.Span);
            return storedRoot.Equals(FoldMerkleProof(leafHash, siblings, leafIndex));
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public bool VerifyStateLeafWithProof(IReadOnlyStore snapshot, uint chainId, UInt256 leafHash, byte[][] siblings, ulong leafIndex)
        {
            var canonicalRoot = GetCanonicalStateRoot(snapshot, chainId);
            return canonicalRoot != UInt256.Zero && canonicalRoot.Equals(FoldMerkleProof(leafHash, siblings, leafIndex));
        }

        private async ContractTask RecordDataAvailability(ApplicationEngine engine, uint chainId, ulong batchNumber, UInt256 daCommitment, byte daMode)
        {
            if (daCommitment == UInt256.Zero) throw new ArgumentException("DA commitment must be non-zero.", nameof(daCommitment));
            if (daMode > 3) throw new ArgumentOutOfRangeException(nameof(daMode), "daMode must be 0..3.");
            var daRegistry = GetDARegistry(engine.SnapshotCache);
            RequireNonZero(daRegistry, nameof(daRegistry));
            await engine.CallFromNativeContractAsync(Hash, daRegistry, "record", chainId, batchNumber, daCommitment.ToArray(), daMode);
        }

        private async ContractTask ValidateDataAvailability(ApplicationEngine engine, uint chainId, ulong batchNumber, byte[] header)
        {
            var chainRegistry = ReadUInt160(engine.SnapshotCache, PrefixChainRegistry);
            RequireNonZero(chainRegistry, nameof(chainRegistry));
            var daMode = await GetChainDAMode(engine, chainRegistry, chainId);
            var daCommitment = ReadUInt256(header, DACommitmentOffset);
            var validator = GetDAValidator(engine.SnapshotCache);
            RequireNonZero(validator, nameof(validator));
            var ok = await engine.CallFromNativeContractAsync<bool>(Hash, validator, "validate", chainId, batchNumber, daCommitment.ToArray(), daMode);
            if (!ok) throw new InvalidOperationException("DA validator rejected commitment");
        }

        private async ContractTask<byte> GetChainDAMode(ApplicationEngine engine, UInt160 chainRegistry, uint chainId)
        {
            var mode = await engine.CallFromNativeContractAsync<BigInteger>(Hash, chainRegistry, "getDAMode", chainId);
            return (byte)mode;
        }

        private void SetLatestFinalizedBatch(DataCache snapshot, uint chainId, ulong batchNumber)
        {
            snapshot.GetAndChange(LatestBatchKey(chainId), () => new StorageItem(BigInteger.Zero))
                .Set(new BigInteger(batchNumber));
        }

        private StorageKey StatusKey(uint chainId, ulong batchNumber) => Key(PrefixBatchStatus, chainId, batchNumber);

        private StorageKey BatchHeaderKey(uint chainId, ulong batchNumber) => Key(PrefixBatchHeader, chainId, batchNumber);

        private StorageKey WithdrawalRootKey(uint chainId, ulong batchNumber) => Key(PrefixWithdrawalRoot, chainId, batchNumber);

        private StorageKey CanonicalRootKey(uint chainId) => Key(PrefixCanonicalRoot, chainId);

        private StorageKey LatestBatchKey(uint chainId) => Key(PrefixLatestBatch, chainId);

        private static UInt256 FoldMerkleProof(UInt256 leafHash, byte[][] siblings, ulong leafIndex)
        {
            ArgumentNullException.ThrowIfNull(siblings);
            if (siblings.Length > MaxProofDepth) throw new InvalidOperationException("proof too deep");

            var current = leafHash.ToArray();
            var index = leafIndex;
            foreach (var sibling in siblings)
            {
                if (sibling.Length != UInt256.Length) throw new ArgumentException("sibling must be 32 bytes.", nameof(siblings));
                var combined = new byte[UInt256.Length * 2];
                if ((index & 1UL) == 0UL)
                {
                    current.CopyTo(combined, 0);
                    sibling.CopyTo(combined, UInt256.Length);
                }
                else
                {
                    sibling.CopyTo(combined, 0);
                    current.CopyTo(combined, UInt256.Length);
                }
                current = Crypto.Hash256(combined);
                index >>= 1;
            }
            return new UInt256(current);
        }

        private static ulong ReadU64Le(ReadOnlySpan<byte> bytes)
        {
            return bytes[0]
                | ((ulong)bytes[1] << 8)
                | ((ulong)bytes[2] << 16)
                | ((ulong)bytes[3] << 24)
                | ((ulong)bytes[4] << 32)
                | ((ulong)bytes[5] << 40)
                | ((ulong)bytes[6] << 48)
                | ((ulong)bytes[7] << 56);
        }

        private static UInt256 ReadUInt256(byte[] data, int offset)
        {
            if (data.Length < offset + UInt256.Length) throw new ArgumentException("commitment too small.", nameof(data));
            return new UInt256(data.AsSpan(offset, UInt256.Length));
        }

        private static UInt160 ReadOptimisticSequencer(byte[] commitmentBytes)
        {
            if (commitmentBytes.Length < ProofBytesOffset) throw new ArgumentException("commitment missing proof length.", nameof(commitmentBytes));
            var proofLen = (int)ReadU32Le(commitmentBytes.AsSpan(ProofLenOffset));
            if (proofLen < OptimisticMinProofBytes) throw new InvalidOperationException("optimistic proof too small");
            if (proofLen > OptimisticMaxProofBytes) throw new InvalidOperationException("optimistic proof too large");
            if (ProofBytesOffset + proofLen != commitmentBytes.Length)
                throw new InvalidOperationException("commitment proof length mismatch");
            if (commitmentBytes[ProofBytesOffset] != 2) throw new InvalidOperationException("unsupported optimistic proof version");

            var sequencer = ReadUInt160(commitmentBytes, ProofBytesOffset + OptimisticSequencerOffsetInProof);
            RequireNonZero(sequencer, nameof(sequencer));
            return sequencer;
        }
    }

}
