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

}
