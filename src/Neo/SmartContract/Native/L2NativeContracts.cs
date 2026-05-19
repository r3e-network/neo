// Copyright (C) 2015-2026 The Neo Project.
//
// N4 L2 native contracts are maintained by r3e-network in the r3e/neo-n4-core
// branch. They are registered as Neo native contracts so an L2 chain does not
// deploy these system contracts after genesis.

#pragma warning disable IDE0051

using Neo.Cryptography;
using Neo.Extensions;
using Neo.Extensions.IO;
using Neo.Persistence;
using Neo.SmartContract.Manifest;
using Neo.VM.Types;
using System.Numerics;
using System.Text;

namespace Neo.SmartContract.Native;

public abstract class L2NativeContract : NativeContract
{
    private protected L2NativeContract(int id) : base(id) { }

    protected UInt160 ReadUInt160(IReadOnlyStore snapshot, byte prefix)
    {
        return snapshot.TryGet(CreateStorageKey(prefix), out var item) ? new UInt160(item.Value.Span) : UInt160.Zero;
    }

    protected UInt160 ReadUInt160(IReadOnlyStore snapshot, StorageKey key)
    {
        return snapshot.TryGet(key, out var item) ? new UInt160(item.Value.Span) : UInt160.Zero;
    }

    protected void WriteUInt160(DataCache snapshot, byte prefix, UInt160 value)
    {
        snapshot.GetAndChange(CreateStorageKey(prefix), () => new StorageItem(value.ToArray())).Value = value.ToArray();
    }

    protected static void RequireNonZero(UInt160 value, string name)
    {
        if (value == UInt160.Zero) throw new ArgumentException($"{name} must be non-zero.", name);
    }

    protected static void RequirePositive(BigInteger value, string name)
    {
        if (value <= 0) throw new ArgumentOutOfRangeException(name, "must be positive.");
    }

    protected BigInteger ReadInteger(IReadOnlyStore snapshot, byte prefix)
    {
        return snapshot.TryGet(CreateStorageKey(prefix), out var item) ? (BigInteger)item : BigInteger.Zero;
    }

    protected BigInteger ReadInteger(IReadOnlyStore snapshot, StorageKey key)
    {
        return snapshot.TryGet(key, out var item) ? (BigInteger)item : BigInteger.Zero;
    }

    protected void WriteInteger(DataCache snapshot, byte prefix, BigInteger value)
    {
        snapshot.GetAndChange(CreateStorageKey(prefix), () => new StorageItem(BigInteger.Zero)).Set(value);
    }

    protected static void WriteInteger(DataCache snapshot, StorageKey key, BigInteger value)
    {
        snapshot.GetAndChange(key, () => new StorageItem(BigInteger.Zero)).Set(value);
    }

    protected static void AssertWitness(ApplicationEngine engine, UInt160 account, string message)
    {
        if (!engine.CheckWitnessInternal(account)) throw new InvalidOperationException(message);
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

    protected void AssertSystem(ApplicationEngine engine, byte systemPrefix = 0xfe)
    {
        var system = ReadUInt160(engine.SnapshotCache, systemPrefix);
        if (system == UInt160.Zero) throw new InvalidOperationException("system account unset");
        AssertWitness(engine, system, "not system");
    }

    protected UInt160 CallingScriptHash(ApplicationEngine engine)
    {
        return engine.CallingScriptHash ?? throw new InvalidOperationException("calling script hash unavailable");
    }

    protected static bool IsForeignChainId(uint externalChainId)
    {
        return (externalChainId & 0xff000000u) == 0xe0000000u;
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

    protected static ulong ReadU64Le(ReadOnlySpan<byte> bytes)
    {
        return ((ulong)bytes[0])
            | ((ulong)bytes[1] << 8)
            | ((ulong)bytes[2] << 16)
            | ((ulong)bytes[3] << 24)
            | ((ulong)bytes[4] << 32)
            | ((ulong)bytes[5] << 40)
            | ((ulong)bytes[6] << 48)
            | ((ulong)bytes[7] << 56);
    }

    protected StorageKey Key(byte prefix, UInt160 value) => CreateStorageKey(prefix, value.ToArray());

    protected StorageKey Key(byte prefix, UInt160 left, UInt160 right)
    {
        var data = new byte[UInt160.Length * 2];
        left.ToArray().CopyTo(data, 0);
        right.ToArray().CopyTo(data, UInt160.Length);
        return CreateStorageKey(prefix, data);
    }

    protected StorageKey Key(byte prefix, uint left, ulong right)
    {
        var data = new byte[12];
        U32Le(left).CopyTo(data, 0);
        U64Le(right).CopyTo(data, 4);
        return CreateStorageKey(prefix, data);
    }

    protected StorageKey Key(byte prefix, uint left, UInt160 right)
    {
        var data = new byte[4 + UInt160.Length];
        U32Le(left).CopyTo(data, 0);
        right.ToArray().CopyTo(data, 4);
        return CreateStorageKey(prefix, data);
    }
}

[ContractEvent(0, name: "ConfigUpdated", "slot", ContractParameterType.Integer, "value", ContractParameterType.ByteArray)]
public sealed class L2SystemConfigContract : L2NativeContract
{
    private const byte KeySystemAccount = 0x01;
    private const byte KeyL1MessageContract = 0x02;
    private const byte KeyBridgeContract = 0x03;
    private const byte KeyMessageContract = 0x04;
    private const byte KeyBatchInfoContract = 0x05;
    private const byte KeyFeeContract = 0x06;
    private const byte KeyPaymasterContract = 0x07;
    private const byte KeyChainId = 0x08;
    private const byte KeySettingsBlob = 0x09;
    private const byte KeyOwner = 0xff;

    internal L2SystemConfigContract() : base(-101) { }

    [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
    private void Configure(ApplicationEngine engine, UInt160 owner, UInt160 systemAccount, uint chainId)
    {
        RequireNonZero(owner, nameof(owner));
        RequireNonZero(systemAccount, nameof(systemAccount));
        if (chainId == 0) throw new ArgumentOutOfRangeException(nameof(chainId), "chainId 0 is reserved for L1.");
        AssertOwnerOrCommittee(engine, KeyOwner);
        WriteUInt160(engine.SnapshotCache, KeyOwner, owner);
        WriteUInt160(engine.SnapshotCache, KeySystemAccount, systemAccount);
        WriteInteger(engine.SnapshotCache, KeyChainId, chainId);
    }

    [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
    public UInt160 GetOwner(IReadOnlyStore snapshot) => ReadUInt160(snapshot, KeyOwner);

    [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
    public UInt160 GetSystemAccount(IReadOnlyStore snapshot) => ReadUInt160(snapshot, KeySystemAccount);

    [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
    public uint GetChainId(IReadOnlyStore snapshot) => (uint)ReadInteger(snapshot, KeyChainId);

    [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
    private void SetSlot(ApplicationEngine engine, byte slot, byte[] value)
    {
        AssertOwnerOrCommittee(engine, KeyOwner);
        if (slot < KeyL1MessageContract || slot > KeySettingsBlob) throw new ArgumentOutOfRangeException(nameof(slot), "slot out of range.");
        engine.SnapshotCache.GetAndChange(CreateStorageKey(slot), () => new StorageItem()).Value = value;
        Notify(engine, "ConfigUpdated", slot, value);
    }

    [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
    public byte[] GetSlot(IReadOnlyStore snapshot, byte slot)
    {
        return snapshot.TryGet(CreateStorageKey(slot), out var item) ? item.Value.ToArray() : [];
    }

    [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
    public UInt160 GetAddressSlot(IReadOnlyStore snapshot, byte slot)
    {
        var raw = GetSlot(snapshot, slot);
        return raw.Length == UInt160.Length ? new UInt160(raw) : UInt160.Zero;
    }
}

[ContractEvent(0, name: "BatchAdvanced", "chainId", ContractParameterType.Integer, "batchNumber", ContractParameterType.Integer, "l1FinalizedHeight", ContractParameterType.Integer)]
public sealed class L2BatchInfoContract : L2NativeContract
{
    private const byte KeyChainId = 0x01;
    private const byte KeyBatchNumber = 0x02;
    private const byte KeyL1FinalizedHeight = 0x03;
    private const byte KeySystemAccount = 0xfe;
    private const byte KeyOwner = 0xff;

    internal L2BatchInfoContract() : base(-102) { }

    [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
    private void Configure(ApplicationEngine engine, UInt160 owner, UInt160 systemAccount, uint chainId)
    {
        RequireNonZero(owner, nameof(owner));
        RequireNonZero(systemAccount, nameof(systemAccount));
        if (chainId == 0) throw new ArgumentOutOfRangeException(nameof(chainId), "chainId 0 is reserved for L1.");
        AssertOwnerOrCommittee(engine, KeyOwner);
        WriteUInt160(engine.SnapshotCache, KeyOwner, owner);
        WriteUInt160(engine.SnapshotCache, KeySystemAccount, systemAccount);
        WriteInteger(engine.SnapshotCache, KeyChainId, chainId);
    }

    [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
    public uint GetChainId(IReadOnlyStore snapshot) => (uint)ReadInteger(snapshot, KeyChainId);

    [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
    public ulong GetBatchNumber(IReadOnlyStore snapshot) => (ulong)ReadInteger(snapshot, KeyBatchNumber);

    [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
    public uint GetL1FinalizedHeight(IReadOnlyStore snapshot) => (uint)ReadInteger(snapshot, KeyL1FinalizedHeight);

    [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
    private void Advance(ApplicationEngine engine, ulong newBatchNumber, uint newL1Height)
    {
        AssertSystem(engine, KeySystemAccount);
        var current = GetBatchNumber(engine.SnapshotCache);
        if (newBatchNumber != current + 1) throw new InvalidOperationException("batch number out of sequence");
        if (newL1Height < GetL1FinalizedHeight(engine.SnapshotCache)) throw new InvalidOperationException("L1 finalized height must not decrease");
        WriteInteger(engine.SnapshotCache, KeyBatchNumber, newBatchNumber);
        WriteInteger(engine.SnapshotCache, KeyL1FinalizedHeight, newL1Height);
        Notify(engine, "BatchAdvanced", GetChainId(engine.SnapshotCache), newBatchNumber, newL1Height);
    }
}

[ContractEvent(0, name: "MessageEmitted", "sourceChainId", ContractParameterType.Integer, "targetChainId", ContractParameterType.Integer, "nonce", ContractParameterType.Integer, "sender", ContractParameterType.Hash160, "receiver", ContractParameterType.Hash160, "messageType", ContractParameterType.Integer)]
[ContractEvent(1, name: "InboundApplied", "sourceChainId", ContractParameterType.Integer, "nonce", ContractParameterType.Integer, "receiver", ContractParameterType.Hash160)]
public sealed class L2MessageContract : L2NativeContract
{
    private const byte PrefixOutboundNonce = 0x01;
    private const byte PrefixInboundConsumed = 0x02;
    private const byte KeyChainId = 0x03;
    private const byte KeySystemAccount = 0xfe;
    private const byte KeyOwner = 0xff;

    internal L2MessageContract() : base(-103) { }

    [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
    private void Configure(ApplicationEngine engine, UInt160 owner, UInt160 systemAccount, uint chainId)
    {
        RequireNonZero(owner, nameof(owner));
        RequireNonZero(systemAccount, nameof(systemAccount));
        if (chainId == 0) throw new ArgumentOutOfRangeException(nameof(chainId), "chainId 0 is reserved for L1.");
        AssertOwnerOrCommittee(engine, KeyOwner);
        WriteUInt160(engine.SnapshotCache, KeyOwner, owner);
        WriteUInt160(engine.SnapshotCache, KeySystemAccount, systemAccount);
        WriteInteger(engine.SnapshotCache, KeyChainId, chainId);
    }

    [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
    public uint GetChainId(IReadOnlyStore snapshot) => (uint)ReadInteger(snapshot, KeyChainId);

    [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
    private ulong EmitMessage(ApplicationEngine engine, uint targetChainId, UInt160 receiver, byte messageType, byte[] payload)
    {
        RequireNonZero(receiver, nameof(receiver));
        if (targetChainId == GetChainId(engine.SnapshotCache)) throw new InvalidOperationException("self-targeted message");
        var sender = CallingScriptHash(engine);
        var nonce = NextNonce(engine.SnapshotCache, sender);
        Notify(engine, "MessageEmitted", GetChainId(engine.SnapshotCache), targetChainId, nonce, sender, receiver, messageType);
        return nonce;
    }

    [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
    private async ContractTask ApplyInbound(ApplicationEngine engine, uint sourceChainId, ulong nonce, UInt160 receiver, byte messageType, byte[] payload)
    {
        AssertSystem(engine, KeySystemAccount);
        var key = Key(PrefixInboundConsumed, sourceChainId, nonce);
        if (engine.SnapshotCache.Contains(key)) throw new InvalidOperationException("inbound replayed");
        engine.SnapshotCache.Add(key, new StorageItem(new byte[] { 1 }));
        if (messageType != 0)
            await engine.CallFromNativeContractAsync(Hash, receiver, "onCrossChainMessage", sourceChainId, nonce, messageType, payload);
        Notify(engine, "InboundApplied", sourceChainId, nonce, receiver);
    }

    [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
    public bool HasConsumed(IReadOnlyStore snapshot, uint sourceChainId, ulong nonce) => snapshot.Contains(Key(PrefixInboundConsumed, sourceChainId, nonce));

    private ulong NextNonce(DataCache snapshot, UInt160 sender)
    {
        var key = Key(PrefixOutboundNonce, sender);
        var next = (ulong)ReadInteger(snapshot, key) + 1;
        snapshot.GetAndChange(key, () => new StorageItem(BigInteger.Zero)).Set(next);
        return next;
    }
}

[ContractEvent(0, name: "Mint", "l1Asset", ContractParameterType.Hash160, "recipient", ContractParameterType.Hash160, "amount", ContractParameterType.Integer, "sourceChainId", ContractParameterType.Integer, "nonce", ContractParameterType.Integer)]
[ContractEvent(1, name: "WithdrawalEmitted", "sender", ContractParameterType.Hash160, "l1Recipient", ContractParameterType.Hash160, "l2Asset", ContractParameterType.Hash160, "amount", ContractParameterType.Integer, "nonce", ContractParameterType.Integer)]
public sealed class L2BridgeContract : L2NativeContract
{
    private const byte PrefixMapping = 0x01;
    private const byte PrefixDepositConsumed = 0x02;
    private const byte PrefixWithdrawalNonce = 0x03;
    private const byte PrefixMappingByL2 = 0x04;
    private const byte MaxTokenDecimals = 18;
    private const byte KeySystemAccount = 0xfe;
    private const byte KeyOwner = 0xff;

    internal L2BridgeContract() : base(-104) { }

    [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
    private void Configure(ApplicationEngine engine, UInt160 owner, UInt160 systemAccount)
    {
        RequireNonZero(owner, nameof(owner));
        RequireNonZero(systemAccount, nameof(systemAccount));
        AssertOwnerOrCommittee(engine, KeyOwner);
        WriteUInt160(engine.SnapshotCache, KeyOwner, owner);
        WriteUInt160(engine.SnapshotCache, KeySystemAccount, systemAccount);
    }

    [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
    private void RegisterMapping(ApplicationEngine engine, UInt160 l1Asset, UInt160 l2Asset, byte l1Decimals, byte l2Decimals)
    {
        AssertOwnerOrCommittee(engine, KeyOwner);
        RequireNonZero(l1Asset, nameof(l1Asset));
        RequireNonZero(l2Asset, nameof(l2Asset));
        ValidateDecimals(l1Decimals, nameof(l1Decimals));
        ValidateDecimals(l2Decimals, nameof(l2Decimals));
        ValidatePlatformMapping(l2Asset, l1Decimals, l2Decimals);

        var oldByL1 = ReadMapping(engine.SnapshotCache, Key(PrefixMapping, l1Asset));
        if (oldByL1.Asset != UInt160.Zero && oldByL1.Asset != l2Asset)
            engine.SnapshotCache.Delete(Key(PrefixMappingByL2, oldByL1.Asset));

        var oldByL2 = ReadMapping(engine.SnapshotCache, Key(PrefixMappingByL2, l2Asset));
        if (oldByL2.Asset != UInt160.Zero && oldByL2.Asset != l1Asset)
            engine.SnapshotCache.Delete(Key(PrefixMapping, oldByL2.Asset));

        engine.SnapshotCache.GetAndChange(Key(PrefixMapping, l1Asset), () => new StorageItem()).Value =
            EncodeMapping(l2Asset, l1Decimals, l2Decimals);
        engine.SnapshotCache.GetAndChange(Key(PrefixMappingByL2, l2Asset), () => new StorageItem()).Value =
            EncodeMapping(l1Asset, l1Decimals, l2Decimals);
    }

    [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
    public UInt160 GetL2Asset(IReadOnlyStore snapshot, UInt160 l1Asset) => ReadMapping(snapshot, Key(PrefixMapping, l1Asset)).Asset;

    [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
    public UInt160 GetL1Asset(IReadOnlyStore snapshot, UInt160 l2Asset) => ReadMapping(snapshot, Key(PrefixMappingByL2, l2Asset)).Asset;

    [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
    public byte GetL1Decimals(IReadOnlyStore snapshot, UInt160 l1Asset) => ReadMapping(snapshot, Key(PrefixMapping, l1Asset)).L1Decimals;

    [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
    public byte GetL2Decimals(IReadOnlyStore snapshot, UInt160 l1Asset) => ReadMapping(snapshot, Key(PrefixMapping, l1Asset)).L2Decimals;

    [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
    private async ContractTask ApplyDeposit(ApplicationEngine engine, uint sourceChainId, ulong nonce, UInt160 l1Asset, UInt160 recipient, BigInteger amount)
    {
        AssertSystem(engine, KeySystemAccount);
        RequirePositive(amount, nameof(amount));
        RequireNonZero(recipient, nameof(recipient));
        var dedupe = Key(PrefixDepositConsumed, sourceChainId, nonce);
        if (engine.SnapshotCache.Contains(dedupe)) throw new InvalidOperationException("deposit replayed");
        engine.SnapshotCache.Add(dedupe, new StorageItem(new byte[] { 1 }));
        var mapping = ReadMapping(engine.SnapshotCache, Key(PrefixMapping, l1Asset));
        RequireNonZero(mapping.Asset, nameof(l1Asset));
        var l2Amount = ScaleAmount(amount, mapping.L1Decimals, mapping.L2Decimals);
        if (IsPlatformToken(mapping.Asset))
            await NativeContract.TokenManagement.MintInternal(engine, mapping.Asset, recipient, l2Amount,
                assertOwner: false, callOnBalanceChanged: true, callOnPayment: true, callOnTransfer: true);
        else
            await engine.CallFromNativeContractAsync(Hash, NativeContract.BridgedNep17.Hash, "mint", mapping.Asset, recipient, l2Amount);
        Notify(engine, "Mint", l1Asset, recipient, l2Amount, sourceChainId, nonce);
    }

    [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
    private async ContractTask<ulong> InitiateWithdrawal(ApplicationEngine engine, UInt160 l2Asset, BigInteger amount, UInt160 l1Recipient)
    {
        RequireNonZero(l2Asset, nameof(l2Asset));
        RequirePositive(amount, nameof(amount));
        RequireNonZero(l1Recipient, nameof(l1Recipient));
        var mapping = ReadMapping(engine.SnapshotCache, Key(PrefixMappingByL2, l2Asset));
        RequireNonZero(mapping.Asset, nameof(l2Asset));
        var l1Amount = ScaleAmount(amount, mapping.L2Decimals, mapping.L1Decimals);
        var caller = CallingScriptHash(engine);
        var nonce = NextNonce(engine.SnapshotCache, caller);
        if (IsPlatformToken(l2Asset))
            await NativeContract.TokenManagement.BurnInternal(engine, l2Asset, caller, amount,
                assertOwner: false, callOnBalanceChanged: true, callOnTransfer: true);
        else
            await engine.CallFromNativeContractAsync(Hash, NativeContract.BridgedNep17.Hash, "burn", l2Asset, caller, amount);
        Notify(engine, "WithdrawalEmitted", caller, l1Recipient, l2Asset, l1Amount, nonce);
        return nonce;
    }

    private static bool IsPlatformToken(UInt160 l2Asset)
    {
        return l2Asset == NativeContract.Governance.NeoTokenId
            || l2Asset == NativeContract.Governance.GasTokenId;
    }

    private static void ValidateDecimals(byte decimals, string name)
    {
        if (decimals > MaxTokenDecimals) throw new ArgumentOutOfRangeException(name, "decimals must be between 0 and 18.");
    }

    private static void ValidatePlatformMapping(UInt160 l2Asset, byte l1Decimals, byte l2Decimals)
    {
        if (l2Asset == NativeContract.Governance.GasTokenId &&
            (l1Decimals != Governance.GasTokenDecimals || l2Decimals != Governance.GasTokenDecimals))
            throw new InvalidOperationException("GAS mapping must use 8 decimals on both sides");
        if (l2Asset == NativeContract.BridgedNep17.L2NeoTokenId &&
            (l1Decimals != Governance.NeoTokenDecimals || l2Decimals != BridgedNep17Contract.PlatformNeoDecimals))
            throw new InvalidOperationException("NEO mapping must convert L1 0 decimals to L2 8 decimals");
        ValidateFixedPlatformMapping(l2Asset, NativeContract.BridgedNep17.L2UsdtTokenId, l1Decimals, l2Decimals,
            BridgedNep17Contract.PlatformUsdtDecimals, BridgedNep17Contract.PlatformUsdtDecimals, "USDT");
        ValidateFixedPlatformMapping(l2Asset, NativeContract.BridgedNep17.L2UsdcTokenId, l1Decimals, l2Decimals,
            BridgedNep17Contract.PlatformUsdcDecimals, BridgedNep17Contract.PlatformUsdcDecimals, "USDC");
        ValidateFixedPlatformMapping(l2Asset, NativeContract.BridgedNep17.L2BtcTokenId, l1Decimals, l2Decimals,
            BridgedNep17Contract.PlatformBtcDecimals, BridgedNep17Contract.PlatformBtcDecimals, "BTC");
    }

    private static void ValidateFixedPlatformMapping(
        UInt160 l2Asset,
        UInt160 expectedAsset,
        byte l1Decimals,
        byte l2Decimals,
        byte expectedL1Decimals,
        byte expectedL2Decimals,
        string symbol)
    {
        if (l2Asset == expectedAsset && (l1Decimals != expectedL1Decimals || l2Decimals != expectedL2Decimals))
            throw new InvalidOperationException($"{symbol} mapping must use {expectedL1Decimals} decimals on L1 and {expectedL2Decimals} decimals on L2");
    }

    private static BigInteger ScaleAmount(BigInteger amount, byte fromDecimals, byte toDecimals)
    {
        ValidateDecimals(fromDecimals, nameof(fromDecimals));
        ValidateDecimals(toDecimals, nameof(toDecimals));
        if (fromDecimals == toDecimals) return amount;
        var factor = BigInteger.Pow(10, Math.Abs(toDecimals - fromDecimals));
        if (toDecimals > fromDecimals) return amount * factor;
        var quotient = BigInteger.DivRem(amount, factor, out var remainder);
        if (remainder != BigInteger.Zero)
            throw new InvalidOperationException("amount cannot be represented exactly in the target decimal domain");
        return quotient;
    }

    private static byte[] EncodeMapping(UInt160 asset, byte l1Decimals, byte l2Decimals)
    {
        var data = new byte[UInt160.Length + 2];
        asset.ToArray().CopyTo(data, 0);
        data[UInt160.Length] = l1Decimals;
        data[UInt160.Length + 1] = l2Decimals;
        return data;
    }

    private MappingEntry ReadMapping(IReadOnlyStore snapshot, StorageKey key)
    {
        if (!snapshot.TryGet(key, out var item)) return new MappingEntry(UInt160.Zero, 0, 0);
        var bytes = item.Value.Span;
        if (bytes.Length < UInt160.Length) return new MappingEntry(UInt160.Zero, 0, 0);
        var asset = new UInt160(bytes[..UInt160.Length]);
        if (bytes.Length == UInt160.Length) return new MappingEntry(asset, 8, 8);
        if (bytes.Length < UInt160.Length + 2) return new MappingEntry(UInt160.Zero, 0, 0);
        return new MappingEntry(asset, bytes[UInt160.Length], bytes[UInt160.Length + 1]);
    }

    private ulong NextNonce(DataCache snapshot, UInt160 sender)
    {
        var key = Key(PrefixWithdrawalNonce, sender);
        var next = (ulong)ReadInteger(snapshot, key) + 1;
        snapshot.GetAndChange(key, () => new StorageItem(BigInteger.Zero)).Set(next);
        return next;
    }

    private readonly record struct MappingEntry(UInt160 Asset, byte L1Decimals, byte L2Decimals);
}

[ContractEvent(0, name: "FeesDistributed", "amount", ContractParameterType.Integer, "sequencerShare", ContractParameterType.Integer, "proverShare", ContractParameterType.Integer, "daShare", ContractParameterType.Integer)]
public sealed class L2FeeContract : L2NativeContract
{
    public const ushort BasisPointsTotal = 10_000;
    private const byte KeySequencerBps = 0x01;
    private const byte KeyProverBps = 0x02;
    private const byte KeyDABps = 0x03;
    private const byte KeySequencerAddress = 0x04;
    private const byte KeyProverAddress = 0x05;
    private const byte KeyDAAddress = 0x06;
    private const byte KeyFeeAsset = 0x07;
    private const byte KeyOwner = 0xff;

    internal L2FeeContract() : base(-105) { }

    [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
    private void Configure(ApplicationEngine engine, UInt160 owner, UInt160 feeAsset, UInt160 sequencer, UInt160 prover, UInt160 da, uint sequencerBps, uint proverBps, uint daBps)
    {
        AssertOwnerOrCommittee(engine, KeyOwner);
        RequireNonZero(owner, nameof(owner));
        RequireNonZero(feeAsset, nameof(feeAsset));
        RequireNonZero(sequencer, nameof(sequencer));
        RequireNonZero(prover, nameof(prover));
        RequireNonZero(da, nameof(da));
        ValidateBps(sequencerBps, proverBps, daBps);
        WriteUInt160(engine.SnapshotCache, KeyOwner, owner);
        WriteUInt160(engine.SnapshotCache, KeyFeeAsset, feeAsset);
        WriteUInt160(engine.SnapshotCache, KeySequencerAddress, sequencer);
        WriteUInt160(engine.SnapshotCache, KeyProverAddress, prover);
        WriteUInt160(engine.SnapshotCache, KeyDAAddress, da);
        SetBpsInternal(engine.SnapshotCache, sequencerBps, proverBps, daBps);
    }

    [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
    private void SetBps(ApplicationEngine engine, uint sequencerBps, uint proverBps, uint daBps)
    {
        AssertOwnerOrCommittee(engine, KeyOwner);
        ValidateBps(sequencerBps, proverBps, daBps);
        SetBpsInternal(engine.SnapshotCache, sequencerBps, proverBps, daBps);
    }

    [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
    public uint[] GetBps(IReadOnlyStore snapshot) =>
    [
        (uint)ReadInteger(snapshot, KeySequencerBps),
        (uint)ReadInteger(snapshot, KeyProverBps),
        (uint)ReadInteger(snapshot, KeyDABps)
    ];

    [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
    private async ContractTask Distribute(ApplicationEngine engine, BigInteger amount)
    {
        AssertOwnerOrCommittee(engine, KeyOwner);
        RequirePositive(amount, nameof(amount));
        var bps = GetBps(engine.SnapshotCache);
        var sequencerShare = amount * bps[0] / BasisPointsTotal;
        var proverShare = amount * bps[1] / BasisPointsTotal;
        var daShare = amount - sequencerShare - proverShare;
        var asset = ReadUInt160(engine.SnapshotCache, KeyFeeAsset);
        await TransferFeeShare(engine, asset, ReadUInt160(engine.SnapshotCache, KeySequencerAddress), sequencerShare);
        await TransferFeeShare(engine, asset, ReadUInt160(engine.SnapshotCache, KeyProverAddress), proverShare);
        await TransferFeeShare(engine, asset, ReadUInt160(engine.SnapshotCache, KeyDAAddress), daShare);
        Notify(engine, "FeesDistributed", amount, sequencerShare, proverShare, daShare);
    }

    private async ContractTask TransferFeeShare(ApplicationEngine engine, UInt160 asset, UInt160 recipient, BigInteger amount)
    {
        if (amount <= 0) return;
        if (!await engine.CallFromNativeContractAsync<bool>(Hash, asset, "transfer", Hash, recipient, amount, StackItem.Null))
            throw new InvalidOperationException("fee transfer failed");
    }

    private void SetBpsInternal(DataCache snapshot, uint sequencerBps, uint proverBps, uint daBps)
    {
        WriteInteger(snapshot, KeySequencerBps, sequencerBps);
        WriteInteger(snapshot, KeyProverBps, proverBps);
        WriteInteger(snapshot, KeyDABps, daBps);
    }

    private static void ValidateBps(uint sequencerBps, uint proverBps, uint daBps)
    {
        if (sequencerBps + proverBps + daBps != BasisPointsTotal) throw new InvalidOperationException("bps must sum to 10000");
    }
}

[ContractEvent(0, name: "TopUp", "user", ContractParameterType.Hash160, "asset", ContractParameterType.Hash160, "amount", ContractParameterType.Integer)]
[ContractEvent(1, name: "FeeCharged", "user", ContractParameterType.Hash160, "asset", ContractParameterType.Hash160, "amount", ContractParameterType.Integer)]
public sealed class L2PaymasterContract : L2NativeContract
{
    private const byte PrefixApprovedAsset = 0x01;
    private const byte PrefixBalance = 0x02;
    private const byte KeyFeeContract = 0xfd;
    private const byte KeyOwner = 0xff;

    internal L2PaymasterContract() : base(-106) { }

    [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
    private void Configure(ApplicationEngine engine, UInt160 owner, UInt160 feeContract)
    {
        AssertOwnerOrCommittee(engine, KeyOwner);
        RequireNonZero(owner, nameof(owner));
        RequireNonZero(feeContract, nameof(feeContract));
        WriteUInt160(engine.SnapshotCache, KeyOwner, owner);
        WriteUInt160(engine.SnapshotCache, KeyFeeContract, feeContract);
    }

    [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
    private void ApproveAsset(ApplicationEngine engine, UInt160 asset)
    {
        AssertOwnerOrCommittee(engine, KeyOwner);
        RequireNonZero(asset, nameof(asset));
        engine.SnapshotCache.GetAndChange(Key(PrefixApprovedAsset, asset), () => new StorageItem(new byte[] { 1 })).Value = new byte[] { 1 };
    }

    [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
    public bool IsApproved(IReadOnlyStore snapshot, UInt160 asset) => snapshot.Contains(Key(PrefixApprovedAsset, asset));

    [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
    private async ContractTask TopUp(ApplicationEngine engine, UInt160 user, UInt160 asset, BigInteger amount)
    {
        RequireNonZero(user, nameof(user));
        RequireNonZero(asset, nameof(asset));
        RequirePositive(amount, nameof(amount));
        if (!IsApproved(engine.SnapshotCache, asset)) throw new InvalidOperationException("asset not approved");
        var caller = CallingScriptHash(engine);
        if (!await engine.CallFromNativeContractAsync<bool>(Hash, asset, "transfer", caller, Hash, amount, StackItem.Null))
            throw new InvalidOperationException("asset transfer failed");
        var key = Key(PrefixBalance, user, asset);
        engine.SnapshotCache.GetAndChange(key, () => new StorageItem(BigInteger.Zero)).Add(amount);
        Notify(engine, "TopUp", user, asset, amount);
    }

    [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
    public BigInteger GetBalance(IReadOnlyStore snapshot, UInt160 user, UInt160 asset) => ReadInteger(snapshot, Key(PrefixBalance, user, asset));

    [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
    private void Charge(ApplicationEngine engine, UInt160 user, UInt160 asset, BigInteger amount)
    {
        var feeContract = ReadUInt160(engine.SnapshotCache, KeyFeeContract);
        if (CallingScriptHash(engine) != feeContract && !engine.CheckWitnessInternal(feeContract)) throw new InvalidOperationException("not fee contract");
        RequirePositive(amount, nameof(amount));
        var key = Key(PrefixBalance, user, asset);
        var current = ReadInteger(engine.SnapshotCache, key);
        if (current < amount) throw new InvalidOperationException("insufficient balance");
        engine.SnapshotCache.GetAndChange(key)!.Set(current - amount);
        Notify(engine, "FeeCharged", user, asset, amount);
    }
}

[ContractEvent(0, name: "ExternalSendInitiated", "externalChainId", ContractParameterType.Integer, "nonce", ContractParameterType.Integer, "sender", ContractParameterType.Hash160, "recipient", ContractParameterType.Hash160, "l2Asset", ContractParameterType.Hash160, "amount", ContractParameterType.Integer, "calldata", ContractParameterType.ByteArray)]
[ContractEvent(1, name: "ExternalInboundApplied", "externalChainId", ContractParameterType.Integer, "nonce", ContractParameterType.Integer, "foreignSender", ContractParameterType.Hash160, "l2Recipient", ContractParameterType.Hash160, "amount", ContractParameterType.Integer)]
public sealed class L2NativeExternalBridgeContract : L2NativeContract
{
    private const byte PrefixOutboundNonce = 0x01;
    private const byte PrefixConsumedInboundNonce = 0x02;
    private const byte PrefixAssetMapping = 0x03;
    private const byte PrefixReverseAssetMapping = 0x04;
    private const byte KeySystemAccount = 0xfe;
    private const byte KeyOwner = 0xff;

    internal L2NativeExternalBridgeContract() : base(-107) { }

    [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
    private void Configure(ApplicationEngine engine, UInt160 owner, UInt160 systemAccount)
    {
        AssertOwnerOrCommittee(engine, KeyOwner);
        RequireNonZero(owner, nameof(owner));
        RequireNonZero(systemAccount, nameof(systemAccount));
        WriteUInt160(engine.SnapshotCache, KeyOwner, owner);
        WriteUInt160(engine.SnapshotCache, KeySystemAccount, systemAccount);
    }

    [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
    public UInt160 GetOwner(IReadOnlyStore snapshot) => ReadUInt160(snapshot, KeyOwner);

    [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
    public UInt160 GetSystemAccount(IReadOnlyStore snapshot) => ReadUInt160(snapshot, KeySystemAccount);

    [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
    private void RegisterAssetMapping(ApplicationEngine engine, uint externalChainId, UInt160 foreignAsset, UInt160 l2Asset)
    {
        AssertOwnerOrCommittee(engine, KeyOwner);
        if (!IsForeignChainId(externalChainId)) throw new ArgumentOutOfRangeException(nameof(externalChainId), "externalChainId must use 0xE0 namespace.");
        RequireNonZero(l2Asset, nameof(l2Asset));
        var reverseKey = Key(PrefixReverseAssetMapping, externalChainId, l2Asset);
        if (engine.SnapshotCache.Contains(reverseKey))
        {
            var existingForeignAsset = ReadUInt160(engine.SnapshotCache, reverseKey);
            if (existingForeignAsset != foreignAsset) throw new InvalidOperationException("L2 asset already mapped to another foreign asset");
        }
        engine.SnapshotCache.GetAndChange(Key(PrefixAssetMapping, externalChainId, foreignAsset), () => new StorageItem(l2Asset.ToArray())).Value = l2Asset.ToArray();
        engine.SnapshotCache.GetAndChange(reverseKey, () => new StorageItem(foreignAsset.ToArray())).Value = foreignAsset.ToArray();
    }

    [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
    public UInt160 GetAssetMapping(IReadOnlyStore snapshot, uint externalChainId, UInt160 foreignAsset) => ReadUInt160(snapshot, Key(PrefixAssetMapping, externalChainId, foreignAsset));

    [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
    public bool IsL2AssetRegistered(IReadOnlyStore snapshot, uint externalChainId, UInt160 l2Asset) => snapshot.Contains(Key(PrefixReverseAssetMapping, externalChainId, l2Asset));

    [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
    private async ContractTask<ulong> Send(ApplicationEngine engine, uint externalChainId, UInt160 recipient, UInt160 l2Asset, BigInteger amount, byte[] calldata, ulong deadlineUnixSeconds)
    {
        if (!IsForeignChainId(externalChainId)) throw new ArgumentOutOfRangeException(nameof(externalChainId), "externalChainId must use 0xE0 namespace.");
        RequireNonZero(recipient, nameof(recipient));
        RequireNonZero(l2Asset, nameof(l2Asset));
        RequirePositive(amount, nameof(amount));
        if (!IsL2AssetRegistered(engine.SnapshotCache, externalChainId, l2Asset)) throw new InvalidOperationException("asset not registered for external chain");
        var sender = CallingScriptHash(engine);
        await engine.CallFromNativeContractAsync(Hash, NativeContract.BridgedNep17.Hash, "burn", l2Asset, sender, amount);
        var nonceKey = CreateStorageKey(PrefixOutboundNonce, U32Le(externalChainId));
        var next = (ulong)ReadInteger(engine.SnapshotCache, nonceKey) + 1;
        engine.SnapshotCache.GetAndChange(nonceKey, () => new StorageItem(BigInteger.Zero)).Set(next);
        Notify(engine, "ExternalSendInitiated", externalChainId, next, sender, recipient, l2Asset, amount, calldata);
        return next;
    }

    [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
    private async ContractTask ApplyInbound(ApplicationEngine engine, uint externalChainId, ulong nonce, UInt160 foreignSender, UInt160 l2Recipient, UInt160 l2Asset, BigInteger amount)
    {
        AssertSystem(engine, KeySystemAccount);
        RequirePositive(amount, nameof(amount));
        RequireNonZero(l2Recipient, nameof(l2Recipient));
        RequireNonZero(l2Asset, nameof(l2Asset));
        if (!IsL2AssetRegistered(engine.SnapshotCache, externalChainId, l2Asset)) throw new InvalidOperationException("asset not registered for external chain");
        var consumed = Key(PrefixConsumedInboundNonce, externalChainId, nonce);
        if (engine.SnapshotCache.Contains(consumed)) throw new InvalidOperationException("inbound nonce already consumed");
        await engine.CallFromNativeContractAsync(Hash, NativeContract.BridgedNep17.Hash, "mint", l2Asset, l2Recipient, amount);
        engine.SnapshotCache.Add(consumed, new StorageItem(new byte[] { 1 }));
        Notify(engine, "ExternalInboundApplied", externalChainId, nonce, foreignSender, l2Recipient, amount);
    }

    [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
    public ulong GetLastOutboundNonce(IReadOnlyStore snapshot, uint externalChainId) => (ulong)ReadInteger(snapshot, CreateStorageKey(PrefixOutboundNonce, U32Le(externalChainId)));

    [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
    public bool IsInboundConsumed(IReadOnlyStore snapshot, uint externalChainId, ulong nonce) => snapshot.Contains(Key(PrefixConsumedInboundNonce, externalChainId, nonce));
}

[ContractEvent(0, name: "AccountConfigured", "account", ContractParameterType.Hash160, "validator", ContractParameterType.Hash160, "paymaster", ContractParameterType.Hash160)]
[ContractEvent(1, name: "NonceConsumed", "account", ContractParameterType.Hash160, "nonce", ContractParameterType.Integer)]
[ContractEvent(2, name: "TxExecuted", "account", ContractParameterType.Hash160, "nonce", ContractParameterType.Integer, "target", ContractParameterType.Hash160, "method", ContractParameterType.String)]
public sealed class L2AccountAbstraction : L2NativeContract
{
    public const uint ValidationMagic = 0x4e344141;
    private const byte PrefixValidator = 0x01;
    private const byte PrefixPaymaster = 0x02;
    private const byte PrefixNonce = 0x03;
    private const byte KeySystemAccount = 0xfe;
    private const byte KeyOwner = 0xff;

    internal L2AccountAbstraction() : base(-108) { }

    [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
    private void Configure(ApplicationEngine engine, UInt160 owner, UInt160 systemAccount)
    {
        AssertOwnerOrCommittee(engine, KeyOwner);
        RequireNonZero(owner, nameof(owner));
        RequireNonZero(systemAccount, nameof(systemAccount));
        WriteUInt160(engine.SnapshotCache, KeyOwner, owner);
        WriteUInt160(engine.SnapshotCache, KeySystemAccount, systemAccount);
    }

    [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
    private void ConfigureAccount(ApplicationEngine engine, UInt160 account, UInt160 validator, UInt160 paymaster)
    {
        RequireNonZero(account, nameof(account));
        var owner = ReadUInt160(engine.SnapshotCache, KeyOwner);
        if (!engine.CheckWitnessInternal(account) && (owner == UInt160.Zero || !engine.CheckWitnessInternal(owner)))
            throw new InvalidOperationException("not authorized");
        SetOrDelete(engine.SnapshotCache, Key(PrefixValidator, account), validator);
        SetOrDelete(engine.SnapshotCache, Key(PrefixPaymaster, account), paymaster);
        Notify(engine, "AccountConfigured", account, validator, paymaster);
    }

    [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
    public UInt160 GetValidator(IReadOnlyStore snapshot, UInt160 account) => ReadUInt160(snapshot, Key(PrefixValidator, account));

    [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
    public UInt160 GetPaymaster(IReadOnlyStore snapshot, UInt160 account) => ReadUInt160(snapshot, Key(PrefixPaymaster, account));

    [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
    public ulong GetNonce(IReadOnlyStore snapshot, UInt160 account) => (ulong)ReadInteger(snapshot, Key(PrefixNonce, account));

    [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
    private async ContractTask<bool> ValidateTx(ApplicationEngine engine, UInt160 account, ulong nonce, UInt256 txHash, byte[] signature)
    {
        if (account == UInt160.Zero || nonce != GetNonce(engine.SnapshotCache, account) + 1) return false;
        var validator = GetValidator(engine.SnapshotCache, account);
        if (validator == UInt160.Zero) return engine.CheckWitnessInternal(account);
        return await engine.CallFromNativeContractAsync<bool>(Hash, validator, "validateTx", account, nonce, txHash, signature);
    }

    [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
    private async ContractTask<uint> ValidateTransaction(ApplicationEngine engine, UInt160 account, ulong nonce, UInt256 txHash, byte[] signature)
    {
        return await ValidateTx(engine, account, nonce, txHash, signature) ? ValidationMagic : 0u;
    }

    [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
    private void ConsumeNonce(ApplicationEngine engine, UInt160 account, ulong nonce)
    {
        AssertSystem(engine, KeySystemAccount);
        ConsumeNonceInternal(engine, account, nonce);
    }

    [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
    private async ContractTask<StackItem> ExecuteTx(ApplicationEngine engine, UInt160 account, ulong nonce, UInt256 txHash, UInt160 target, string method, StackItem[] args, UInt160 feeAsset, BigInteger feeAmount, byte[] signature)
    {
        if (!await ValidateTx(engine, account, nonce, txHash, signature)) throw new InvalidOperationException("AA validation failed");
        RequireNonZero(target, nameof(target));
        if (string.IsNullOrWhiteSpace(method)) throw new ArgumentException("method required", nameof(method));
        ConsumeNonceInternal(engine, account, nonce);
        await ChargePaymaster(engine, account, feeAsset, feeAmount);
        var result = await engine.CallFromNativeContractAsync<StackItem>(Hash, target, method, args);
        Notify(engine, "TxExecuted", account, nonce, target, method);
        return result;
    }

    private async ContractTask ChargePaymaster(ApplicationEngine engine, UInt160 account, UInt160 feeAsset, BigInteger feeAmount)
    {
        if (feeAmount == 0) return;
        RequirePositive(feeAmount, nameof(feeAmount));
        RequireNonZero(feeAsset, nameof(feeAsset));
        var paymaster = GetPaymaster(engine.SnapshotCache, account);
        RequireNonZero(paymaster, nameof(paymaster));
        await engine.CallFromNativeContractAsync(Hash, paymaster, "charge", account, feeAsset, feeAmount);
    }

    private void ConsumeNonceInternal(ApplicationEngine engine, UInt160 account, ulong nonce)
    {
        RequireNonZero(account, nameof(account));
        if (nonce != GetNonce(engine.SnapshotCache, account) + 1) throw new InvalidOperationException("nonce out of sequence");
        engine.SnapshotCache.GetAndChange(Key(PrefixNonce, account), () => new StorageItem(BigInteger.Zero)).Set(nonce);
        Notify(engine, "NonceConsumed", account, nonce);
    }

    private static void SetOrDelete(DataCache snapshot, StorageKey key, UInt160 value)
    {
        if (value == UInt160.Zero)
            snapshot.Delete(key);
        else
            snapshot.GetAndChange(key, () => new StorageItem(value.ToArray())).Value = value.ToArray();
    }
}

[ContractEvent(0, name: "BridgedTokenCreated", "l1Asset", ContractParameterType.Hash160, "l2Asset", ContractParameterType.Hash160)]
public sealed class BridgedNep17Contract : L2NativeContract
{
    public const string PlatformNeoName = "NEO";
    public const string PlatformNeoSymbol = "NEO";
    public const byte PlatformNeoDecimals = 8;
    private static readonly BigInteger PlatformNeoMaxSupply = Governance.NeoTokenTotalAmount * BigInteger.Pow(10, PlatformNeoDecimals - Governance.NeoTokenDecimals);
    public const string PlatformUsdtName = "USDT";
    public const string PlatformUsdtSymbol = "USDT";
    public const byte PlatformUsdtDecimals = 6;
    public const string PlatformUsdcName = "USDC";
    public const string PlatformUsdcSymbol = "USDC";
    public const byte PlatformUsdcDecimals = 6;
    public const string PlatformBtcName = "BTC";
    public const string PlatformBtcSymbol = "BTC";
    public const byte PlatformBtcDecimals = 8;
    private static readonly BigInteger PlatformUnlimitedMaxSupply = BigInteger.MinusOne;
    private static readonly BigInteger PlatformBtcMaxSupply = new BigInteger(21_000_000) * BigInteger.Pow(10, PlatformBtcDecimals);
    private const byte PrefixL1ToL2 = 0x01;
    private const byte PrefixL2ToL1 = 0x02;
    private const byte PrefixAuthorizedBridge = 0x03;
    private const byte KeyBridge = 0xfe;
    private const byte KeyOwner = 0xff;

    internal BridgedNep17Contract() : base(-109) { }

    public UInt160 L2NeoTokenId => field ??= TokenManagement.GetAssetId(Hash, PlatformNeoName);
    public UInt160 L2UsdtTokenId => field ??= TokenManagement.GetAssetId(Hash, PlatformUsdtName);
    public UInt160 L2UsdcTokenId => field ??= TokenManagement.GetAssetId(Hash, PlatformUsdcName);
    public UInt160 L2BtcTokenId => field ??= TokenManagement.GetAssetId(Hash, PlatformBtcName);

    internal override ContractTask InitializeAsync(ApplicationEngine engine, Hardfork? hardfork)
    {
        if (hardfork == ActiveIn)
        {
            EnsurePlatformToken(engine, L2NeoTokenId, PlatformNeoName, PlatformNeoSymbol, PlatformNeoDecimals, PlatformNeoMaxSupply);
            EnsurePlatformToken(engine, L2UsdtTokenId, PlatformUsdtName, PlatformUsdtSymbol, PlatformUsdtDecimals, PlatformUnlimitedMaxSupply);
            EnsurePlatformToken(engine, L2UsdcTokenId, PlatformUsdcName, PlatformUsdcSymbol, PlatformUsdcDecimals, PlatformUnlimitedMaxSupply);
            EnsurePlatformToken(engine, L2BtcTokenId, PlatformBtcName, PlatformBtcSymbol, PlatformBtcDecimals, PlatformBtcMaxSupply);
        }
        return ContractTask.CompletedTask;
    }

    protected override void OnManifestCompose(IsHardforkEnabledDelegate hfChecker, uint blockHeight, ContractManifest manifest)
    {
        manifest.SupportedStandards = ["NEP-17"];
    }

    [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
    private void Configure(ApplicationEngine engine, UInt160 owner, UInt160 bridge)
    {
        AssertOwnerOrCommittee(engine, KeyOwner);
        RequireNonZero(owner, nameof(owner));
        RequireNonZero(bridge, nameof(bridge));
        WriteUInt160(engine.SnapshotCache, KeyOwner, owner);
        SetBridgeInternal(engine.SnapshotCache, bridge);
    }

    [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
    public UInt160 GetOwner(IReadOnlyStore snapshot) => ReadUInt160(snapshot, KeyOwner);

    [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
    public UInt160 GetBridge(IReadOnlyStore snapshot) => ReadUInt160(snapshot, KeyBridge);

    [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
    private void SetBridge(ApplicationEngine engine, UInt160 bridge)
    {
        AssertOwnerOrCommittee(engine, KeyOwner);
        RequireNonZero(bridge, nameof(bridge));
        SetBridgeInternal(engine.SnapshotCache, bridge);
    }

    [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
    private void AuthorizeBridge(ApplicationEngine engine, UInt160 bridge, bool allowed)
    {
        AssertOwnerOrCommittee(engine, KeyOwner);
        RequireNonZero(bridge, nameof(bridge));
        var key = Key(PrefixAuthorizedBridge, bridge);
        if (allowed)
            engine.SnapshotCache.GetAndChange(key, () => new StorageItem(new byte[] { 1 })).Value = new byte[] { 1 };
        else
            engine.SnapshotCache.Delete(key);
    }

    [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
    public bool IsAuthorizedBridge(IReadOnlyStore snapshot, UInt160 bridge)
    {
        return bridge != UInt160.Zero && snapshot.Contains(Key(PrefixAuthorizedBridge, bridge));
    }

    [ContractMethod(CpuFee = 1 << 17, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
    private async ContractTask<UInt160> CreateBridgedToken(ApplicationEngine engine, string name, string symbol, byte decimals, UInt160 l1Asset, BigInteger maxSupply)
    {
        AssertOwnerOrCommittee(engine, KeyOwner);
        RequireNonZero(l1Asset, nameof(l1Asset));
        var existing = GetL2Asset(engine.SnapshotCache, l1Asset);
        if (existing != UInt160.Zero) throw new InvalidOperationException("L1 asset already mapped");
        var assetId = TokenManagement.GetAssetId(Hash, name);
        var token = TokenManagement.GetTokenInfo(engine.SnapshotCache, assetId);
        if (token is null)
        {
            assetId = await engine.CallFromNativeContractAsync<UInt160>(Hash, TokenManagement.Hash, "create", name, symbol, decimals, maxSupply);
        }
        else
        {
            if (token.Type != TokenType.Fungible || token.Owner != Hash || token.Name != name || token.Symbol != symbol ||
                token.Decimals != decimals || token.MaxSupply != maxSupply)
                throw new InvalidOperationException("existing bridged token metadata mismatch");
            var mappedL1 = GetL1Asset(engine.SnapshotCache, assetId);
            if (mappedL1 != UInt160.Zero && mappedL1 != l1Asset) throw new InvalidOperationException("L2 asset already mapped");
        }
        engine.SnapshotCache.Add(Key(PrefixL1ToL2, l1Asset), new StorageItem(assetId.ToArray()));
        engine.SnapshotCache.Add(Key(PrefixL2ToL1, assetId), new StorageItem(l1Asset.ToArray()));
        Notify(engine, "BridgedTokenCreated", l1Asset, assetId);
        return assetId;
    }

    [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
    public UInt160 GetL2Asset(IReadOnlyStore snapshot, UInt160 l1Asset) => ReadUInt160(snapshot, Key(PrefixL1ToL2, l1Asset));

    [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
    public UInt160 GetL1Asset(IReadOnlyStore snapshot, UInt160 l2Asset) => ReadUInt160(snapshot, Key(PrefixL2ToL1, l2Asset));

    private void EnsurePlatformToken(ApplicationEngine engine, UInt160 assetId, string name, string symbol, byte decimals, BigInteger maxSupply)
    {
        var token = NativeContract.TokenManagement.GetTokenInfo(engine.SnapshotCache, assetId);
        if (token is null)
        {
            NativeContract.TokenManagement.CreateInternal(engine, Hash, name, symbol, decimals, maxSupply);
            return;
        }
        if (token.Type != TokenType.Fungible || token.Owner != Hash || token.Name != name || token.Symbol != symbol ||
            token.Decimals != decimals || token.MaxSupply != maxSupply)
            throw new InvalidOperationException($"{symbol} platform token metadata mismatch");
    }

    [ContractMethod(CpuFee = 1 << 17, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
    private async ContractTask Mint(ApplicationEngine engine, UInt160 assetId, UInt160 to, BigInteger amount)
    {
        AssertBridge(engine);
        await engine.CallFromNativeContractAsync(Hash, TokenManagement.Hash, "mint", assetId, to, amount);
    }

    [ContractMethod(CpuFee = 1 << 17, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
    private async ContractTask Burn(ApplicationEngine engine, UInt160 assetId, UInt160 account, BigInteger amount)
    {
        AssertBridge(engine);
        await engine.CallFromNativeContractAsync(Hash, TokenManagement.Hash, "burn", assetId, account, amount);
    }

    [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
    public BigInteger BalanceOf(IReadOnlyStore snapshot, UInt160 assetId, UInt160 account) => TokenManagement.BalanceOf(snapshot, assetId, account);

    [ContractMethod(CpuFee = 1 << 17, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
    private async ContractTask<bool> Transfer(ApplicationEngine engine, UInt160 assetId, UInt160 from, UInt160 to, BigInteger amount, StackItem data)
    {
        return await engine.CallFromNativeContractAsync<bool>(Hash, TokenManagement.Hash, "transfer", assetId, from, to, amount, data);
    }

    [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
    private bool Verify(ApplicationEngine engine)
    {
        var owner = GetOwner(engine.SnapshotCache);
        return owner != UInt160.Zero && engine.CheckWitnessInternal(owner);
    }

    private void AssertBridge(ApplicationEngine engine)
    {
        var bridge = GetBridge(engine.SnapshotCache);
        if (bridge == UInt160.Zero) throw new InvalidOperationException("bridge unset");
        var caller = CallingScriptHash(engine);
        if (caller != bridge && !IsAuthorizedBridge(engine.SnapshotCache, caller) && !engine.CheckWitnessInternal(bridge)) throw new InvalidOperationException("not bridge");
    }

    private void SetBridgeInternal(DataCache snapshot, UInt160 bridge)
    {
        WriteUInt160(snapshot, KeyBridge, bridge);
        snapshot.GetAndChange(Key(PrefixAuthorizedBridge, bridge), () => new StorageItem(new byte[] { 1 })).Value = new byte[] { 1 };
    }
}

[ContractEvent(0, name: "GlobalRootMirrored", "epoch", ContractParameterType.Integer, "root", ContractParameterType.Hash256, "l1FinalizedHeight", ContractParameterType.Integer)]
[ContractEvent(1, name: "MessageConsumed", "epoch", ContractParameterType.Integer, "leafHash", ContractParameterType.Hash256)]
public sealed class L2InteropVerifier : L2NativeContract
{
    public const int MaxProofDepth = 64;
    private const byte PrefixGlobalRoot = 0x01;
    private const byte PrefixConsumed = 0x02;
    private const byte PrefixRootHeight = 0x03;
    private const byte KeyBatchInfo = 0xfd;
    private const byte KeySystemAccount = 0xfe;
    private const byte KeyOwner = 0xff;

    internal L2InteropVerifier() : base(-110) { }

    [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States)]
    private void Configure(ApplicationEngine engine, UInt160 owner, UInt160 systemAccount, UInt160 batchInfo)
    {
        AssertOwnerOrCommittee(engine, KeyOwner);
        RequireNonZero(owner, nameof(owner));
        RequireNonZero(systemAccount, nameof(systemAccount));
        RequireNonZero(batchInfo, nameof(batchInfo));
        WriteUInt160(engine.SnapshotCache, KeyOwner, owner);
        WriteUInt160(engine.SnapshotCache, KeySystemAccount, systemAccount);
        WriteUInt160(engine.SnapshotCache, KeyBatchInfo, batchInfo);
    }

    [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
    public UInt160 GetOwner(IReadOnlyStore snapshot) => ReadUInt160(snapshot, KeyOwner);

    [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
    public UInt160 GetSystemAccount(IReadOnlyStore snapshot) => ReadUInt160(snapshot, KeySystemAccount);

    [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
    public UInt160 GetBatchInfo(IReadOnlyStore snapshot) => ReadUInt160(snapshot, KeyBatchInfo);

    [ContractMethod(CpuFee = 1 << 15, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
    private async ContractTask PublishGlobalRoot(ApplicationEngine engine, ulong epoch, UInt256 root, uint l1FinalizedHeight)
    {
        AssertSystem(engine, KeySystemAccount);
        if (root == UInt256.Zero) throw new ArgumentException("root must be non-zero.", nameof(root));
        var batchInfo = GetBatchInfo(engine.SnapshotCache);
        uint currentL1Height = batchInfo == NativeContract.L2BatchInfo.Hash
            ? NativeContract.L2BatchInfo.GetL1FinalizedHeight(engine.SnapshotCache)
            : await engine.CallFromNativeContractAsync<uint>(Hash, batchInfo, "getL1FinalizedHeight");
        if (l1FinalizedHeight > currentL1Height) throw new InvalidOperationException("root height not finalized on this L2 yet");
        var key = EpochKey(PrefixGlobalRoot, epoch);
        if (engine.SnapshotCache.Contains(key)) throw new InvalidOperationException("global root already mirrored");
        engine.SnapshotCache.Add(key, new StorageItem(root.ToArray()));
        WriteInteger(engine.SnapshotCache, EpochKey(PrefixRootHeight, epoch), l1FinalizedHeight);
        Notify(engine, "GlobalRootMirrored", epoch, root, l1FinalizedHeight);
    }

    [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
    public UInt256 GetGlobalRoot(IReadOnlyStore snapshot, ulong epoch)
    {
        return snapshot.TryGet(EpochKey(PrefixGlobalRoot, epoch), out var item) ? new UInt256(item.Value.Span) : UInt256.Zero;
    }

    [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
    public uint GetRootL1Height(IReadOnlyStore snapshot, ulong epoch) => (uint)ReadInteger(snapshot, EpochKey(PrefixRootHeight, epoch));

    [ContractMethod(CpuFee = 1 << 16, RequiredCallFlags = CallFlags.ReadStates)]
    public bool VerifyMessage(IReadOnlyStore snapshot, ulong epoch, UInt256 leafHash, byte[][] siblings, ulong leafIndex)
    {
        var root = GetGlobalRoot(snapshot, epoch);
        return root != UInt256.Zero && root == FoldMerkle(leafHash, siblings, leafIndex);
    }

    [ContractMethod(CpuFee = 1 << 16, StorageFee = 1 << 7, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
    private void ConsumeMessage(ApplicationEngine engine, ulong epoch, UInt256 leafHash, byte[][] siblings, ulong leafIndex)
    {
        if (!VerifyMessage(engine.SnapshotCache, epoch, leafHash, siblings, leafIndex)) throw new InvalidOperationException("message proof rejected");
        var key = ConsumedKey(epoch, leafHash);
        if (engine.SnapshotCache.Contains(key)) throw new InvalidOperationException("message already consumed");
        engine.SnapshotCache.Add(key, new StorageItem(new byte[] { 1 }));
        Notify(engine, "MessageConsumed", epoch, leafHash);
    }

    [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
    public bool IsConsumed(IReadOnlyStore snapshot, ulong epoch, UInt256 leafHash) => snapshot.Contains(ConsumedKey(epoch, leafHash));

    private StorageKey EpochKey(byte prefix, ulong epoch) => CreateStorageKey(prefix, U64Le(epoch));

    private StorageKey ConsumedKey(ulong epoch, UInt256 leafHash)
    {
        var data = new byte[8 + UInt256.Length];
        U64Le(epoch).CopyTo(data, 0);
        leafHash.ToArray().CopyTo(data, 8);
        return CreateStorageKey(PrefixConsumed, data);
    }

    private static UInt256 FoldMerkle(UInt256 leafHash, byte[][] siblings, ulong leafIndex)
    {
        if (siblings.Length > MaxProofDepth) throw new ArgumentOutOfRangeException(nameof(siblings), "proof too deep");
        var current = leafHash.ToArray();
        var index = leafIndex;
        foreach (var sibling in siblings)
        {
            if (sibling.Length != UInt256.Length) throw new ArgumentException("sibling must be 32 bytes", nameof(siblings));
            var combined = new byte[64];
            if ((index & 1) == 0)
            {
                current.CopyTo(combined, 0);
                sibling.CopyTo(combined, 32);
            }
            else
            {
                sibling.CopyTo(combined, 0);
                current.CopyTo(combined, 32);
            }
            current = Crypto.Hash256(combined);
            index >>= 1;
        }
        return new UInt256(current);
    }
}
