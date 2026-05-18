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

}
