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

    public sealed class NeoHubSequencerBondContract : NeoHubNativeContract
    {
        private const byte PrefixBalance = 0x01;
        private const byte PrefixSlasher = 0x02;
        private const byte KeyBondAsset = 0x03;
        private const byte KeyMinBond = 0x04;
        private const byte KeyOwner = 0xff;

        public const ulong DefaultMinBond = 1_000_000UL;

        [ContractEvent(0, name: "BondDeposited", "chainId", ContractParameterType.Integer, "sequencer", ContractParameterType.Hash160, "amount", ContractParameterType.Integer)]
        [ContractEvent(1, name: "BondSlashed", "chainId", ContractParameterType.Integer, "sequencer", ContractParameterType.Hash160, "amount", ContractParameterType.Integer, "recipient", ContractParameterType.Hash160)]
        [ContractEvent(2, name: "BondWithdrawn", "chainId", ContractParameterType.Integer, "sequencer", ContractParameterType.Hash160, "amount", ContractParameterType.Integer)]
        internal NeoHubSequencerBondContract() : base(-112) { }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
        private void Configure(ApplicationEngine engine, UInt160 owner, UInt160 bondAsset, UInt160[] slashers)
        {
            RequireNonZero(owner, nameof(owner));
            RequireNonZero(bondAsset, nameof(bondAsset));
            ArgumentNullException.ThrowIfNull(slashers);
            if (slashers.Length == 0) throw new ArgumentException("slashers list must be non-empty.", nameof(slashers));
            foreach (var slasher in slashers)
                RequireNonZero(slasher, nameof(slashers));

            AssertOwnerOrCommittee(engine);
            WriteUInt160(engine.SnapshotCache, KeyOwner, owner);
            WriteUInt160(engine.SnapshotCache, KeyBondAsset, bondAsset);
            PutInteger(engine.SnapshotCache, CreateStorageKey(KeyMinBond), new BigInteger(DefaultMinBond));

            foreach (var slasher in slashers)
                WriteSlasher(engine.SnapshotCache, slasher);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public UInt160 GetOwner(IReadOnlyStore snapshot) => ReadUInt160(snapshot, KeyOwner);

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public UInt160 GetBondAsset(IReadOnlyStore snapshot) => ReadUInt160(snapshot, KeyBondAsset);

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public BigInteger GetMinBond(IReadOnlyStore snapshot) => ReadInteger(snapshot, CreateStorageKey(KeyMinBond));

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
        private void SetMinBond(ApplicationEngine engine, BigInteger amount)
        {
            RequirePositive(amount, nameof(amount));
            AssertOwner(engine);
            PutInteger(engine.SnapshotCache, CreateStorageKey(KeyMinBond), amount);
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
        private void RegisterSlasher(ApplicationEngine engine, UInt160 slasher)
        {
            RequireNonZero(slasher, nameof(slasher));
            AssertOwner(engine);
            WriteSlasher(engine.SnapshotCache, slasher);
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
        private void RevokeSlasher(ApplicationEngine engine, UInt160 slasher)
        {
            RequireNonZero(slasher, nameof(slasher));
            AssertOwner(engine);
            engine.SnapshotCache.Delete(SlasherKey(slasher));
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public bool IsSlasher(IReadOnlyStore snapshot, UInt160 who)
        {
            return who != UInt160.Zero && snapshot.TryGet(SlasherKey(who), out _);
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowCall | CallFlags.AllowNotify)]
        private async ContractTask Deposit(ApplicationEngine engine, uint chainId, UInt160 sequencer, BigInteger amount)
        {
            AssertBondArgs(chainId, sequencer, amount);
            var asset = GetBondAsset(engine.SnapshotCache);
            RequireNonZero(asset, nameof(asset));
            var caller = engine.CallingScriptHash ?? throw new InvalidOperationException("calling script hash unavailable");

            var transferred = await engine.CallFromNativeContractAsync<bool>(
                Hash, asset, "transfer", caller.ToArray(), Hash.ToArray(), amount, StackItem.Null);
            if (!transferred) throw new InvalidOperationException("asset transfer failed");

            var current = GetBalance(engine.SnapshotCache, chainId, sequencer);
            WriteBalance(engine.SnapshotCache, chainId, sequencer, current + amount);
            Notify(engine, "BondDeposited", chainId, sequencer, amount);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public BigInteger GetBalance(IReadOnlyStore snapshot, uint chainId, UInt160 sequencer)
        {
            return ReadInteger(snapshot, BalanceKey(chainId, sequencer));
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public bool HasMinBond(IReadOnlyStore snapshot, uint chainId, UInt160 sequencer)
        {
            var minBond = GetMinBond(snapshot);
            return minBond > BigInteger.Zero && GetBalance(snapshot, chainId, sequencer) >= minBond;
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowCall | CallFlags.AllowNotify)]
        private async ContractTask Slash(ApplicationEngine engine, uint chainId, UInt160 sequencer, BigInteger amount, UInt160 recipient)
        {
            AssertBondArgs(chainId, sequencer, amount);
            var caller = engine.CallingScriptHash ?? throw new InvalidOperationException("calling script hash unavailable");
            if (!IsSlasher(engine.SnapshotCache, caller)) throw new InvalidOperationException("caller is not an authorized slasher");

            var current = GetBalance(engine.SnapshotCache, chainId, sequencer);
            if (current < amount) throw new InvalidOperationException("insufficient bond");

            WriteBalance(engine.SnapshotCache, chainId, sequencer, current - amount);
            if (recipient != UInt160.Zero)
                await TransferFromBond(engine, recipient, amount);
            Notify(engine, "BondSlashed", chainId, sequencer, amount, recipient);
        }

        [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowCall | CallFlags.AllowNotify)]
        private async ContractTask Withdraw(ApplicationEngine engine, uint chainId, UInt160 sequencer, BigInteger amount)
        {
            AssertBondArgs(chainId, sequencer, amount);
            AssertOwner(engine);

            var current = GetBalance(engine.SnapshotCache, chainId, sequencer);
            if (current < amount) throw new InvalidOperationException("insufficient balance");

            WriteBalance(engine.SnapshotCache, chainId, sequencer, current - amount);
            await TransferFromBond(engine, sequencer, amount);
            Notify(engine, "BondWithdrawn", chainId, sequencer, amount);
        }

        private async ContractTask TransferFromBond(ApplicationEngine engine, UInt160 recipient, BigInteger amount)
        {
            var asset = GetBondAsset(engine.SnapshotCache);
            RequireNonZero(asset, nameof(asset));
            var transferred = await engine.CallFromNativeContractAsync<bool>(
                Hash, asset, "transfer", Hash.ToArray(), recipient.ToArray(), amount, StackItem.Null);
            if (!transferred) throw new InvalidOperationException("asset transfer failed");
        }

        private void WriteBalance(DataCache snapshot, uint chainId, UInt160 sequencer, BigInteger value)
        {
            var key = BalanceKey(chainId, sequencer);
            if (value == BigInteger.Zero)
            {
                snapshot.Delete(key);
                return;
            }
            PutInteger(snapshot, key, value);
        }

        private void WriteSlasher(DataCache snapshot, UInt160 slasher)
        {
            Put(snapshot, SlasherKey(slasher), [1]);
        }

        private StorageKey BalanceKey(uint chainId, UInt160 sequencer)
        {
            var data = new byte[4 + UInt160.Length];
            U32Le(chainId).CopyTo(data, 0);
            sequencer.ToArray().CopyTo(data, 4);
            return CreateStorageKey(PrefixBalance, data);
        }

        private StorageKey SlasherKey(UInt160 slasher) => Key(PrefixSlasher, slasher);

        private static void AssertBondArgs(uint chainId, UInt160 sequencer, BigInteger amount)
        {
            if (chainId == 0) throw new ArgumentOutOfRangeException(nameof(chainId), "chainId 0 is reserved for L1.");
            RequireNonZero(sequencer, nameof(sequencer));
            RequirePositive(amount, nameof(amount));
        }

        private static void RequirePositive(BigInteger amount, string name)
        {
            if (amount <= BigInteger.Zero) throw new ArgumentOutOfRangeException(name, "amount must be positive.");
        }

        private static BigInteger ReadInteger(IReadOnlyStore snapshot, StorageKey key)
        {
            return snapshot.TryGet(key, out var item) ? (BigInteger)item : BigInteger.Zero;
        }

        private static void PutInteger(DataCache snapshot, StorageKey key, BigInteger value)
        {
            snapshot.GetAndChange(key, () => new StorageItem(BigInteger.Zero)).Set(value);
        }
    }

}
