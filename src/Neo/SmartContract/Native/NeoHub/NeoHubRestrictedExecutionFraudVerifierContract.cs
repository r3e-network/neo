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

    public sealed class NeoHubRestrictedExecutionFraudVerifierContract : NeoHubNativeContract
    {
        public const int V1HeaderSize = 1 + UInt256.Length * 3 + 4;
        public const int V2HeaderSize = V1HeaderSize + 4;
        public const byte SupportedVersion3 = 3;
        public const int MaxDisputedTxBytes = 64 * 1024;
        public const int MaxStorageProofsPerPayload = 32;
        public const int MaxKeyBytes = 256;
        public const int MaxValueBytes = 4096;
        public const int MaxSiblingDepth = 64;
        public const byte ReasonBadLength = 1;
        public const byte ReasonBadVersion = 2;
        public const byte ReasonNoDiscrepancy = 3;
        public const byte ReasonOversizedWitness = 4;
        public const byte ReasonInvalidStorageProof = 5;
        public const byte ReasonProofCountInvalid = 6;
        public const byte ReasonPreStateRootMismatch = 7;
        public const byte ReasonReplayedPostStateRootMismatch = 8;

        private const int PreStateRootOffset = 1;
        private const int ClaimedRootOffset = PreStateRootOffset + UInt256.Length;
        private const int ReplayedRootOffset = ClaimedRootOffset + UInt256.Length;
        private const int DisputedTxLengthOffset = V1HeaderSize;

        [ContractEvent(0, name: "FraudProofAccepted", "chainId", ContractParameterType.Integer, "batchNumber", ContractParameterType.Integer, "claimedPostStateRoot", ContractParameterType.Hash256, "replayedPostStateRoot", ContractParameterType.Hash256)]
        [ContractEvent(1, name: "FraudProofRejected", "chainId", ContractParameterType.Integer, "batchNumber", ContractParameterType.Integer, "reason", ContractParameterType.Integer)]
        internal NeoHubRestrictedExecutionFraudVerifierContract() : base(-117) { }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        private bool VerifyFraud(ApplicationEngine engine, uint chainId, ulong batchNumber, byte[] payload)
        {
            ArgumentNullException.ThrowIfNull(payload);
            if (payload.Length < 1) return Reject(engine, chainId, batchNumber, ReasonBadLength);
            if (payload[0] != SupportedVersion3) return Reject(engine, chainId, batchNumber, ReasonBadVersion);
            if (payload.Length < V2HeaderSize) return Reject(engine, chainId, batchNumber, ReasonBadLength);

            var disputedTxLength = ReadU32Le(payload.AsSpan(DisputedTxLengthOffset, 4));
            if (disputedTxLength > MaxDisputedTxBytes)
                return Reject(engine, chainId, batchNumber, ReasonOversizedWitness);

            var position = V2HeaderSize + checked((int)disputedTxLength);
            if (payload.Length < position + 4)
                return Reject(engine, chainId, batchNumber, ReasonBadLength);

            if (BytesEqual(payload, ClaimedRootOffset, payload, ReplayedRootOffset, UInt256.Length))
                return Reject(engine, chainId, batchNumber, ReasonNoDiscrepancy);

            var proofCount = ReadU32Le(payload.AsSpan(position, 4));
            position += 4;
            if (proofCount == 0 || proofCount > MaxStorageProofsPerPayload)
                return Reject(engine, chainId, batchNumber, ReasonProofCountInvalid);

            for (var i = 0u; i < proofCount; i++)
            {
                if (payload.Length < position + 2)
                    return Reject(engine, chainId, batchNumber, ReasonBadLength);
                var keyLength = ReadU16Le(payload.AsSpan(position, 2));
                position += 2;
                if (keyLength > MaxKeyBytes)
                    return Reject(engine, chainId, batchNumber, ReasonInvalidStorageProof);
                if (payload.Length < position + keyLength)
                    return Reject(engine, chainId, batchNumber, ReasonBadLength);
                var keyOffset = position;
                position += keyLength;

                if (payload.Length < position + 4)
                    return Reject(engine, chainId, batchNumber, ReasonBadLength);
                var preValueLength = ReadU32Le(payload.AsSpan(position, 4));
                position += 4;
                if (preValueLength > MaxValueBytes)
                    return Reject(engine, chainId, batchNumber, ReasonInvalidStorageProof);
                if (payload.Length < position + checked((int)preValueLength))
                    return Reject(engine, chainId, batchNumber, ReasonBadLength);
                var preValueOffset = position;
                position += (int)preValueLength;

                if (payload.Length < position + 4)
                    return Reject(engine, chainId, batchNumber, ReasonBadLength);
                var postValueLength = ReadU32Le(payload.AsSpan(position, 4));
                position += 4;
                if (postValueLength > MaxValueBytes)
                    return Reject(engine, chainId, batchNumber, ReasonInvalidStorageProof);
                if (payload.Length < position + checked((int)postValueLength))
                    return Reject(engine, chainId, batchNumber, ReasonBadLength);
                var postValueOffset = position;
                position += (int)postValueLength;

                if (payload.Length < position + 8)
                    return Reject(engine, chainId, batchNumber, ReasonBadLength);
                var leafIndex = ReadU64Le(payload.AsSpan(position, 8));
                position += 8;

                if (payload.Length < position + 1)
                    return Reject(engine, chainId, batchNumber, ReasonBadLength);
                var preSiblingCount = payload[position++];
                if (preSiblingCount > MaxSiblingDepth)
                    return Reject(engine, chainId, batchNumber, ReasonInvalidStorageProof);
                if (payload.Length < position + UInt256.Length * preSiblingCount)
                    return Reject(engine, chainId, batchNumber, ReasonBadLength);
                var preSiblingOffset = position;
                position += UInt256.Length * preSiblingCount;

                if (payload.Length < position + 1)
                    return Reject(engine, chainId, batchNumber, ReasonBadLength);
                var postSiblingCount = payload[position++];
                if (postSiblingCount > MaxSiblingDepth)
                    return Reject(engine, chainId, batchNumber, ReasonInvalidStorageProof);
                if (payload.Length < position + UInt256.Length * postSiblingCount)
                    return Reject(engine, chainId, batchNumber, ReasonBadLength);
                var postSiblingOffset = position;
                position += UInt256.Length * postSiblingCount;

                var preLeaf = HashEntry(payload, keyOffset, keyLength, preValueOffset, (int)preValueLength);
                var preRoot = FoldMerkleProof(preLeaf, payload, preSiblingOffset, preSiblingCount, leafIndex);
                if (!BytesEqual(preRoot, 0, payload, PreStateRootOffset, UInt256.Length))
                    return Reject(engine, chainId, batchNumber, ReasonPreStateRootMismatch);

                var postLeaf = HashEntry(payload, keyOffset, keyLength, postValueOffset, (int)postValueLength);
                var postRoot = FoldMerkleProof(postLeaf, payload, postSiblingOffset, postSiblingCount, leafIndex);
                if (!BytesEqual(postRoot, 0, payload, ReplayedRootOffset, UInt256.Length))
                    return Reject(engine, chainId, batchNumber, ReasonReplayedPostStateRootMismatch);
            }

            if (position != payload.Length)
                return Reject(engine, chainId, batchNumber, ReasonBadLength);

            Notify(engine, "FraudProofAccepted", chainId, batchNumber,
                ReadUInt256(payload, ClaimedRootOffset), ReadUInt256(payload, ReplayedRootOffset));
            return true;
        }

        private bool Reject(ApplicationEngine engine, uint chainId, ulong batchNumber, byte reason)
        {
            Notify(engine, "FraudProofRejected", chainId, batchNumber, reason);
            return false;
        }

        private static UInt256 ReadUInt256(byte[] data, int offset)
        {
            if (data.Length < offset + UInt256.Length) throw new ArgumentException("payload too small.", nameof(data));
            return new UInt256(data.AsSpan(offset, UInt256.Length));
        }

        private static ushort ReadU16Le(ReadOnlySpan<byte> bytes)
        {
            return (ushort)(bytes[0] | (bytes[1] << 8));
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

        private static byte[] HashEntry(byte[] payload, int keyOffset, int keyLength, int valueOffset, int valueLength)
        {
            var bytes = new byte[4 + keyLength + 4 + valueLength];
            WriteU32Le(bytes, 0, (uint)keyLength);
            System.Buffer.BlockCopy(payload, keyOffset, bytes, 4, keyLength);
            WriteU32Le(bytes, 4 + keyLength, (uint)valueLength);
            System.Buffer.BlockCopy(payload, valueOffset, bytes, 8 + keyLength, valueLength);
            return Crypto.Hash256(bytes);
        }

        private static byte[] FoldMerkleProof(byte[] leafHash, byte[] payload, int siblingOffset, int siblingCount, ulong leafIndex)
        {
            var current = leafHash;
            var index = leafIndex;
            for (var i = 0; i < siblingCount; i++)
            {
                var combined = new byte[UInt256.Length * 2];
                var sourceOffset = siblingOffset + UInt256.Length * i;
                if ((index & 1UL) == 0UL)
                {
                    System.Buffer.BlockCopy(current, 0, combined, 0, UInt256.Length);
                    System.Buffer.BlockCopy(payload, sourceOffset, combined, UInt256.Length, UInt256.Length);
                }
                else
                {
                    System.Buffer.BlockCopy(payload, sourceOffset, combined, 0, UInt256.Length);
                    System.Buffer.BlockCopy(current, 0, combined, UInt256.Length, UInt256.Length);
                }
                current = Crypto.Hash256(combined);
                index >>= 1;
            }
            return current;
        }

        private static bool BytesEqual(byte[] left, int leftOffset, byte[] right, int rightOffset, int length)
        {
            var diff = 0;
            for (var i = 0; i < length; i++)
                diff |= left[leftOffset + i] ^ right[rightOffset + i];
            return diff == 0;
        }

        private static void WriteU32Le(byte[] bytes, int offset, uint value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
            bytes[offset + 2] = (byte)(value >> 16);
            bytes[offset + 3] = (byte)(value >> 24);
        }
    }

}
