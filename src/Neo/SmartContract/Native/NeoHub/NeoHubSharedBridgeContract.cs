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

}
