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

    public sealed class NeoHubSequencerRegistryContract : NeoHubNativeContract
    {
        private const byte PrefixSequencer = 0x01;
        private const byte PrefixCount = 0x02;
        private const byte KeyMaxCommitteeSize = 0x03;
        private const byte KeyExitWindowSeconds = 0x04;
        private const byte KeyBondContract = 0xfd;
        private const byte KeyOwner = 0xff;

        public const byte DefaultMaxCommitteeSize = 21;
        public const uint DefaultExitWindowSeconds = 86400;
        public const byte StatusActive = 1;
        public const byte StatusExiting = 2;

        [ContractEvent(0, name: "SequencerRegistered", "chainId", ContractParameterType.Integer, "sequencerKey", ContractParameterType.PublicKey)]
        [ContractEvent(1, name: "SequencerExiting", "chainId", ContractParameterType.Integer, "sequencerKey", ContractParameterType.PublicKey, "exitsAt", ContractParameterType.Integer)]
        [ContractEvent(2, name: "SequencerRemoved", "chainId", ContractParameterType.Integer, "sequencerKey", ContractParameterType.PublicKey)]
        internal NeoHubSequencerRegistryContract() : base(-113) { }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
        private void Configure(ApplicationEngine engine, UInt160 owner, UInt160 bondContract)
        {
            RequireNonZero(owner, nameof(owner));
            RequireNonZero(bondContract, nameof(bondContract));
            AssertOwnerOrCommittee(engine);

            WriteUInt160(engine.SnapshotCache, KeyOwner, owner);
            WriteUInt160(engine.SnapshotCache, KeyBondContract, bondContract);
            Put(engine.SnapshotCache, KeyMaxCommitteeSize, [DefaultMaxCommitteeSize]);
            PutInteger(engine.SnapshotCache, CreateStorageKey(KeyExitWindowSeconds), DefaultExitWindowSeconds);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public UInt160 GetOwner(IReadOnlyStore snapshot) => ReadUInt160(snapshot, KeyOwner);

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public UInt160 GetBondContract(IReadOnlyStore snapshot) => ReadUInt160(snapshot, KeyBondContract);

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public byte GetMaxCommitteeSize(IReadOnlyStore snapshot)
        {
            return snapshot.TryGet(CreateStorageKey(KeyMaxCommitteeSize), out var item) ? item.Value.Span[0] : DefaultMaxCommitteeSize;
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public uint GetExitWindowSeconds(IReadOnlyStore snapshot)
        {
            var seconds = ReadUInt32(snapshot, CreateStorageKey(KeyExitWindowSeconds));
            return seconds == 0 ? DefaultExitWindowSeconds : seconds;
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
        private void SetMaxCommitteeSize(ApplicationEngine engine, byte size)
        {
            if (size < 1 || size > 64) throw new ArgumentOutOfRangeException(nameof(size), "size must be in [1, 64].");
            AssertOwner(engine);
            Put(engine.SnapshotCache, KeyMaxCommitteeSize, [size]);
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
        private void SetExitWindowSeconds(ApplicationEngine engine, uint seconds)
        {
            if (seconds < 60 || seconds > 7 * 86400) throw new ArgumentOutOfRangeException(nameof(seconds), "exit window out of bounds [60s, 7d].");
            AssertOwner(engine);
            PutInteger(engine.SnapshotCache, CreateStorageKey(KeyExitWindowSeconds), seconds);
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowCall | CallFlags.AllowNotify)]
        private async ContractTask Register(ApplicationEngine engine, uint chainId, ECPoint sequencerKey, UInt160 sequencerAddress)
        {
            AssertRegistrationArgs(chainId, sequencerKey, sequencerAddress);
            AssertSequencerWitness(engine, sequencerKey);

            var bondContract = GetBondContract(engine.SnapshotCache);
            RequireNonZero(bondContract, nameof(bondContract));
            var hasBond = await engine.CallFromNativeContractAsync<bool>(
                Hash, bondContract, "hasMinBond", chainId, sequencerAddress.ToArray());
            if (!hasBond) throw new InvalidOperationException("insufficient bond");

            var key = SequencerKey(chainId, sequencerKey);
            if (engine.SnapshotCache.Contains(key)) throw new InvalidOperationException("already registered");

            var current = GetActiveCount(engine.SnapshotCache, chainId);
            if (current >= GetMaxCommitteeSize(engine.SnapshotCache)) throw new InvalidOperationException("committee full");

            Put(engine.SnapshotCache, key, EncodeEntry(StatusActive, sequencerAddress, 0));
            PutInteger(engine.SnapshotCache, CountKey(chainId), current + 1);
            Notify(engine, "SequencerRegistered", chainId, sequencerKey);
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
        private uint Unregister(ApplicationEngine engine, uint chainId, ECPoint sequencerKey)
        {
            if (chainId == 0) throw new ArgumentOutOfRangeException(nameof(chainId), "chainId 0 is reserved for L1.");
            ArgumentNullException.ThrowIfNull(sequencerKey);
            AssertSequencerWitness(engine, sequencerKey);

            var key = SequencerKey(chainId, sequencerKey);
            var entry = ReadEntry(engine.SnapshotCache, key);
            if (entry.Length == 0) throw new InvalidOperationException("not registered");
            if (entry[0] != StatusActive) throw new InvalidOperationException("not currently active");

            var exitsAt = checked(RuntimeTimeSeconds(engine) + GetExitWindowSeconds(engine.SnapshotCache));
            entry[0] = StatusExiting;
            U32Le(exitsAt).CopyTo(entry, 1 + UInt160.Length);
            Put(engine.SnapshotCache, key, entry);
            Notify(engine, "SequencerExiting", chainId, sequencerKey, exitsAt);
            return exitsAt;
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
        private void Finalize(ApplicationEngine engine, uint chainId, ECPoint sequencerKey)
        {
            if (chainId == 0) throw new ArgumentOutOfRangeException(nameof(chainId), "chainId 0 is reserved for L1.");
            ArgumentNullException.ThrowIfNull(sequencerKey);

            var key = SequencerKey(chainId, sequencerKey);
            var entry = ReadEntry(engine.SnapshotCache, key);
            if (entry.Length == 0) throw new InvalidOperationException("not registered");
            if (entry[0] != StatusExiting) throw new InvalidOperationException("not exiting");

            var exitsAt = ReadU32Le(entry.AsSpan(1 + UInt160.Length, 4));
            if (RuntimeTimeSeconds(engine) < exitsAt) throw new InvalidOperationException("exit window still open");

            engine.SnapshotCache.Delete(key);
            var current = GetActiveCount(engine.SnapshotCache, chainId);
            PutInteger(engine.SnapshotCache, CountKey(chainId), current == 0 ? 0 : current - 1);
            Notify(engine, "SequencerRemoved", chainId, sequencerKey);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public uint GetActiveCount(IReadOnlyStore snapshot, uint chainId)
        {
            return ReadUInt32(snapshot, CountKey(chainId));
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public bool IsRegistered(IReadOnlyStore snapshot, uint chainId, ECPoint sequencerKey)
        {
            return sequencerKey is not null && snapshot.TryGet(SequencerKey(chainId, sequencerKey), out _);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public byte GetStatus(IReadOnlyStore snapshot, uint chainId, ECPoint sequencerKey)
        {
            var entry = sequencerKey is null ? EmptyBytes : ReadEntry(snapshot, SequencerKey(chainId, sequencerKey));
            return entry.Length == 0 ? (byte)0 : entry[0];
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public UInt160 GetSequencerAddress(IReadOnlyStore snapshot, uint chainId, ECPoint sequencerKey)
        {
            var entry = sequencerKey is null ? EmptyBytes : ReadEntry(snapshot, SequencerKey(chainId, sequencerKey));
            return entry.Length == 0 ? UInt160.Zero : new UInt160(entry.AsSpan(1, UInt160.Length));
        }

        private StorageKey SequencerKey(uint chainId, ECPoint sequencerKey)
        {
            var data = new byte[4 + 33];
            U32Le(chainId).CopyTo(data, 0);
            sequencerKey.ToArray().CopyTo(data, 4);
            return CreateStorageKey(PrefixSequencer, data);
        }

        private StorageKey CountKey(uint chainId) => Key(PrefixCount, chainId);

        private static void AssertRegistrationArgs(uint chainId, ECPoint sequencerKey, UInt160 sequencerAddress)
        {
            if (chainId == 0) throw new ArgumentOutOfRangeException(nameof(chainId), "chainId 0 is reserved for L1.");
            ArgumentNullException.ThrowIfNull(sequencerKey);
            RequireNonZero(sequencerAddress, nameof(sequencerAddress));
        }

        private static void AssertSequencerWitness(ApplicationEngine engine, ECPoint sequencerKey)
        {
            var account = Contract.CreateSignatureRedeemScript(sequencerKey).ToScriptHash();
            if (!engine.CheckWitnessInternal(account)) throw new InvalidOperationException("no witness for sequencer key");
        }

        private static byte[] EncodeEntry(byte status, UInt160 sequencerAddress, uint exitsAt)
        {
            var entry = new byte[1 + UInt160.Length + 4];
            entry[0] = status;
            sequencerAddress.ToArray().CopyTo(entry, 1);
            U32Le(exitsAt).CopyTo(entry, 1 + UInt160.Length);
            return entry;
        }

        private static byte[] ReadEntry(IReadOnlyStore snapshot, StorageKey key)
        {
            if (!snapshot.TryGet(key, out var item)) return EmptyBytes;
            var entry = item.Value.ToArray();
            if (entry.Length != 1 + UInt160.Length + 4) throw new InvalidOperationException("corrupt sequencer entry");
            return entry;
        }

        private static uint ReadUInt32(IReadOnlyStore snapshot, StorageKey key)
        {
            return snapshot.TryGet(key, out var item) ? (uint)(BigInteger)item : 0U;
        }

        private static void PutInteger(DataCache snapshot, StorageKey key, uint value)
        {
            snapshot.GetAndChange(key, () => new StorageItem(BigInteger.Zero)).Set(new BigInteger(value));
        }

        private static uint RuntimeTimeSeconds(ApplicationEngine engine)
        {
            return checked((uint)((engine.PersistingBlock?.Timestamp ?? 0UL) / 1000UL));
        }
    }

}
