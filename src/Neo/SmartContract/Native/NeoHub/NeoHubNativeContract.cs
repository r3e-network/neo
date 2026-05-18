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

}
