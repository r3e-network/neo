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

    public sealed class NeoHubDAValidatorContract : NeoHubNativeContract
    {
        private const byte PrefixCommittee = 0x01;
        private const byte PrefixValidated = 0x02;
        private const byte KeyDARegistry = 0xfd;
        private const byte KeyOwner = 0xff;

        public const byte ModeL1 = 0;
        public const byte ModeNeoFS = 1;
        public const byte ModeExternal = 2;
        public const byte ModeDAC = 3;
        public const int PublicKeyLength = 33;
        public const int SignatureLength = 64;
        public const int MaxCommitteeSize = 64;

        [ContractEvent(0, name: "DACommitteeRegistered", "chainId", ContractParameterType.Integer, "threshold", ContractParameterType.Integer, "size", ContractParameterType.Integer)]
        [ContractEvent(1, name: "DAValidated", "chainId", ContractParameterType.Integer, "batchNumber", ContractParameterType.Integer, "commitment", ContractParameterType.Hash256, "daMode", ContractParameterType.Integer)]
        internal NeoHubDAValidatorContract() : base(-108) { }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
        private void Configure(ApplicationEngine engine, UInt160 owner, UInt160 daRegistry)
        {
            RequireNonZero(owner, nameof(owner));
            RequireNonZero(daRegistry, nameof(daRegistry));
            AssertOwnerOrCommittee(engine);
            WriteUInt160(engine.SnapshotCache, KeyOwner, owner);
            WriteUInt160(engine.SnapshotCache, KeyDARegistry, daRegistry);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public UInt160 GetOwner(IReadOnlyStore snapshot) => ReadUInt160(snapshot, KeyOwner);

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public UInt160 GetDARegistry(IReadOnlyStore snapshot) => ReadUInt160(snapshot, KeyDARegistry);

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
        private void RegisterCommittee(ApplicationEngine engine, uint chainId, byte threshold, byte[] committeeBlob)
        {
            AssertOwner(engine);
            if (chainId == 0) throw new ArgumentOutOfRangeException(nameof(chainId), "chainId 0 is reserved for L1.");
            if (threshold == 0) throw new ArgumentOutOfRangeException(nameof(threshold), "threshold must be positive.");
            if (committeeBlob.Length == 0) throw new ArgumentException("committee blob is empty.", nameof(committeeBlob));
            if (committeeBlob.Length % PublicKeyLength != 0)
                throw new ArgumentException("committee blob must contain 33-byte public keys.", nameof(committeeBlob));

            var size = committeeBlob.Length / PublicKeyLength;
            if (size > MaxCommitteeSize) throw new ArgumentException("committee too large.", nameof(committeeBlob));
            if (threshold > size) throw new ArgumentOutOfRangeException(nameof(threshold), "threshold exceeds committee size.");

            var stored = new byte[2 + committeeBlob.Length];
            stored[0] = threshold;
            stored[1] = (byte)size;
            committeeBlob.CopyTo(stored, 2);
            Put(engine.SnapshotCache, CommitteeKey(chainId), stored);
            Notify(engine, "DACommitteeRegistered", chainId, threshold, (byte)size);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public byte[] GetCommittee(IReadOnlyStore snapshot, uint chainId)
        {
            return snapshot.TryGet(CommitteeKey(chainId), out var item) ? item.Value.ToArray() : EmptyBytes;
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
        private bool SubmitAttestation(ApplicationEngine engine, uint chainId, ulong batchNumber, UInt256 commitment, byte daMode, byte[] proofBytes)
        {
            if (!VerifyAttestation(engine.SnapshotCache, chainId, batchNumber, commitment, daMode, proofBytes))
                throw new InvalidOperationException("DA attestation rejected");

            var value = new byte[1 + UInt256.Length];
            value[0] = daMode;
            commitment.ToArray().CopyTo(value, 1);
            Put(engine.SnapshotCache, ValidatedKey(chainId, batchNumber), value);
            Notify(engine, "DAValidated", chainId, batchNumber, commitment, daMode);
            return true;
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public bool IsValidated(IReadOnlyStore snapshot, uint chainId, ulong batchNumber, UInt256 commitment, byte daMode)
        {
            if (!snapshot.TryGet(ValidatedKey(chainId, batchNumber), out var item)) return false;
            var bytes = item.Value.Span;
            return bytes.Length == 1 + UInt256.Length
                && bytes[0] == daMode
                && bytes[1..].SequenceEqual(commitment.ToArray());
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public bool Validate(IReadOnlyStore snapshot, uint chainId, ulong batchNumber, UInt256 commitment, byte daMode)
        {
            if (chainId == 0 || daMode > ModeDAC || commitment == UInt256.Zero) return false;
            if (daMode == ModeL1 || daMode == ModeNeoFS || daMode == ModeExternal) return true;
            return IsValidated(snapshot, chainId, batchNumber, commitment, daMode);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public bool VerifyAttestation(IReadOnlyStore snapshot, uint chainId, ulong batchNumber, UInt256 commitment, byte daMode, byte[] proofBytes)
        {
            if (chainId == 0) throw new ArgumentOutOfRangeException(nameof(chainId), "chainId 0 is reserved for L1.");
            if (daMode != ModeDAC) throw new InvalidOperationException("attestations are only required for DAC mode");
            if (commitment == UInt256.Zero) throw new ArgumentException("commitment must be non-zero.", nameof(commitment));
            if (proofBytes.Length < 2) throw new ArgumentException("proof too short.", nameof(proofBytes));

            var committee = GetCommittee(snapshot, chainId);
            if (committee.Length == 0) throw new InvalidOperationException("no DA committee for chain");
            if (committee.Length < 2) throw new InvalidOperationException("committee malformed");
            var threshold = committee[0];
            var size = committee[1];
            if (threshold == 0 || threshold > size) throw new InvalidOperationException("committee threshold invalid");
            if (committee.Length != 2 + size * PublicKeyLength) throw new InvalidOperationException("committee length mismatch");

            var sigCount = proofBytes[0] | (proofBytes[1] << 8);
            if (sigCount < threshold) throw new InvalidOperationException("signature count below threshold");
            if (proofBytes.Length != 2 + sigCount * (1 + SignatureLength))
                throw new ArgumentException("proof length mismatch.", nameof(proofBytes));

            var message = BuildAttestationMessage(chainId, batchNumber, commitment, daMode);
            var seen = new byte[(MaxCommitteeSize + 7) / 8];
            var valid = 0;
            for (var i = 0; i < sigCount; i++)
            {
                var offset = 2 + i * (1 + SignatureLength);
                var signerIndex = proofBytes[offset];
                if (signerIndex >= size) throw new InvalidOperationException("signer index outside committee");

                var byteIndex = signerIndex / 8;
                var bit = (byte)(1 << (signerIndex % 8));
                if ((seen[byteIndex] & bit) != 0) throw new InvalidOperationException("duplicate signer");
                seen[byteIndex] = (byte)(seen[byteIndex] | bit);

                var pubkeyOffset = 2 + signerIndex * PublicKeyLength;
                var pubkey = committee.AsSpan(pubkeyOffset, PublicKeyLength).ToArray();
                var signature = proofBytes.AsSpan(offset + 1, SignatureLength).ToArray();
                if (!Crypto.VerifySignature(message, signature, pubkey, ECCurve.Secp256r1, HashAlgorithm.SHA256))
                    throw new InvalidOperationException("signature verification failed");
                valid++;
            }

            return valid >= threshold;
        }

        private StorageKey CommitteeKey(uint chainId) => Key(PrefixCommittee, chainId);

        private StorageKey ValidatedKey(uint chainId, ulong batchNumber) => Key(PrefixValidated, chainId, batchNumber);

        private static byte[] BuildAttestationMessage(uint chainId, ulong batchNumber, UInt256 commitment, byte daMode)
        {
            var bytes = new byte[4 + 4 + 8 + UInt256.Length + 1];
            var offset = 0;
            bytes[offset++] = 0x4e;
            bytes[offset++] = 0x34;
            bytes[offset++] = 0x44;
            bytes[offset++] = 0x41;
            U32Le(chainId).CopyTo(bytes, offset);
            offset += 4;
            U64Le(batchNumber).CopyTo(bytes, offset);
            offset += 8;
            commitment.ToArray().CopyTo(bytes, offset);
            offset += UInt256.Length;
            bytes[offset] = daMode;
            return bytes;
        }
    }

}
