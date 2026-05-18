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

    public sealed class NeoHubGovernanceFraudVerifierContract : NeoHubNativeContract
    {
        public const int FraudProofPayloadSize = 1 + UInt256.Length * 3 + 4;
        public const int V2HeaderSize = FraudProofPayloadSize + 4;
        public const int MaxDisputedTxBytes = 64 * 1024;
        public const byte SupportedVersion = 1;
        public const byte SupportedVersion2 = 2;
        public const byte ReasonBadLength = 1;
        public const byte ReasonBadVersion = 2;
        public const byte ReasonNoDiscrepancy = 3;
        public const byte ReasonOversizedWitness = 4;

        private const int ClaimedRootOffset = 1 + UInt256.Length;
        private const int ReplayedRootOffset = 1 + UInt256.Length * 2;
        private const int V2WitnessLengthOffset = FraudProofPayloadSize;

        [ContractEvent(0, name: "FraudProofAccepted", "chainId", ContractParameterType.Integer, "batchNumber", ContractParameterType.Integer, "claimedPostStateRoot", ContractParameterType.Hash256, "replayedPostStateRoot", ContractParameterType.Hash256)]
        [ContractEvent(1, name: "FraudProofRejected", "chainId", ContractParameterType.Integer, "batchNumber", ContractParameterType.Integer, "reason", ContractParameterType.Integer)]
        internal NeoHubGovernanceFraudVerifierContract() : base(-116) { }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        private bool VerifyFraud(ApplicationEngine engine, uint chainId, ulong batchNumber, byte[] payload)
        {
            ArgumentNullException.ThrowIfNull(payload);
            if (payload.Length < 1) return Reject(engine, chainId, batchNumber, ReasonBadLength);

            var version = payload[0];
            if (version == SupportedVersion)
            {
                if (payload.Length != FraudProofPayloadSize)
                    return Reject(engine, chainId, batchNumber, ReasonBadLength);
            }
            else if (version == SupportedVersion2)
            {
                if (payload.Length < V2HeaderSize)
                    return Reject(engine, chainId, batchNumber, ReasonBadLength);

                var declaredLength = ReadU32Le(payload.AsSpan(V2WitnessLengthOffset, 4));
                if (declaredLength > MaxDisputedTxBytes)
                    return Reject(engine, chainId, batchNumber, ReasonOversizedWitness);
                if (payload.Length != V2HeaderSize + checked((int)declaredLength))
                    return Reject(engine, chainId, batchNumber, ReasonBadLength);
            }
            else
            {
                return Reject(engine, chainId, batchNumber, ReasonBadVersion);
            }

            if (BytesEqual(payload, ClaimedRootOffset, ReplayedRootOffset, UInt256.Length))
                return Reject(engine, chainId, batchNumber, ReasonNoDiscrepancy);

            var claimedRoot = ReadUInt256(payload, ClaimedRootOffset);
            var replayedRoot = ReadUInt256(payload, ReplayedRootOffset);
            Notify(engine, "FraudProofAccepted", chainId, batchNumber, claimedRoot, replayedRoot);
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

        private static bool BytesEqual(byte[] data, int leftOffset, int rightOffset, int length)
        {
            var diff = 0;
            for (var i = 0; i < length; i++)
                diff |= data[leftOffset + i] ^ data[rightOffset + i];
            return diff == 0;
        }
    }

}
