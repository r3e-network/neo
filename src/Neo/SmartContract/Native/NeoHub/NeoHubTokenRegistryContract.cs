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

}
