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

}
