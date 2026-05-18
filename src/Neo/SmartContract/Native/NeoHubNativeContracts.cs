// Copyright (C) 2015-2026 The Neo Project.
//
// NeoHub native contracts are maintained by r3e-network in the r3e/neo-n3-core
// branch. They embed the Neo Elastic Network L1 anchor surface into the r3e Neo
// core fork so production L1 networks do not deploy these system contracts after
// genesis.

#pragma warning disable IDE0051

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
