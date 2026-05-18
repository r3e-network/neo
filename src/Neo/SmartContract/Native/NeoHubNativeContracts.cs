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
    /// <summary>
    /// Base class for NeoHub L1 native contracts.
    /// </summary>
    public abstract class NeoHubNativeContract : NativeContract
    {
        private protected NeoHubNativeContract(int id) : base(id) { }

        protected UInt160 ReadUInt160(IReadOnlyStore snapshot, byte prefix)
        {
            return snapshot.TryGet(CreateStorageKey(prefix), out var item) ? new UInt160(item.Value.Span) : UInt160.Zero;
        }

        protected static UInt160 ReadUInt160(IReadOnlyStore snapshot, StorageKey key)
        {
            return snapshot.TryGet(key, out var item) ? new UInt160(item.Value.Span) : UInt160.Zero;
        }

        protected void WriteUInt160(DataCache snapshot, byte prefix, UInt160 value)
        {
            snapshot.GetAndChange(CreateStorageKey(prefix), () => new StorageItem()).Value = value.ToArray();
        }

        protected static void WriteUInt160(DataCache snapshot, StorageKey key, UInt160 value)
        {
            snapshot.GetAndChange(key, () => new StorageItem()).Value = value.ToArray();
        }

        protected static void Put(DataCache snapshot, StorageKey key, byte[] value)
        {
            snapshot.GetAndChange(key, () => new StorageItem()).Value = value;
        }

        protected void Put(DataCache snapshot, byte prefix, byte[] value)
        {
            snapshot.GetAndChange(CreateStorageKey(prefix), () => new StorageItem()).Value = value;
        }

        protected static void RequireNonZero(UInt160 value, string name)
        {
            if (value == UInt160.Zero) throw new ArgumentException($"{name} must be non-zero.", name);
        }

        protected static void AssertWitness(ApplicationEngine engine, UInt160 account, string message)
        {
            if (!engine.CheckWitnessInternal(account)) throw new InvalidOperationException(message);
        }

        protected void AssertOwner(ApplicationEngine engine, byte ownerPrefix = 0xff)
        {
            var owner = ReadUInt160(engine.SnapshotCache, ownerPrefix);
            AssertWitness(engine, owner, "not authorized");
        }

        protected void AssertOwnerOrCommittee(ApplicationEngine engine, byte ownerPrefix = 0xff)
        {
            var owner = ReadUInt160(engine.SnapshotCache, ownerPrefix);
            if (owner == UInt160.Zero)
            {
                AssertCommittee(engine);
                return;
            }
            AssertWitness(engine, owner, "not authorized");
        }

        protected StorageKey Key(byte prefix, UInt160 value)
        {
            return CreateStorageKey(prefix, value.ToArray());
        }

        protected StorageKey Key(byte prefix, uint value)
        {
            return CreateStorageKey(prefix, U32Le(value));
        }

        protected StorageKey Key(byte prefix, UInt160 left, uint right)
        {
            var data = new byte[UInt160.Length + 4];
            left.ToArray().CopyTo(data, 0);
            U32Le(right).CopyTo(data, UInt160.Length);
            return CreateStorageKey(prefix, data);
        }

        protected StorageKey Key(byte prefix, uint left, ulong right)
        {
            var data = new byte[12];
            U32Le(left).CopyTo(data, 0);
            U64Le(right).CopyTo(data, 4);
            return CreateStorageKey(prefix, data);
        }

        protected static byte[] U32Le(uint value)
        {
            return [(byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24)];
        }

        protected static byte[] U64Le(ulong value)
        {
            return
            [
                (byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24),
                (byte)(value >> 32), (byte)(value >> 40), (byte)(value >> 48), (byte)(value >> 56)
            ];
        }

        protected static uint ReadU32Le(ReadOnlySpan<byte> bytes)
        {
            return bytes[0]
                | ((uint)bytes[1] << 8)
                | ((uint)bytes[2] << 16)
                | ((uint)bytes[3] << 24);
        }

        protected static UInt160 ReadUInt160(ReadOnlySpan<byte> bytes, int offset)
        {
            return new UInt160(bytes.Slice(offset, UInt160.Length));
        }

        protected static byte[] EmptyBytes => [];
    }

    public sealed class NeoHubChainRegistryContract : NeoHubNativeContract
    {
        private const byte PrefixConfig = 0x01;
        private const byte PrefixChainIndex = 0x02;
        private const byte KeyGovernanceController = 0x03;
        private const byte KeyOwner = 0xff;

        public const int ConfigSize = 4 + 20 * 4 + 7;
        public const int OffsetSecurityLevel = 84;
        public const int OffsetDAMode = 85;
        public const int OffsetGatewayEnabled = 86;
        public const int OffsetPermissionlessExit = 87;
        public const int OffsetSequencerModel = 88;
        public const int OffsetExitModel = 89;

        [ContractEvent(0, name: "ChainRegistered", "chainId", ContractParameterType.Integer, "config", ContractParameterType.ByteArray)]
        [ContractEvent(1, name: "ChainPaused", "chainId", ContractParameterType.Integer)]
        [ContractEvent(2, name: "ChainResumed", "chainId", ContractParameterType.Integer)]
        internal NeoHubChainRegistryContract() : base(-101) { }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
        private void Configure(ApplicationEngine engine, UInt160 owner)
        {
            RequireNonZero(owner, nameof(owner));
            AssertOwnerOrCommittee(engine);
            WriteUInt160(engine.SnapshotCache, KeyOwner, owner);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public UInt160 GetOwner(IReadOnlyStore snapshot)
        {
            return ReadUInt160(snapshot, KeyOwner);
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
        private void SetOwner(ApplicationEngine engine, UInt160 newOwner)
        {
            RequireNonZero(newOwner, nameof(newOwner));
            AssertOwner(engine);
            WriteUInt160(engine.SnapshotCache, KeyOwner, newOwner);
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
        private void RegisterChain(ApplicationEngine engine, uint chainId, byte[] configBytes)
        {
            AssertOwner(engine);
            WriteChainConfig(engine, chainId, configBytes);
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowCall | CallFlags.AllowNotify)]
        private async ContractTask RegisterChainPublic(ApplicationEngine engine, uint chainId, byte[] configBytes)
        {
            var governanceController = GetGovernanceController(engine.SnapshotCache);
            RequireNonZero(governanceController, nameof(governanceController));

            var mode = (byte)await engine.CallFromNativeContractAsync<BigInteger>(Hash, governanceController, "getAdmissionMode");
            if (mode == 0)
                throw new InvalidOperationException("admission mode = permissioned; use RegisterChain (owner-only)");

            if (mode == 1)
            {
                if (configBytes.Length < 64) throw new ArgumentException("config too short for verifier+bridge read.", nameof(configBytes));
                var verifier = ReadUInt160(configBytes, 24);
                var bridge = ReadUInt160(configBytes, 44);
                var verifierApproved = await engine.CallFromNativeContractAsync<bool>(Hash, governanceController, "isApprovedVerifier", verifier.ToArray());
                if (!verifierApproved)
                    throw new InvalidOperationException("verifier not in GovernanceController approved set (semi-permissionless mode)");
                var bridgeApproved = await engine.CallFromNativeContractAsync<bool>(Hash, governanceController, "isApprovedBridgeAdapter", bridge.ToArray());
                if (!bridgeApproved)
                    throw new InvalidOperationException("bridge adapter not in GovernanceController approved set (semi-permissionless mode)");
            }

            WriteChainConfig(engine, chainId, configBytes);
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
        private void SetGovernanceController(ApplicationEngine engine, UInt160 governanceController)
        {
            AssertOwner(engine);
            RequireNonZero(governanceController, nameof(governanceController));
            WriteUInt160(engine.SnapshotCache, KeyGovernanceController, governanceController);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public UInt160 GetGovernanceController(IReadOnlyStore snapshot)
        {
            return ReadUInt160(snapshot, KeyGovernanceController);
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
        private void UpdateChain(ApplicationEngine engine, uint chainId, byte[] configBytes)
        {
            AssertOwner(engine);
            if (chainId == 0) throw new ArgumentOutOfRangeException(nameof(chainId), "chainId 0 is reserved for L1.");
            if (configBytes.Length != ConfigSize) throw new ArgumentException("config size mismatch.", nameof(configBytes));
            if (ReadU32Le(configBytes) != chainId) throw new ArgumentException("chainId mismatch.", nameof(configBytes));
            if (!engine.SnapshotCache.Contains(Key(PrefixConfig, chainId))) throw new InvalidOperationException("chain not registered");

            Put(engine.SnapshotCache, Key(PrefixConfig, chainId), configBytes);
            Notify(engine, "ChainRegistered", chainId, configBytes);
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
        private void PauseChain(ApplicationEngine engine, uint chainId)
        {
            AssertOwner(engine);
            var key = Key(PrefixConfig, chainId);
            var item = engine.SnapshotCache.GetAndChange(key) ?? throw new InvalidOperationException("chain not registered");
            var bytes = item.Value.ToArray();
            bytes[ConfigSize - 1] = 0;
            item.Value = bytes;
            Notify(engine, "ChainPaused", chainId);
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
        private void ResumeChain(ApplicationEngine engine, uint chainId)
        {
            AssertOwner(engine);
            var key = Key(PrefixConfig, chainId);
            var item = engine.SnapshotCache.GetAndChange(key) ?? throw new InvalidOperationException("chain not registered");
            var bytes = item.Value.ToArray();
            bytes[ConfigSize - 1] = 1;
            item.Value = bytes;
            Notify(engine, "ChainResumed", chainId);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public byte[] GetChainConfig(IReadOnlyStore snapshot, uint chainId)
        {
            return snapshot.TryGet(Key(PrefixConfig, chainId), out var item) ? item.Value.ToArray() : EmptyBytes;
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public bool IsActive(IReadOnlyStore snapshot, uint chainId)
        {
            var config = GetChainConfig(snapshot, chainId);
            return config.Length == ConfigSize && config[ConfigSize - 1] == 1;
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public byte GetSecurityLevel(IReadOnlyStore snapshot, uint chainId) => ReadConfigByte(snapshot, chainId, OffsetSecurityLevel);

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public byte GetDAMode(IReadOnlyStore snapshot, uint chainId) => ReadConfigByte(snapshot, chainId, OffsetDAMode);

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public bool GetGatewayEnabled(IReadOnlyStore snapshot, uint chainId) => ReadConfigByte(snapshot, chainId, OffsetGatewayEnabled) == 1;

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public bool GetPermissionlessExit(IReadOnlyStore snapshot, uint chainId) => ReadConfigByte(snapshot, chainId, OffsetPermissionlessExit) == 1;

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public byte GetSequencerModel(IReadOnlyStore snapshot, uint chainId) => ReadConfigByte(snapshot, chainId, OffsetSequencerModel);

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public byte GetExitModel(IReadOnlyStore snapshot, uint chainId) => ReadConfigByte(snapshot, chainId, OffsetExitModel);

        private void WriteChainConfig(ApplicationEngine engine, uint chainId, byte[] configBytes)
        {
            if (chainId == 0) throw new ArgumentOutOfRangeException(nameof(chainId), "chainId 0 is reserved for L1.");
            if (configBytes.Length != ConfigSize) throw new ArgumentException("config size mismatch.", nameof(configBytes));
            if (ReadU32Le(configBytes) != chainId) throw new ArgumentException("chainId mismatch.", nameof(configBytes));

            var key = Key(PrefixConfig, chainId);
            var isNew = !engine.SnapshotCache.Contains(key);
            Put(engine.SnapshotCache, key, configBytes);
            if (isNew) Put(engine.SnapshotCache, Key(PrefixChainIndex, chainId), [1]);
            Notify(engine, "ChainRegistered", chainId, configBytes);
        }

        private byte ReadConfigByte(IReadOnlyStore snapshot, uint chainId, int offset)
        {
            var config = GetChainConfig(snapshot, chainId);
            return config.Length == ConfigSize ? config[offset] : (byte)0;
        }
    }

    public sealed class NeoHubTokenRegistryContract : NeoHubNativeContract
    {
        private const byte PrefixMapping = 0x01;
        private const byte KeyOwner = 0xff;

        public const int MappingSize = 20 + 4 + 20 + 4;

        [ContractEvent(0, name: "MappingRegistered", "l1Asset", ContractParameterType.Hash160, "chainId", ContractParameterType.Integer, "l2Asset", ContractParameterType.Hash160)]
        internal NeoHubTokenRegistryContract() : base(-102) { }

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
        private void RegisterMapping(ApplicationEngine engine, byte[] mappingBytes)
        {
            AssertOwner(engine);
            if (mappingBytes.Length != MappingSize) throw new ArgumentException("mapping size mismatch.", nameof(mappingBytes));

            var l1Asset = ReadUInt160(mappingBytes, 0);
            var chainId = ReadU32Le(mappingBytes.AsSpan(20));
            var l2Asset = ReadUInt160(mappingBytes, 24);
            RequireNonZero(l1Asset, nameof(l1Asset));
            RequireNonZero(l2Asset, nameof(l2Asset));
            if (chainId == 0) throw new ArgumentOutOfRangeException(nameof(chainId), "chainId 0 is reserved for L1.");

            Put(engine.SnapshotCache, Key(PrefixMapping, l1Asset, chainId), mappingBytes);
            Notify(engine, "MappingRegistered", l1Asset, chainId, l2Asset);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public byte[] GetMapping(IReadOnlyStore snapshot, UInt160 l1Asset, uint chainId)
        {
            return snapshot.TryGet(Key(PrefixMapping, l1Asset, chainId), out var item) ? item.Value.ToArray() : EmptyBytes;
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public UInt160 GetL2Asset(IReadOnlyStore snapshot, UInt160 l1Asset, uint chainId)
        {
            var mapping = GetMapping(snapshot, l1Asset, chainId);
            return mapping.Length == MappingSize ? ReadUInt160(mapping, 24) : UInt160.Zero;
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public bool IsActive(IReadOnlyStore snapshot, UInt160 l1Asset, uint chainId)
        {
            var mapping = GetMapping(snapshot, l1Asset, chainId);
            return mapping.Length == MappingSize && mapping[MappingSize - 1] == 1;
        }
    }

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

    public sealed class NeoHubL1TxFilterContract : NeoHubNativeContract
    {
        private const byte PrefixSenderRule = 0x01;
        private const byte PrefixReceiverRule = 0x02;
        private const byte PrefixMessageTypeRule = 0x03;
        private const byte KeyDefaultAllow = 0x04;
        private const byte KeyOwner = 0xff;

        private const byte RuleUnset = 0;
        private const byte RuleAllow = 1;
        private const byte RuleDeny = 2;

        public const int MaxPayloadBytes = 128 * 1024;

        [ContractEvent(0, name: "DefaultPolicySet", "allow", ContractParameterType.Boolean)]
        [ContractEvent(1, name: "RuleSet", "kind", ContractParameterType.Integer, "subject", ContractParameterType.ByteArray, "rule", ContractParameterType.Integer)]
        internal NeoHubL1TxFilterContract() : base(-104) { }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
        private void Configure(ApplicationEngine engine, UInt160 owner)
        {
            RequireNonZero(owner, nameof(owner));
            AssertOwnerOrCommittee(engine);
            WriteUInt160(engine.SnapshotCache, KeyOwner, owner);
            Put(engine.SnapshotCache, KeyDefaultAllow, [RuleAllow]);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public UInt160 GetOwner(IReadOnlyStore snapshot) => ReadUInt160(snapshot, KeyOwner);

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public bool GetDefaultAllow(IReadOnlyStore snapshot)
        {
            return !snapshot.TryGet(CreateStorageKey(KeyDefaultAllow), out var item) || item.Value.Span[0] == RuleAllow;
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
        private void SetDefaultAllow(ApplicationEngine engine, bool allow)
        {
            AssertOwner(engine);
            Put(engine.SnapshotCache, KeyDefaultAllow, [allow ? RuleAllow : RuleDeny]);
            Notify(engine, "DefaultPolicySet", allow);
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
        private void SetSenderRule(ApplicationEngine engine, UInt160 sender, byte rule)
        {
            RequireNonZero(sender, nameof(sender));
            SetRule(engine, Key(PrefixSenderRule, sender), PrefixSenderRule, sender.ToArray(), rule);
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
        private void SetAllowedSender(ApplicationEngine engine, UInt160 sender, bool allowed)
        {
            SetSenderRule(engine, sender, allowed ? RuleAllow : RuleDeny);
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
        private void SetReceiverRule(ApplicationEngine engine, UInt160 receiver, byte rule)
        {
            RequireNonZero(receiver, nameof(receiver));
            SetRule(engine, Key(PrefixReceiverRule, receiver), PrefixReceiverRule, receiver.ToArray(), rule);
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
        private void SetAllowedReceiver(ApplicationEngine engine, UInt160 receiver, bool allowed)
        {
            SetReceiverRule(engine, receiver, allowed ? RuleAllow : RuleDeny);
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
        private void SetMessageTypeRule(ApplicationEngine engine, byte messageType, byte rule)
        {
            SetRule(engine, CreateStorageKey(PrefixMessageTypeRule, messageType), PrefixMessageTypeRule, [messageType], rule);
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
        private void SetAllowedMessageType(ApplicationEngine engine, byte messageType, bool allowed)
        {
            SetMessageTypeRule(engine, messageType, allowed ? RuleAllow : RuleDeny);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public bool AcceptL1ToL2(IReadOnlyStore snapshot, uint targetChainId, UInt160 sender, UInt160 receiver, byte messageType, byte[] payload)
        {
            if (targetChainId == 0) return false;
            if (sender == UInt160.Zero || receiver == UInt160.Zero) return false;
            if (payload.Length > MaxPayloadBytes) return false;

            return AcceptRule(snapshot, Key(PrefixSenderRule, sender))
                && AcceptRule(snapshot, Key(PrefixReceiverRule, receiver))
                && AcceptRule(snapshot, CreateStorageKey(PrefixMessageTypeRule, messageType));
        }

        private void SetRule(ApplicationEngine engine, StorageKey key, byte kind, byte[] subject, byte rule)
        {
            AssertOwner(engine);
            if (rule > RuleDeny) throw new ArgumentOutOfRangeException(nameof(rule), "rule must be 0=unset, 1=allow, 2=deny.");
            if (rule == RuleUnset)
                engine.SnapshotCache.Delete(key);
            else
                Put(engine.SnapshotCache, key, [rule]);
            Notify(engine, "RuleSet", kind, subject, rule);
        }

        private bool AcceptRule(IReadOnlyStore snapshot, StorageKey key)
        {
            if (!snapshot.TryGet(key, out var item)) return GetDefaultAllow(snapshot);
            return item.Value.Span[0] == RuleAllow;
        }
    }

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

    public sealed class NeoHubSharedBridgeContract : NeoHubNativeContract
    {
        private const byte PrefixDepositNonce = 0x01;
        private const byte PrefixDeposit = 0x02;
        private const byte PrefixWithdrawalConsumed = 0x03;
        private const byte PrefixSettlementManager = 0xfd;
        private const byte PrefixTokenRegistry = 0xfe;
        private const byte KeyOwner = 0xff;

        private const int MaxAmountBytes = 64;

        [ContractEvent(0, name: "DepositEnqueued", "targetChainId", ContractParameterType.Integer, "nonce", ContractParameterType.Integer, "sender", ContractParameterType.Hash160, "recipient", ContractParameterType.Hash160, "amount", ContractParameterType.Integer)]
        [ContractEvent(1, name: "WithdrawalFinalized", "chainId", ContractParameterType.Integer, "asset", ContractParameterType.Hash160, "recipient", ContractParameterType.Hash160, "amount", ContractParameterType.Integer)]
        internal NeoHubSharedBridgeContract() : base(-109) { }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
        private void Configure(ApplicationEngine engine, UInt160 owner, UInt160 settlementManager, UInt160 tokenRegistry)
        {
            RequireNonZero(owner, nameof(owner));
            RequireNonZero(settlementManager, nameof(settlementManager));
            RequireNonZero(tokenRegistry, nameof(tokenRegistry));
            AssertOwnerOrCommittee(engine);
            WriteUInt160(engine.SnapshotCache, KeyOwner, owner);
            WriteUInt160(engine.SnapshotCache, PrefixSettlementManager, settlementManager);
            WriteUInt160(engine.SnapshotCache, PrefixTokenRegistry, tokenRegistry);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public UInt160 GetOwner(IReadOnlyStore snapshot) => ReadUInt160(snapshot, KeyOwner);

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public UInt160 GetSettlementManager(IReadOnlyStore snapshot) => ReadUInt160(snapshot, PrefixSettlementManager);

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public UInt160 GetTokenRegistry(IReadOnlyStore snapshot) => ReadUInt160(snapshot, PrefixTokenRegistry);

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowCall | CallFlags.AllowNotify)]
        private async ContractTask<ulong> Deposit(ApplicationEngine engine, UInt160 asset, BigInteger amount, uint targetChainId, UInt160 l2Recipient)
        {
            RequireNonZero(asset, nameof(asset));
            if (amount <= BigInteger.Zero) throw new ArgumentOutOfRangeException(nameof(amount), "amount must be positive.");
            if (targetChainId == 0) throw new ArgumentOutOfRangeException(nameof(targetChainId), "targetChainId 0 is reserved for L1.");
            RequireNonZero(l2Recipient, nameof(l2Recipient));

            var caller = engine.CallingScriptHash ?? throw new InvalidOperationException("calling script hash unavailable");
            var transferred = await engine.CallFromNativeContractAsync<bool>(
                Hash, asset, "transfer", caller.ToArray(), Hash.ToArray(), amount, StackItem.Null);
            if (!transferred) throw new InvalidOperationException("asset transfer failed");

            var nonce = NextDepositNonce(engine.SnapshotCache, targetChainId);
            Put(engine.SnapshotCache, DepositKey(targetChainId, nonce),
                EncodeDeposit(asset, amount, l2Recipient, caller, nonce));
            Notify(engine, "DepositEnqueued", targetChainId, nonce, caller, l2Recipient, amount);
            return nonce;
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public byte[] GetDeposit(IReadOnlyStore snapshot, uint chainId, ulong nonce)
        {
            return snapshot.TryGet(DepositKey(chainId, nonce), out var item) ? item.Value.ToArray() : EmptyBytes;
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public bool IsWithdrawalConsumed(IReadOnlyStore snapshot, uint chainId, UInt256 withdrawalLeafHash)
        {
            return snapshot.TryGet(WithdrawalKey(chainId, withdrawalLeafHash), out _);
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowCall | CallFlags.AllowNotify)]
        private async ContractTask FinalizeWithdrawal(
            ApplicationEngine engine,
            uint chainId,
            UInt256 withdrawalLeafHash,
            UInt160 emittingContract,
            UInt160 l2Sender,
            UInt160 l2Asset,
            ulong withdrawalNonce,
            UInt160 asset,
            UInt160 recipient,
            BigInteger amount)
        {
            await ValidateWithdrawalLeafBinding(engine, chainId, withdrawalLeafHash, emittingContract, l2Sender,
                l2Asset, withdrawalNonce, asset, recipient, amount);

            var consumedKey = AssertWithdrawalNotConsumed(engine, chainId, withdrawalLeafHash);
            var settlementManager = GetSettlementManager(engine.SnapshotCache);
            RequireNonZero(settlementManager, nameof(settlementManager));
            var verified = await engine.CallFromNativeContractAsync<bool>(
                Hash, settlementManager, "verifyWithdrawalLeaf", chainId, withdrawalLeafHash.ToArray());
            if (!verified) throw new InvalidOperationException("withdrawal leaf not in finalized batch");

            await ConsumeAndPayout(engine, consumedKey, chainId, asset, recipient, amount);
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowCall | CallFlags.AllowNotify)]
        private async ContractTask FinalizeWithdrawalAt(
            ApplicationEngine engine,
            uint chainId,
            ulong batchNumber,
            UInt256 withdrawalLeafHash,
            UInt160 emittingContract,
            UInt160 l2Sender,
            UInt160 l2Asset,
            ulong withdrawalNonce,
            UInt160 asset,
            UInt160 recipient,
            BigInteger amount)
        {
            await ValidateWithdrawalLeafBinding(engine, chainId, withdrawalLeafHash, emittingContract, l2Sender,
                l2Asset, withdrawalNonce, asset, recipient, amount);

            var consumedKey = AssertWithdrawalNotConsumed(engine, chainId, withdrawalLeafHash);
            var settlementManager = GetSettlementManager(engine.SnapshotCache);
            RequireNonZero(settlementManager, nameof(settlementManager));
            var verified = await engine.CallFromNativeContractAsync<bool>(
                Hash, settlementManager, "verifyWithdrawalLeafAt", chainId, batchNumber, withdrawalLeafHash.ToArray());
            if (!verified) throw new InvalidOperationException("withdrawal leaf not in named finalized batch");

            await ConsumeAndPayout(engine, consumedKey, chainId, asset, recipient, amount);
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowCall | CallFlags.AllowNotify)]
        private async ContractTask FinalizeWithdrawalWithProof(
            ApplicationEngine engine,
            uint chainId,
            ulong batchNumber,
            UInt256 withdrawalLeafHash,
            byte[][] siblings,
            ulong leafIndex,
            UInt160 emittingContract,
            UInt160 l2Sender,
            UInt160 l2Asset,
            ulong withdrawalNonce,
            UInt160 asset,
            UInt160 recipient,
            BigInteger amount)
        {
            await ValidateWithdrawalLeafBinding(engine, chainId, withdrawalLeafHash, emittingContract, l2Sender,
                l2Asset, withdrawalNonce, asset, recipient, amount);

            var consumedKey = AssertWithdrawalNotConsumed(engine, chainId, withdrawalLeafHash);
            var settlementManager = GetSettlementManager(engine.SnapshotCache);
            RequireNonZero(settlementManager, nameof(settlementManager));
            var verified = await engine.CallFromNativeContractAsync<bool>(
                Hash, settlementManager, "verifyWithdrawalLeafWithProof",
                chainId, batchNumber, withdrawalLeafHash.ToArray(), ToStackArray(siblings), leafIndex);
            if (!verified) throw new InvalidOperationException("withdrawal leaf not in batch's Merkle root");

            await ConsumeAndPayout(engine, consumedKey, chainId, asset, recipient, amount);
        }

        private async ContractTask ValidateWithdrawalLeafBinding(
            ApplicationEngine engine,
            uint chainId,
            UInt256 withdrawalLeafHash,
            UInt160 emittingContract,
            UInt160 l2Sender,
            UInt160 l2Asset,
            ulong withdrawalNonce,
            UInt160 asset,
            UInt160 recipient,
            BigInteger amount)
        {
            ValidateWithdrawalArgs(chainId, asset, recipient, amount);
            RequireNonZero(emittingContract, nameof(emittingContract));
            RequireNonZero(l2Sender, nameof(l2Sender));
            RequireNonZero(l2Asset, nameof(l2Asset));

            var expected = ComputeWithdrawalLeafHash(emittingContract, l2Sender, recipient, l2Asset, amount, withdrawalNonce);
            if (!expected.Equals(withdrawalLeafHash)) throw new InvalidOperationException("withdrawal leaf preimage mismatch");

            var tokenRegistry = GetTokenRegistry(engine.SnapshotCache);
            RequireNonZero(tokenRegistry, nameof(tokenRegistry));
            var mappedL2Asset = await engine.CallFromNativeContractAsync<UInt160>(
                Hash, tokenRegistry, "getL2Asset", asset.ToArray(), chainId);
            if (mappedL2Asset is null || !mappedL2Asset.Equals(l2Asset))
                throw new InvalidOperationException("L1 asset does not map to withdrawal L2 asset");

            var active = await engine.CallFromNativeContractAsync<bool>(
                Hash, tokenRegistry, "isActive", asset.ToArray(), chainId);
            if (!active) throw new InvalidOperationException("asset mapping inactive");
        }

        private static void ValidateWithdrawalArgs(uint chainId, UInt160 asset, UInt160 recipient, BigInteger amount)
        {
            if (chainId == 0) throw new ArgumentOutOfRangeException(nameof(chainId), "chainId 0 is reserved for L1.");
            if (amount <= BigInteger.Zero) throw new ArgumentOutOfRangeException(nameof(amount), "amount must be positive.");
            RequireNonZero(asset, nameof(asset));
            RequireNonZero(recipient, nameof(recipient));
        }

        private StorageKey AssertWithdrawalNotConsumed(ApplicationEngine engine, uint chainId, UInt256 withdrawalLeafHash)
        {
            var consumedKey = WithdrawalKey(chainId, withdrawalLeafHash);
            if (engine.SnapshotCache.Contains(consumedKey)) throw new InvalidOperationException("withdrawal already consumed");
            return consumedKey;
        }

        private async ContractTask ConsumeAndPayout(
            ApplicationEngine engine,
            StorageKey consumedKey,
            uint chainId,
            UInt160 asset,
            UInt160 recipient,
            BigInteger amount)
        {
            Put(engine.SnapshotCache, consumedKey, [1]);
            var transferred = await engine.CallFromNativeContractAsync<bool>(
                Hash, asset, "transfer", Hash.ToArray(), recipient.ToArray(), amount, StackItem.Null);
            if (!transferred) throw new InvalidOperationException("asset transfer failed");
            Notify(engine, "WithdrawalFinalized", chainId, asset, recipient, amount);
        }

        private ulong NextDepositNonce(DataCache snapshot, uint chainId)
        {
            var item = snapshot.GetAndChange(NonceKey(chainId), () => new StorageItem(BigInteger.Zero));
            var next = (ulong)(BigInteger)item + 1UL;
            item.Set(new BigInteger(next));
            return next;
        }

        private StorageKey NonceKey(uint chainId) => Key(PrefixDepositNonce, chainId);

        private StorageKey DepositKey(uint chainId, ulong nonce) => Key(PrefixDeposit, chainId, nonce);

        private StorageKey WithdrawalKey(uint chainId, UInt256 withdrawalLeafHash)
        {
            var data = new byte[4 + UInt256.Length];
            U32Le(chainId).CopyTo(data, 0);
            withdrawalLeafHash.ToArray().CopyTo(data, 4);
            return CreateStorageKey(PrefixWithdrawalConsumed, data);
        }

        private static byte[] EncodeDeposit(UInt160 asset, BigInteger amount, UInt160 recipient, UInt160 sender, ulong nonce)
        {
            var amountBytes = amount.ToByteArray();
            var bytes = new byte[UInt160.Length + UInt160.Length + UInt160.Length + 8 + 4 + amountBytes.Length];
            var offset = 0;
            asset.ToArray().CopyTo(bytes, offset);
            offset += UInt160.Length;
            recipient.ToArray().CopyTo(bytes, offset);
            offset += UInt160.Length;
            sender.ToArray().CopyTo(bytes, offset);
            offset += UInt160.Length;
            U64Le(nonce).CopyTo(bytes, offset);
            offset += 8;
            U32Le((uint)amountBytes.Length).CopyTo(bytes, offset);
            offset += 4;
            amountBytes.CopyTo(bytes, offset);
            return bytes;
        }

        private static UInt256 ComputeWithdrawalLeafHash(
            UInt160 emittingContract,
            UInt160 l2Sender,
            UInt160 l1Recipient,
            UInt160 l2Asset,
            BigInteger amount,
            ulong nonce)
        {
            var amountBytes = ToUnsignedLittleEndian(amount);
            if (amountBytes.Length > MaxAmountBytes) throw new InvalidOperationException("amount too large");

            var bytes = new byte[UInt160.Length + UInt160.Length + UInt160.Length + UInt160.Length + 4 + amountBytes.Length + 8];
            var offset = 0;
            emittingContract.ToArray().CopyTo(bytes, offset);
            offset += UInt160.Length;
            l2Sender.ToArray().CopyTo(bytes, offset);
            offset += UInt160.Length;
            l1Recipient.ToArray().CopyTo(bytes, offset);
            offset += UInt160.Length;
            l2Asset.ToArray().CopyTo(bytes, offset);
            offset += UInt160.Length;
            U32Le((uint)amountBytes.Length).CopyTo(bytes, offset);
            offset += 4;
            amountBytes.CopyTo(bytes, offset);
            offset += amountBytes.Length;
            U64Le(nonce).CopyTo(bytes, offset);
            return new UInt256(Crypto.Hash256(bytes));
        }

        private static byte[] ToUnsignedLittleEndian(BigInteger value)
        {
            var raw = value.ToByteArray();
            var length = raw.Length;
            while (length > 1 && raw[length - 1] == 0) length--;
            var trimmed = new byte[length];
            System.Buffer.BlockCopy(raw, 0, trimmed, 0, length);
            return trimmed;
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

    public sealed class NeoHubGovernanceControllerContract : NeoHubNativeContract
    {
        private const byte PrefixCouncilMember = 0x01;
        private const byte KeyCouncilCount = 0x02;
        private const byte KeyCouncilThreshold = 0x03;
        private const byte KeyTimelockSeconds = 0x04;
        private const byte KeyAdmissionMode = 0x05;
        private const byte PrefixProposal = 0x06;
        private const byte PrefixApproval = 0x07;
        private const byte KeyNextProposalId = 0x08;
        private const byte PrefixApprovalCount = 0x09;
        private const byte PrefixApprovedVerifier = 0x0a;
        private const byte PrefixApprovedBridge = 0x0b;
        private const byte PrefixApprovedAt = 0x0c;
        private const byte PrefixImmutableFlag = 0x0d;
        private const byte PrefixConsumedSetImmutable = 0x0e;
        private const byte KeyUpgradeNoticeSeconds = 0x0f;
        private const byte KeyUpgradeExecutionWindowSeconds = 0x10;
        private const byte KeyUpgradeCooldownSeconds = 0x11;
        private const byte PrefixProposalExecutedAt = 0x12;
        private const byte KeyOwner = 0xff;

        public const byte StagePending = 0;
        public const byte StageNotice = 1;
        public const byte StageExecutable = 2;
        public const byte StageCooldown = 3;
        public const byte StageComplete = 4;
        public const byte StageExpired = 5;

        [ContractEvent(0, name: "ProposalCreated", "proposalId", ContractParameterType.Integer, "payload", ContractParameterType.ByteArray)]
        [ContractEvent(1, name: "ProposalApproved", "proposalId", ContractParameterType.Integer, "member", ContractParameterType.PublicKey)]
        [ContractEvent(2, name: "ImmutableFlagSet", "flagId", ContractParameterType.Integer)]
        [ContractEvent(3, name: "UpgradeWindowsSet", "noticeSeconds", ContractParameterType.Integer, "executionWindowSeconds", ContractParameterType.Integer, "cooldownSeconds", ContractParameterType.Integer)]
        [ContractEvent(4, name: "ProposalExecuted", "proposalId", ContractParameterType.Integer, "executedAt", ContractParameterType.Integer)]
        internal NeoHubGovernanceControllerContract() : base(-111) { }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
        private void Configure(ApplicationEngine engine, UInt160 owner, ECPoint[] members, uint threshold, uint timelockSeconds)
        {
            RequireNonZero(owner, nameof(owner));
            ArgumentNullException.ThrowIfNull(members);
            if (members.Length == 0) throw new ArgumentException("council must be non-empty.", nameof(members));
            if (threshold == 0 || threshold > members.Length) throw new ArgumentOutOfRangeException(nameof(threshold), "bad threshold");
            if (timelockSeconds == 0) throw new ArgumentOutOfRangeException(nameof(timelockSeconds), "timelock must be positive");
            AssertOwnerOrCommittee(engine);

            WriteUInt160(engine.SnapshotCache, KeyOwner, owner);
            PutInteger(engine.SnapshotCache, CreateStorageKey(KeyCouncilCount), (ulong)members.Length);
            PutInteger(engine.SnapshotCache, CreateStorageKey(KeyCouncilThreshold), threshold);
            PutInteger(engine.SnapshotCache, CreateStorageKey(KeyTimelockSeconds), timelockSeconds);
            PutInteger(engine.SnapshotCache, CreateStorageKey(KeyUpgradeNoticeSeconds), timelockSeconds);
            PutInteger(engine.SnapshotCache, CreateStorageKey(KeyUpgradeExecutionWindowSeconds), timelockSeconds);
            PutInteger(engine.SnapshotCache, CreateStorageKey(KeyUpgradeCooldownSeconds), timelockSeconds);
            Put(engine.SnapshotCache, KeyAdmissionMode, [0]);
            PutInteger(engine.SnapshotCache, CreateStorageKey(KeyNextProposalId), 1);

            for (var i = 0; i < members.Length; i++)
                Put(engine.SnapshotCache, CouncilMemberKey(members[i]), [1]);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public UInt160 GetOwner(IReadOnlyStore snapshot) => ReadUInt160(snapshot, KeyOwner);

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public bool IsCouncilMember(IReadOnlyStore snapshot, ECPoint memberKey)
        {
            return memberKey is not null && snapshot.TryGet(CouncilMemberKey(memberKey), out _);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public uint GetCouncilCount(IReadOnlyStore snapshot) => ReadUInt32(snapshot, CreateStorageKey(KeyCouncilCount));

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public uint GetThreshold(IReadOnlyStore snapshot) => ReadUInt32(snapshot, CreateStorageKey(KeyCouncilThreshold));

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public uint GetTimelockSeconds(IReadOnlyStore snapshot) => ReadUInt32(snapshot, CreateStorageKey(KeyTimelockSeconds));

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public byte GetAdmissionMode(IReadOnlyStore snapshot)
        {
            return snapshot.TryGet(CreateStorageKey(KeyAdmissionMode), out var item) ? item.Value.Span[0] : (byte)0;
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
        private void SetAdmissionMode(ApplicationEngine engine, byte mode)
        {
            if (mode > 2) throw new ArgumentOutOfRangeException(nameof(mode), "invalid admission mode");
            AssertOwner(engine);
            Put(engine.SnapshotCache, KeyAdmissionMode, [mode]);
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
        private ulong CreateProposal(ApplicationEngine engine, ECPoint signer, byte[] payload)
        {
            RequireCouncilMember(engine.SnapshotCache, signer);
            AssertMemberWitness(engine, signer);
            if (payload.Length == 0) throw new ArgumentException("empty proposal payload.", nameof(payload));

            var key = CreateStorageKey(KeyNextProposalId);
            var id = ReadUInt64(engine.SnapshotCache, key);
            if (id == 0) id = 1;
            PutInteger(engine.SnapshotCache, key, id + 1UL);
            Put(engine.SnapshotCache, ProposalKey(id), payload);
            Notify(engine, "ProposalCreated", id, payload);
            return id;
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
        private uint Approve(ApplicationEngine engine, ulong proposalId, ECPoint memberKey)
        {
            RequireCouncilMember(engine.SnapshotCache, memberKey);
            AssertMemberWitness(engine, memberKey);
            if (!engine.SnapshotCache.Contains(ProposalKey(proposalId))) throw new InvalidOperationException("unknown proposal");

            var approvalKey = ApprovalKey(proposalId, memberKey);
            if (engine.SnapshotCache.Contains(approvalKey)) throw new InvalidOperationException("already approved");
            Put(engine.SnapshotCache, approvalKey, [1]);
            Notify(engine, "ProposalApproved", proposalId, memberKey);
            return IncrementAndCountApprovals(engine, proposalId);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public byte[] GetProposal(IReadOnlyStore snapshot, ulong proposalId)
        {
            return snapshot.TryGet(ProposalKey(proposalId), out var item) ? item.Value.ToArray() : EmptyBytes;
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public ulong GetApprovedAt(IReadOnlyStore snapshot, ulong proposalId) => ReadUInt64(snapshot, ProposalIdKey(PrefixApprovedAt, proposalId));

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public uint GetApprovalCount(IReadOnlyStore snapshot, ulong proposalId) => ReadUInt32(snapshot, ProposalIdKey(PrefixApprovalCount, proposalId));

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        private bool IsApprovedAndTimelocked(ApplicationEngine engine, ulong proposalId)
        {
            var approvedAt = GetApprovedAt(engine.SnapshotCache, proposalId);
            if (approvedAt == 0) return false;
            return RuntimeTime(engine) >= approvedAt + GetTimelockSeconds(engine.SnapshotCache) * 1000UL;
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
        private void SetUpgradeWindows(ApplicationEngine engine, uint noticeSeconds, uint executionWindowSeconds, uint cooldownSeconds)
        {
            AssertOwner(engine);
            if (noticeSeconds == 0) throw new ArgumentOutOfRangeException(nameof(noticeSeconds), "notice must be positive");
            if (executionWindowSeconds == 0) throw new ArgumentOutOfRangeException(nameof(executionWindowSeconds), "execution window must be positive");
            if (cooldownSeconds == 0) throw new ArgumentOutOfRangeException(nameof(cooldownSeconds), "cooldown must be positive");
            PutInteger(engine.SnapshotCache, CreateStorageKey(KeyUpgradeNoticeSeconds), noticeSeconds);
            PutInteger(engine.SnapshotCache, CreateStorageKey(KeyUpgradeExecutionWindowSeconds), executionWindowSeconds);
            PutInteger(engine.SnapshotCache, CreateStorageKey(KeyUpgradeCooldownSeconds), cooldownSeconds);
            Notify(engine, "UpgradeWindowsSet", noticeSeconds, executionWindowSeconds, cooldownSeconds);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public uint GetUpgradeNoticeSeconds(IReadOnlyStore snapshot)
        {
            var value = ReadUInt32(snapshot, CreateStorageKey(KeyUpgradeNoticeSeconds));
            return value == 0 ? GetTimelockSeconds(snapshot) : value;
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public uint GetUpgradeExecutionWindowSeconds(IReadOnlyStore snapshot)
        {
            var value = ReadUInt32(snapshot, CreateStorageKey(KeyUpgradeExecutionWindowSeconds));
            return value == 0 ? GetTimelockSeconds(snapshot) : value;
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public uint GetUpgradeCooldownSeconds(IReadOnlyStore snapshot)
        {
            var value = ReadUInt32(snapshot, CreateStorageKey(KeyUpgradeCooldownSeconds));
            return value == 0 ? GetTimelockSeconds(snapshot) : value;
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public ulong GetProposalExecutedAt(IReadOnlyStore snapshot, ulong proposalId) => ReadUInt64(snapshot, ProposalIdKey(PrefixProposalExecutedAt, proposalId));

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        private byte GetProposalStage(ApplicationEngine engine, ulong proposalId)
        {
            var approvedAt = GetApprovedAt(engine.SnapshotCache, proposalId);
            if (approvedAt == 0) return StagePending;
            var now = RuntimeTime(engine);
            var noticeEnd = approvedAt + GetUpgradeNoticeSeconds(engine.SnapshotCache) * 1000UL;
            if (now < noticeEnd) return StageNotice;

            var executedAt = GetProposalExecutedAt(engine.SnapshotCache, proposalId);
            if (executedAt > 0)
            {
                var cooldownEnd = executedAt + GetUpgradeCooldownSeconds(engine.SnapshotCache) * 1000UL;
                return now < cooldownEnd ? StageCooldown : StageComplete;
            }

            var executionEnd = noticeEnd + GetUpgradeExecutionWindowSeconds(engine.SnapshotCache) * 1000UL;
            return now <= executionEnd ? StageExecutable : StageExpired;
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        private bool IsInExecutionWindow(ApplicationEngine engine, ulong proposalId) => GetProposalStage(engine, proposalId) == StageExecutable;

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
        private void MarkProposalExecuted(ApplicationEngine engine, ulong proposalId)
        {
            AssertOwner(engine);
            if (!IsInExecutionWindow(engine, proposalId)) throw new InvalidOperationException("proposal not executable");
            var key = ProposalIdKey(PrefixProposalExecutedAt, proposalId);
            if (engine.SnapshotCache.Contains(key)) throw new InvalidOperationException("proposal already executed");
            var now = RuntimeTime(engine);
            PutInteger(engine.SnapshotCache, key, now);
            Notify(engine, "ProposalExecuted", proposalId, now);
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
        private void ApproveVerifier(ApplicationEngine engine, UInt160 verifier)
        {
            RequireNonZero(verifier, nameof(verifier));
            AssertOwner(engine);
            Put(engine.SnapshotCache, Key(PrefixApprovedVerifier, verifier), [1]);
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
        private void RevokeVerifier(ApplicationEngine engine, UInt160 verifier)
        {
            RequireNonZero(verifier, nameof(verifier));
            AssertOwner(engine);
            engine.SnapshotCache.Delete(Key(PrefixApprovedVerifier, verifier));
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public bool IsApprovedVerifier(IReadOnlyStore snapshot, UInt160 verifier)
        {
            return verifier != UInt160.Zero && snapshot.TryGet(Key(PrefixApprovedVerifier, verifier), out _);
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
        private void ApproveBridgeAdapter(ApplicationEngine engine, UInt160 bridge)
        {
            RequireNonZero(bridge, nameof(bridge));
            AssertOwner(engine);
            Put(engine.SnapshotCache, Key(PrefixApprovedBridge, bridge), [1]);
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
        private void RevokeBridgeAdapter(ApplicationEngine engine, UInt160 bridge)
        {
            RequireNonZero(bridge, nameof(bridge));
            AssertOwner(engine);
            engine.SnapshotCache.Delete(Key(PrefixApprovedBridge, bridge));
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public bool IsApprovedBridgeAdapter(IReadOnlyStore snapshot, UInt160 bridge)
        {
            return bridge != UInt160.Zero && snapshot.TryGet(Key(PrefixApprovedBridge, bridge), out _);
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
        private void SetImmutableFlag(ApplicationEngine engine, byte flagId)
        {
            AssertOwner(engine);
            SetImmutableFlagCore(engine, flagId);
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
        private void SetImmutableFlagViaProposal(ApplicationEngine engine, byte flagId, ulong proposalId)
        {
            var consumedKey = ProposalIdKey(PrefixConsumedSetImmutable, proposalId);
            if (engine.SnapshotCache.Contains(consumedKey)) throw new InvalidOperationException("proposal already consumed");
            if (!IsApprovedAndTimelocked(engine, proposalId)) throw new InvalidOperationException("proposal not approved + timelocked");
            Put(engine.SnapshotCache, consumedKey, [1]);
            SetImmutableFlagCore(engine, flagId);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public bool IsImmutable(IReadOnlyStore snapshot, byte flagId)
        {
            return snapshot.TryGet(ImmutableFlagKey(flagId), out _);
        }

        private uint IncrementAndCountApprovals(ApplicationEngine engine, ulong proposalId)
        {
            var counterKey = ProposalIdKey(PrefixApprovalCount, proposalId);
            var current = GetApprovalCount(engine.SnapshotCache, proposalId);
            var next = current + 1;
            PutInteger(engine.SnapshotCache, counterKey, next);

            var threshold = GetThreshold(engine.SnapshotCache);
            if (threshold > 0 && current < threshold && next >= threshold && !engine.SnapshotCache.Contains(ProposalIdKey(PrefixApprovedAt, proposalId)))
                PutInteger(engine.SnapshotCache, ProposalIdKey(PrefixApprovedAt, proposalId), RuntimeTime(engine));
            return next;
        }

        private void SetImmutableFlagCore(ApplicationEngine engine, byte flagId)
        {
            var key = ImmutableFlagKey(flagId);
            if (engine.SnapshotCache.Contains(key)) return;
            Put(engine.SnapshotCache, key, [1]);
            Notify(engine, "ImmutableFlagSet", flagId);
        }

        private void RequireCouncilMember(IReadOnlyStore snapshot, ECPoint memberKey)
        {
            if (!IsCouncilMember(snapshot, memberKey)) throw new InvalidOperationException("not a council member");
        }

        private static void AssertMemberWitness(ApplicationEngine engine, ECPoint memberKey)
        {
            var account = Contract.CreateSignatureRedeemScript(memberKey).ToScriptHash();
            if (!engine.CheckWitnessInternal(account)) throw new InvalidOperationException("no witness");
        }

        private StorageKey CouncilMemberKey(ECPoint memberKey) => CreateStorageKey(PrefixCouncilMember, memberKey);

        private StorageKey ProposalKey(ulong proposalId) => ProposalIdKey(PrefixProposal, proposalId);

        private StorageKey ProposalIdKey(byte prefix, ulong proposalId) => CreateStorageKey(prefix, U64Le(proposalId));

        private StorageKey ApprovalKey(ulong proposalId, ECPoint memberKey)
        {
            var data = new byte[8 + 33];
            U64Le(proposalId).CopyTo(data, 0);
            memberKey.ToArray().CopyTo(data, 8);
            return CreateStorageKey(PrefixApproval, data);
        }

        private StorageKey ImmutableFlagKey(byte flagId) => CreateStorageKey(PrefixImmutableFlag, flagId);

        private static uint ReadUInt32(IReadOnlyStore snapshot, StorageKey key) => (uint)ReadUInt64(snapshot, key);

        private static ulong ReadUInt64(IReadOnlyStore snapshot, StorageKey key)
        {
            return snapshot.TryGet(key, out var item) ? (ulong)(BigInteger)item : 0UL;
        }

        private static void PutInteger(DataCache snapshot, StorageKey key, ulong value)
        {
            snapshot.GetAndChange(key, () => new StorageItem(BigInteger.Zero)).Set(new BigInteger(value));
        }

        private static ulong RuntimeTime(ApplicationEngine engine) => engine.PersistingBlock?.Timestamp ?? 0UL;
    }

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
