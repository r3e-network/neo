// Copyright (C) 2015-2026 The Neo Project.
//
// NeoHub native-contract tests are maintained by r3e-network in the
// r3e/neo-n3-core branch.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.Cryptography;
using Neo.Cryptography.ECC;
using Neo.Extensions;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.SmartContract;
using Neo.SmartContract.Manifest;
using Neo.SmartContract.Native;
using Neo.UnitTests.Extensions;
using Neo.VM;
using Neo.VM.Types;
using Neo.Wallets;
using System;
using System.Linq;
using System.Numerics;

namespace Neo.UnitTests.SmartContract.Native
{
    [TestClass]
    public class UT_NeoHubNativeContracts
    {
        private DataCache _snapshotCache = null!;

        private static readonly NativeContract[] ExpectedContracts =
        [
            NativeContract.NeoHubChainRegistry,
            NativeContract.NeoHubTokenRegistry,
            NativeContract.NeoHubDARegistry,
            NativeContract.NeoHubL1TxFilter,
            NativeContract.NeoHubVerifierRegistry,
            NativeContract.NeoHubMessageRouter,
            NativeContract.NeoHubSettlementManager,
            NativeContract.NeoHubDAValidator,
            NativeContract.NeoHubSharedBridge,
            NativeContract.NeoHubEmergencyManager,
            NativeContract.NeoHubGovernanceController,
            NativeContract.NeoHubSequencerBond,
            NativeContract.NeoHubSequencerRegistry,
            NativeContract.NeoHubForcedInclusion,
            NativeContract.NeoHubOptimisticChallenge,
            NativeContract.NeoHubGovernanceFraudVerifier,
            NativeContract.NeoHubRestrictedExecutionFraudVerifier
        ];

        [TestInitialize]
        public void TestSetup()
        {
            _snapshotCache = TestBlockchain.GetTestSnapshotCache();
        }

        [TestMethod]
        public void NeoHubL1Contracts_AreRegisteredAsNativeContracts()
        {
            foreach (var contract in ExpectedContracts)
            {
                Assert.IsTrue(NativeContract.Contracts.Contains(contract));
                Assert.IsTrue(NativeContract.IsNative(contract.Hash));
                Assert.IsTrue(contract.Id <= -101, $"{contract.Name} must use the r3e NeoHub native id range.");

                var methods = contract
                    .GetContractState(TestProtocolSettings.Default, 0)
                    .Manifest
                    .Abi
                    .Methods
                    .Select(m => m.Name)
                    .ToArray();

                CollectionAssert.DoesNotContain(methods, "_deploy");
                CollectionAssert.DoesNotContain(methods, "deploy");
                CollectionAssert.DoesNotContain(methods, "update");
            }
        }

        [TestMethod]
        public void ChainRegistry_ConfiguresOwnerAndStoresChainConfig()
        {
            var snapshot = _snapshotCache.CloneCache();
            var block = Block();
            var committee = NativeContract.NEO.GetCommitteeAddress(snapshot);
            var owner = H(0x11);
            var chainId = 1001u;
            var config = ChainConfig(chainId, securityLevel: 3, daMode: 1, gatewayEnabled: true,
                permissionlessExit: true, sequencerModel: 1, exitModel: 0, active: true);

            NativeContract.NeoHubChainRegistry.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), block,
                "configure", Hash160(owner));
            NativeContract.NeoHubChainRegistry.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "registerChain", Integer(chainId), Bytes(config));

            Assert.AreEqual(owner, AsUInt160(NativeContract.NeoHubChainRegistry.Call(snapshot, "getOwner")));
            CollectionAssert.AreEqual(config, NativeContract.NeoHubChainRegistry.Call(snapshot,
                "getChainConfig", Integer(chainId)).GetSpan().ToArray());
            Assert.IsTrue(NativeContract.NeoHubChainRegistry.Call(snapshot, "isActive", Integer(chainId)).GetBoolean());
            Assert.AreEqual(3, NativeContract.NeoHubChainRegistry.Call(snapshot, "getSecurityLevel", Integer(chainId)).GetInteger());
            Assert.AreEqual(1, NativeContract.NeoHubChainRegistry.Call(snapshot, "getDAMode", Integer(chainId)).GetInteger());
            Assert.IsTrue(NativeContract.NeoHubChainRegistry.Call(snapshot, "getGatewayEnabled", Integer(chainId)).GetBoolean());

            NativeContract.NeoHubChainRegistry.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "pauseChain", Integer(chainId));
            Assert.IsFalse(NativeContract.NeoHubChainRegistry.Call(snapshot, "isActive", Integer(chainId)).GetBoolean());

            NativeContract.NeoHubChainRegistry.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "resumeChain", Integer(chainId));
            Assert.IsTrue(NativeContract.NeoHubChainRegistry.Call(snapshot, "isActive", Integer(chainId)).GetBoolean());
        }

        [TestMethod]
        public void TokenRegistry_ConfiguresOwnerAndStoresMappings()
        {
            var snapshot = _snapshotCache.CloneCache();
            var block = Block();
            var committee = NativeContract.NEO.GetCommitteeAddress(snapshot);
            var owner = H(0x21);
            var l1Asset = H(0x22);
            var l2Asset = H(0x23);
            var chainId = 1002u;
            var mapping = AssetMapping(l1Asset, chainId, l2Asset, active: true);

            NativeContract.NeoHubTokenRegistry.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), block,
                "configure", Hash160(owner));
            NativeContract.NeoHubTokenRegistry.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "registerMapping", Bytes(mapping));

            Assert.AreEqual(owner, AsUInt160(NativeContract.NeoHubTokenRegistry.Call(snapshot, "getOwner")));
            CollectionAssert.AreEqual(mapping, NativeContract.NeoHubTokenRegistry.Call(snapshot,
                "getMapping", Hash160(l1Asset), Integer(chainId)).GetSpan().ToArray());
            Assert.AreEqual(l2Asset, AsUInt160(NativeContract.NeoHubTokenRegistry.Call(snapshot,
                "getL2Asset", Hash160(l1Asset), Integer(chainId))));
            Assert.IsTrue(NativeContract.NeoHubTokenRegistry.Call(snapshot,
                "isActive", Hash160(l1Asset), Integer(chainId)).GetBoolean());
        }

        [TestMethod]
        public void DARegistry_RecordsCommitmentOnlyForSettlementManager()
        {
            var snapshot = _snapshotCache.CloneCache();
            var block = Block();
            var committee = NativeContract.NEO.GetCommitteeAddress(snapshot);
            var owner = H(0x31);
            var settlementManager = H(0x32);
            var commitment = H256(0x33);

            NativeContract.NeoHubDARegistry.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), block,
                "configure", Hash160(owner), Hash160(settlementManager));

            Assert.ThrowsExactly<InvalidOperationException>(() =>
                NativeContract.NeoHubDARegistry.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                    "record", Integer(1003), Integer(7), Hash256(commitment), Integer(1)));

            NativeContract.NeoHubDARegistry.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(settlementManager), block,
                "record", Integer(1003), Integer(7), Hash256(commitment), Integer(1));

            Assert.AreEqual(commitment, AsUInt256(NativeContract.NeoHubDARegistry.Call(snapshot,
                "getCommitment", Integer(1003), Integer(7))));
            Assert.AreEqual(1, NativeContract.NeoHubDARegistry.Call(snapshot,
                "getMode", Integer(1003), Integer(7)).GetInteger());
        }

        [TestMethod]
        public void DAValidator_ConfiguresAndValidatesNonDacModes()
        {
            var snapshot = _snapshotCache.CloneCache();
            var block = Block();
            var committee = NativeContract.NEO.GetCommitteeAddress(snapshot);
            var owner = H(0x3a);
            var daRegistry = H(0x3b);
            var commitment = H256(0x3c);

            NativeContract.NeoHubDAValidator.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), block,
                "configure", Hash160(owner), Hash160(daRegistry));

            Assert.AreEqual(owner, AsUInt160(NativeContract.NeoHubDAValidator.Call(snapshot, "getOwner")));
            Assert.AreEqual(daRegistry, AsUInt160(NativeContract.NeoHubDAValidator.Call(snapshot, "getDARegistry")));

            Assert.IsTrue(NativeContract.NeoHubDAValidator.Call(snapshot,
                "validate", Integer(1009), Integer(1), Hash256(commitment), Integer(0)).GetBoolean());
            Assert.IsTrue(NativeContract.NeoHubDAValidator.Call(snapshot,
                "validate", Integer(1009), Integer(1), Hash256(commitment), Integer(1)).GetBoolean());
            Assert.IsTrue(NativeContract.NeoHubDAValidator.Call(snapshot,
                "validate", Integer(1009), Integer(1), Hash256(commitment), Integer(2)).GetBoolean());
            Assert.IsFalse(NativeContract.NeoHubDAValidator.Call(snapshot,
                "validate", Integer(1009), Integer(1), Hash256(commitment), Integer(3)).GetBoolean());
            Assert.IsFalse(NativeContract.NeoHubDAValidator.Call(snapshot,
                "validate", Integer(0), Integer(1), Hash256(commitment), Integer(0)).GetBoolean());
            Assert.IsFalse(NativeContract.NeoHubDAValidator.Call(snapshot,
                "validate", Integer(1009), Integer(1), Hash256(UInt256.Zero), Integer(0)).GetBoolean());
            Assert.IsFalse(NativeContract.NeoHubDAValidator.Call(snapshot,
                "validate", Integer(1009), Integer(1), Hash256(commitment), Integer(4)).GetBoolean());
        }

        [TestMethod]
        public void DAValidator_RegistersCommitteeAndStoresDacAttestations()
        {
            var snapshot = _snapshotCache.CloneCache();
            var block = Block();
            var committee = NativeContract.NEO.GetCommitteeAddress(snapshot);
            var owner = H(0x3d);
            var daRegistry = H(0x3e);
            var chainId = 1010u;
            var batchNumber = 2UL;
            var commitment = H256(0x3f);
            var key1 = Key(0x01);
            var key2 = Key(0x02);
            var committeeBlob = key1.PublicKey.EncodePoint(true)
                .Concat(key2.PublicKey.EncodePoint(true))
                .ToArray();
            var message = DAAttestationMessage(chainId, batchNumber, commitment, 3);
            var proof = DAAttestationProof(message, key1, key2);

            NativeContract.NeoHubDAValidator.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), block,
                "configure", Hash160(owner), Hash160(daRegistry));

            Assert.ThrowsExactly<InvalidOperationException>(() =>
                NativeContract.NeoHubDAValidator.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(), block,
                    "registerCommittee", Integer(chainId), Integer(2), Bytes(committeeBlob)));

            NativeContract.NeoHubDAValidator.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "registerCommittee", Integer(chainId), Integer(2), Bytes(committeeBlob));

            CollectionAssert.AreEqual(new byte[] { 2, 2 }.Concat(committeeBlob).ToArray(),
                NativeContract.NeoHubDAValidator.Call(snapshot, "getCommittee", Integer(chainId)).GetSpan().ToArray());
            Assert.IsTrue(NativeContract.NeoHubDAValidator.Call(snapshot,
                "verifyAttestation", Integer(chainId), Integer(batchNumber), Hash256(commitment), Integer(3), Bytes(proof)).GetBoolean());
            Assert.IsTrue(NativeContract.NeoHubDAValidator.Call(snapshot,
                "submitAttestation", Integer(chainId), Integer(batchNumber), Hash256(commitment), Integer(3), Bytes(proof)).GetBoolean());
            Assert.IsTrue(NativeContract.NeoHubDAValidator.Call(snapshot,
                "isValidated", Integer(chainId), Integer(batchNumber), Hash256(commitment), Integer(3)).GetBoolean());
            Assert.IsTrue(NativeContract.NeoHubDAValidator.Call(snapshot,
                "validate", Integer(chainId), Integer(batchNumber), Hash256(commitment), Integer(3)).GetBoolean());

            var duplicateSignerProof = DAAttestationProof(message, ((byte)0, key1), ((byte)0, key1));
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                NativeContract.NeoHubDAValidator.Call(snapshot,
                    "verifyAttestation", Integer(chainId), Integer(batchNumber), Hash256(commitment), Integer(3), Bytes(duplicateSignerProof)));
        }

        [TestMethod]
        public void L1TxFilter_ConfiguresDefaultAndRules()
        {
            var snapshot = _snapshotCache.CloneCache();
            var block = Block();
            var committee = NativeContract.NEO.GetCommitteeAddress(snapshot);
            var owner = H(0x41);
            var sender = H(0x42);
            var receiver = H(0x43);

            NativeContract.NeoHubL1TxFilter.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), block,
                "configure", Hash160(owner));

            Assert.IsTrue(NativeContract.NeoHubL1TxFilter.Call(snapshot,
                "acceptL1ToL2", Integer(1004), Hash160(sender), Hash160(receiver), Integer(9), Bytes([1, 2, 3])).GetBoolean());

            NativeContract.NeoHubL1TxFilter.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "setAllowedSender", Hash160(sender), Boolean(false));

            Assert.IsFalse(NativeContract.NeoHubL1TxFilter.Call(snapshot,
                "acceptL1ToL2", Integer(1004), Hash160(sender), Hash160(receiver), Integer(9), Bytes([1, 2, 3])).GetBoolean());
        }

        [TestMethod]
        public void VerifierRegistry_ConfiguresOwnerAndStoresVerifier()
        {
            var snapshot = _snapshotCache.CloneCache();
            var block = Block();
            var committee = NativeContract.NEO.GetCommitteeAddress(snapshot);
            var owner = H(0x61);
            var verifier = H(0x62);

            NativeContract.NeoHubVerifierRegistry.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), block,
                "configure", Hash160(owner));

            Assert.ThrowsExactly<InvalidOperationException>(() =>
                NativeContract.NeoHubVerifierRegistry.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(), block,
                    "registerVerifier", Integer(1), Hash160(verifier)));

            NativeContract.NeoHubVerifierRegistry.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "registerVerifier", Integer(1), Hash160(verifier));

            Assert.AreEqual(owner, AsUInt160(NativeContract.NeoHubVerifierRegistry.Call(snapshot, "getOwner")));
            Assert.AreEqual(verifier, AsUInt160(NativeContract.NeoHubVerifierRegistry.Call(snapshot,
                "getVerifier", Integer(1))));
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                NativeContract.NeoHubVerifierRegistry.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                    "registerVerifier", Integer(0), Hash160(verifier)));
        }

        [TestMethod]
        public void MessageRouter_EnqueuesMessagesAndAppliesL1TxFilter()
        {
            var snapshot = _snapshotCache.CloneCache();
            var block = Block();
            var committee = NativeContract.NEO.GetCommitteeAddress(snapshot);
            var owner = H(0x71);
            var settlementManager = H(0x72);
            var sender = H(0x73);
            var receiver = H(0x74);

            NativeContract.NeoHubL1TxFilter.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), block,
                "configure", Hash160(owner));
            NativeContract.NeoHubL1TxFilter.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "setAllowedSender", Hash160(sender), Boolean(false));
            NativeContract.NeoHubMessageRouter.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), block,
                "configure", Hash160(owner), Hash160(settlementManager));
            NativeContract.NeoHubMessageRouter.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "setL1TxFilter", Integer(1005), Hash160(NativeContract.NeoHubL1TxFilter.Hash));

            Assert.ThrowsExactly<InvalidOperationException>(() =>
                CallAsScript(NativeContract.NeoHubMessageRouter, snapshot, sender, block,
                    "enqueueL1ToL2", Integer(1005), Hash160(receiver), Integer(9), Bytes([1, 2, 3])));

            NativeContract.NeoHubL1TxFilter.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "setAllowedSender", Hash160(sender), Boolean(true));
            var nonce = CallAsScript(NativeContract.NeoHubMessageRouter, snapshot, sender, block,
                "enqueueL1ToL2", Integer(1005), Hash160(receiver), Integer(9), Bytes([1, 2, 3])).GetInteger();

            Assert.AreEqual(1, nonce);
            Assert.IsTrue(NativeContract.NeoHubMessageRouter.Call(snapshot,
                "getL1ToL2", Integer(1005), Integer(1)).GetSpan().Length > 0);
            Assert.AreEqual(NativeContract.NeoHubL1TxFilter.Hash, AsUInt160(NativeContract.NeoHubMessageRouter.Call(snapshot,
                "getL1TxFilter", Integer(1005))));
        }

        [TestMethod]
        public void SettlementManager_ConfiguresWiringAndRejectsInactiveChains()
        {
            var snapshot = _snapshotCache.CloneCache();
            var block = Block();
            var committee = NativeContract.NEO.GetCommitteeAddress(snapshot);
            var owner = H(0x81);
            var daRegistry = H(0x82);
            var daValidator = H(0x83);
            var commitment = BatchCommitment(1006, 1, 1);

            NativeContract.NeoHubSettlementManager.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), block,
                "configure", Hash160(owner), Hash160(NativeContract.NeoHubChainRegistry.Hash),
                Hash160(NativeContract.NeoHubVerifierRegistry.Hash));

            Assert.ThrowsExactly<InvalidOperationException>(() =>
                NativeContract.NeoHubSettlementManager.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(), block,
                    "setDARegistry", Hash160(daRegistry)));

            NativeContract.NeoHubSettlementManager.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "setDARegistry", Hash160(daRegistry));
            NativeContract.NeoHubSettlementManager.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "setDAValidator", Hash160(daValidator));

            Assert.AreEqual(owner, AsUInt160(NativeContract.NeoHubSettlementManager.Call(snapshot, "getOwner")));
            Assert.AreEqual(daRegistry, AsUInt160(NativeContract.NeoHubSettlementManager.Call(snapshot, "getDARegistry")));
            Assert.AreEqual(daValidator, AsUInt160(NativeContract.NeoHubSettlementManager.Call(snapshot, "getDAValidator")));
            Assert.AreEqual(UInt160.Zero, AsUInt160(NativeContract.NeoHubSettlementManager.Call(snapshot, "getOptimisticChallenge")));

            Assert.ThrowsExactly<InvalidOperationException>(() =>
                NativeContract.NeoHubSettlementManager.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                    "submitBatch", Bytes(commitment)));
        }

        [TestMethod]
        public void SettlementManager_SubmitsAndFinalizesBatchesThroughRegistries()
        {
            var snapshot = _snapshotCache.CloneCache();
            var block = Block();
            var committee = NativeContract.NEO.GetCommitteeAddress(snapshot);
            var owner = H(0x88);
            var chainId = 1008u;
            var verifier = AlwaysTrueContract();
            var postStateRoot = H256(0x89);
            var withdrawalRoot = H256(0x8a);
            var daCommitment = H256(0x8b);
            var commitment = BatchCommitment(chainId, 1, 1, postStateRoot, withdrawalRoot, daCommitment);

            snapshot.AddContract(verifier.Hash, verifier);

            NativeContract.NeoHubChainRegistry.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), block,
                "configure", Hash160(owner));
            NativeContract.NeoHubChainRegistry.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "registerChain", Integer(chainId), Bytes(ChainConfig(chainId, securityLevel: 3, daMode: 1, gatewayEnabled: true,
                    permissionlessExit: true, sequencerModel: 1, exitModel: 0, active: true)));

            NativeContract.NeoHubVerifierRegistry.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), block,
                "configure", Hash160(owner));
            NativeContract.NeoHubVerifierRegistry.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "registerVerifier", Integer(1), Hash160(verifier.Hash));

            NativeContract.NeoHubDARegistry.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), block,
                "configure", Hash160(owner), Hash160(NativeContract.NeoHubSettlementManager.Hash));
            NativeContract.NeoHubDAValidator.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), block,
                "configure", Hash160(owner), Hash160(NativeContract.NeoHubDARegistry.Hash));
            NativeContract.NeoHubSettlementManager.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), block,
                "configure", Hash160(owner), Hash160(NativeContract.NeoHubChainRegistry.Hash),
                Hash160(NativeContract.NeoHubVerifierRegistry.Hash));
            NativeContract.NeoHubSettlementManager.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "setDARegistry", Hash160(NativeContract.NeoHubDARegistry.Hash));
            NativeContract.NeoHubSettlementManager.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "setDAValidator", Hash160(NativeContract.NeoHubDAValidator.Hash));

            NativeContract.NeoHubSettlementManager.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "submitBatch", Bytes(commitment));

            Assert.AreEqual(NeoHubSettlementManagerContract.StatusPending, NativeContract.NeoHubSettlementManager.Call(snapshot,
                "getBatchStatus", Integer(chainId), Integer(1)).GetInteger());
            Assert.AreEqual(daCommitment, AsUInt256(NativeContract.NeoHubDARegistry.Call(snapshot,
                "getCommitment", Integer(chainId), Integer(1))));

            NativeContract.NeoHubSettlementManager.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "finalizeBatch", Integer(chainId), Integer(1));

            Assert.AreEqual(NeoHubSettlementManagerContract.StatusFinalized, NativeContract.NeoHubSettlementManager.Call(snapshot,
                "getBatchStatus", Integer(chainId), Integer(1)).GetInteger());
            Assert.AreEqual(1, NativeContract.NeoHubSettlementManager.Call(snapshot,
                "getLatestFinalizedBatch", Integer(chainId)).GetInteger());
            Assert.AreEqual(postStateRoot, AsUInt256(NativeContract.NeoHubSettlementManager.Call(snapshot,
                "getCanonicalStateRoot", Integer(chainId))));
            Assert.IsTrue(NativeContract.NeoHubSettlementManager.Call(snapshot,
                "verifyWithdrawalLeafAt", Integer(chainId), Integer(1), Hash256(withdrawalRoot)).GetBoolean());
        }

        [TestMethod]
        public void SettlementManager_VerifiesWithdrawalProofsOnlyForFinalizedBatches()
        {
            var snapshot = _snapshotCache.CloneCache();
            const uint chainId = 1007;
            const ulong batchNumber = 1;
            var leaf = H256(0x91);
            var sibling = H256(0x92);
            var root = HashPair(leaf, sibling);
            var siblings = ArrayParam(Bytes(sibling.ToArray()));

            snapshot.Add(SettlementKey(0x01, chainId, batchNumber), new StorageItem([2]));
            snapshot.Add(SettlementKey(0x05, chainId, batchNumber), new StorageItem(root.ToArray()));

            Assert.IsFalse(NativeContract.NeoHubSettlementManager.Call(snapshot,
                "verifyWithdrawalLeafWithProof", Integer(chainId), Integer(batchNumber), Hash256(leaf), siblings, Integer(0)).GetBoolean());

            snapshot.GetAndChange(SettlementKey(0x01, chainId, batchNumber))!.Value = new byte[] { 3 };

            Assert.IsTrue(NativeContract.NeoHubSettlementManager.Call(snapshot,
                "verifyWithdrawalLeafWithProof", Integer(chainId), Integer(batchNumber), Hash256(leaf), siblings, Integer(0)).GetBoolean());
            Assert.IsFalse(NativeContract.NeoHubSettlementManager.Call(snapshot,
                "verifyWithdrawalLeafWithProof", Integer(chainId), Integer(batchNumber), Hash256(leaf), siblings, Integer(1)).GetBoolean());
        }

        [TestMethod]
        public void SharedBridge_ConfiguresAndEnqueuesDeposits()
        {
            var snapshot = _snapshotCache.CloneCache();
            var block = Block();
            var committee = NativeContract.NEO.GetCommitteeAddress(snapshot);
            var owner = H(0xa1);
            var settlementManager = H(0xa2);
            var tokenRegistry = H(0xa3);
            var sender = H(0xa4);
            var recipient = H(0xa5);
            const uint chainId = 1011;
            const long amount = 123456789;
            var token = TransferTokenContract();

            snapshot.AddContract(token.Hash, token);

            NativeContract.NeoHubSharedBridge.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), block,
                "configure", Hash160(owner), Hash160(settlementManager), Hash160(tokenRegistry));

            Assert.AreEqual(owner, AsUInt160(NativeContract.NeoHubSharedBridge.Call(snapshot, "getOwner")));
            Assert.AreEqual(settlementManager, AsUInt160(NativeContract.NeoHubSharedBridge.Call(snapshot, "getSettlementManager")));
            Assert.AreEqual(tokenRegistry, AsUInt160(NativeContract.NeoHubSharedBridge.Call(snapshot, "getTokenRegistry")));

            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                CallAsScript(NativeContract.NeoHubSharedBridge, snapshot, sender, block,
                    "deposit", Hash160(token.Hash), Integer(amount), Integer(0), Hash160(recipient)));

            var nonce = CallAsScript(NativeContract.NeoHubSharedBridge, snapshot, sender, block,
                "deposit", Hash160(token.Hash), Integer(amount), Integer(chainId), Hash160(recipient)).GetInteger();

            Assert.AreEqual(1, nonce);
            var deposit = NativeContract.NeoHubSharedBridge.Call(snapshot,
                "getDeposit", Integer(chainId), Integer(1)).GetSpan().ToArray();
            var amountBytes = new BigInteger(amount).ToByteArray();

            Assert.AreEqual(20 + 20 + 20 + 8 + 4 + amountBytes.Length, deposit.Length);
            Assert.AreEqual(token.Hash, new UInt160(deposit.AsSpan(0, UInt160.Length)));
            Assert.AreEqual(recipient, new UInt160(deposit.AsSpan(20, UInt160.Length)));
            Assert.AreEqual(sender, new UInt160(deposit.AsSpan(40, UInt160.Length)));
            Assert.AreEqual(1UL, ReadU64(deposit.AsSpan(60)));
            Assert.AreEqual((uint)amountBytes.Length, ReadU32(deposit.AsSpan(68)));
            CollectionAssert.AreEqual(amountBytes, deposit.Skip(72).ToArray());
        }

        [TestMethod]
        public void SharedBridge_FinalizesWithdrawalsThroughSettlementAndTokenRegistry()
        {
            var snapshot = _snapshotCache.CloneCache();
            var block = Block();
            var committee = NativeContract.NEO.GetCommitteeAddress(snapshot);
            var owner = H(0xb1);
            const uint chainId = 1012;
            const ulong batchNumber = 2;
            const ulong withdrawalNonce = 9;
            const long amount = 987654321;
            var token = TransferTokenContract();
            var l2Asset = H(0xb2);
            var emittingContract = H(0xb3);
            var l2Sender = H(0xb4);
            var recipient = H(0xb5);
            var caller = H(0xb6);
            var leaf = ComputeWithdrawalLeafHash(
                emittingContract, l2Sender, recipient, l2Asset, new BigInteger(amount), withdrawalNonce);

            snapshot.AddContract(token.Hash, token);
            NativeContract.NeoHubTokenRegistry.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), block,
                "configure", Hash160(owner));
            NativeContract.NeoHubTokenRegistry.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "registerMapping", Bytes(AssetMapping(token.Hash, chainId, l2Asset, active: true)));
            NativeContract.NeoHubSharedBridge.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), block,
                "configure", Hash160(owner), Hash160(NativeContract.NeoHubSettlementManager.Hash),
                Hash160(NativeContract.NeoHubTokenRegistry.Hash));
            snapshot.Add(SettlementKey(0x01, chainId, batchNumber), new StorageItem([NeoHubSettlementManagerContract.StatusFinalized]));
            snapshot.Add(SettlementKey(0x05, chainId, batchNumber), new StorageItem(leaf.ToArray()));

            CallAsScript(NativeContract.NeoHubSharedBridge, snapshot, caller, block,
                "finalizeWithdrawalAt", Integer(chainId), Integer(batchNumber), Hash256(leaf),
                Hash160(emittingContract), Hash160(l2Sender), Hash160(l2Asset), Integer(withdrawalNonce),
                Hash160(token.Hash), Hash160(recipient), Integer(amount));

            Assert.IsTrue(NativeContract.NeoHubSharedBridge.Call(snapshot,
                "isWithdrawalConsumed", Integer(chainId), Hash256(leaf)).GetBoolean());
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                CallAsScript(NativeContract.NeoHubSharedBridge, snapshot, caller, block,
                    "finalizeWithdrawalAt", Integer(chainId), Integer(batchNumber), Hash256(leaf),
                    Hash160(emittingContract), Hash160(l2Sender), Hash160(l2Asset), Integer(withdrawalNonce),
                    Hash160(token.Hash), Hash160(recipient), Integer(amount)));
        }

        [TestMethod]
        public void SharedBridge_FinalizesLatestAndMerkleProofWithdrawals()
        {
            var snapshot = _snapshotCache.CloneCache();
            var block = Block();
            var committee = NativeContract.NEO.GetCommitteeAddress(snapshot);
            var owner = H(0xc1);
            const uint latestChainId = 1013;
            const uint proofChainId = 1014;
            const ulong latestBatch = 3;
            const ulong proofBatch = 4;
            const long latestAmount = 1234;
            const long proofAmount = 5678;
            var token = TransferTokenContract();
            var l2Asset = H(0xc2);
            var emittingContract = H(0xc3);
            var l2Sender = H(0xc4);
            var latestRecipient = H(0xc5);
            var proofRecipient = H(0xc6);
            var caller = H(0xc7);
            var latestLeaf = ComputeWithdrawalLeafHash(
                emittingContract, l2Sender, latestRecipient, l2Asset, new BigInteger(latestAmount), 1);
            var proofLeaf = ComputeWithdrawalLeafHash(
                emittingContract, l2Sender, proofRecipient, l2Asset, new BigInteger(proofAmount), 2);
            var proofSibling = H256(0xc8);

            snapshot.AddContract(token.Hash, token);
            NativeContract.NeoHubTokenRegistry.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), block,
                "configure", Hash160(owner));
            NativeContract.NeoHubTokenRegistry.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "registerMapping", Bytes(AssetMapping(token.Hash, latestChainId, l2Asset, active: true)));
            NativeContract.NeoHubTokenRegistry.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "registerMapping", Bytes(AssetMapping(token.Hash, proofChainId, l2Asset, active: true)));
            NativeContract.NeoHubSharedBridge.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), block,
                "configure", Hash160(owner), Hash160(NativeContract.NeoHubSettlementManager.Hash),
                Hash160(NativeContract.NeoHubTokenRegistry.Hash));

            snapshot.Add(SettlementKey(0x01, latestChainId, latestBatch), new StorageItem([NeoHubSettlementManagerContract.StatusFinalized]));
            snapshot.Add(SettlementKey(0x05, latestChainId, latestBatch), new StorageItem(latestLeaf.ToArray()));
            snapshot.Add(SettlementKey(0x04, latestChainId), new StorageItem(new BigInteger(latestBatch)));
            snapshot.Add(SettlementKey(0x01, proofChainId, proofBatch), new StorageItem([NeoHubSettlementManagerContract.StatusFinalized]));
            snapshot.Add(SettlementKey(0x05, proofChainId, proofBatch), new StorageItem(HashPair(proofLeaf, proofSibling).ToArray()));

            CallAsScript(NativeContract.NeoHubSharedBridge, snapshot, caller, block,
                "finalizeWithdrawal", Integer(latestChainId), Hash256(latestLeaf),
                Hash160(emittingContract), Hash160(l2Sender), Hash160(l2Asset), Integer(1),
                Hash160(token.Hash), Hash160(latestRecipient), Integer(latestAmount));

            CallAsScript(NativeContract.NeoHubSharedBridge, snapshot, caller, block,
                "finalizeWithdrawalWithProof", Integer(proofChainId), Integer(proofBatch), Hash256(proofLeaf),
                ArrayParam(Bytes(proofSibling.ToArray())), Integer(0),
                Hash160(emittingContract), Hash160(l2Sender), Hash160(l2Asset), Integer(2),
                Hash160(token.Hash), Hash160(proofRecipient), Integer(proofAmount));

            Assert.IsTrue(NativeContract.NeoHubSharedBridge.Call(snapshot,
                "isWithdrawalConsumed", Integer(latestChainId), Hash256(latestLeaf)).GetBoolean());
            Assert.IsTrue(NativeContract.NeoHubSharedBridge.Call(snapshot,
                "isWithdrawalConsumed", Integer(proofChainId), Hash256(proofLeaf)).GetBoolean());
        }

        [TestMethod]
        public void EmergencyManager_ConfiguresPauseAndResume()
        {
            var snapshot = _snapshotCache.CloneCache();
            var block = Block();
            var committee = NativeContract.NEO.GetCommitteeAddress(snapshot);
            var owner = H(0xd1);
            var council = H(0xd2);
            var settlementManager = H(0xd3);

            NativeContract.NeoHubEmergencyManager.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), block,
                "configure", Hash160(owner), Hash160(council), Hash160(settlementManager));

            Assert.AreEqual(owner, AsUInt160(NativeContract.NeoHubEmergencyManager.Call(snapshot, "getOwner")));
            Assert.AreEqual(council, AsUInt160(NativeContract.NeoHubEmergencyManager.Call(snapshot, "getEmergencyCouncil")));
            Assert.AreEqual(settlementManager, AsUInt160(NativeContract.NeoHubEmergencyManager.Call(snapshot, "getSettlementManager")));
            Assert.IsFalse(NativeContract.NeoHubEmergencyManager.Call(snapshot, "isPaused").GetBoolean());

            Assert.ThrowsExactly<InvalidOperationException>(() =>
                NativeContract.NeoHubEmergencyManager.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                    "pause"));

            NativeContract.NeoHubEmergencyManager.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(council), block,
                "pause");
            Assert.IsTrue(NativeContract.NeoHubEmergencyManager.Call(snapshot, "isPaused").GetBoolean());

            Assert.ThrowsExactly<InvalidOperationException>(() =>
                NativeContract.NeoHubEmergencyManager.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(council), block,
                    "resume"));

            NativeContract.NeoHubEmergencyManager.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "resume");
            Assert.IsFalse(NativeContract.NeoHubEmergencyManager.Call(snapshot, "isPaused").GetBoolean());
        }

        [TestMethod]
        public void EmergencyManager_EscapeHatchConsumesCanonicalAndProofLeavesOnlyWhilePaused()
        {
            var snapshot = _snapshotCache.CloneCache();
            var block = Block();
            var committee = NativeContract.NEO.GetCommitteeAddress(snapshot);
            var owner = H(0xe1);
            var council = H(0xe2);
            var sender = H(0xe3);
            var otherSender = H(0xe4);
            const uint singleEntryChainId = 1015;
            const uint proofChainId = 1016;
            var singleEntryLeaf = H256(0xe5);
            var proofLeaf = H256(0xe6);
            var proofSibling = H256(0xe7);

            NativeContract.NeoHubEmergencyManager.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), block,
                "configure", Hash160(owner), Hash160(council), Hash160(NativeContract.NeoHubSettlementManager.Hash));
            snapshot.Add(SettlementKey(0x03, singleEntryChainId), new StorageItem(singleEntryLeaf.ToArray()));
            snapshot.Add(SettlementKey(0x03, proofChainId), new StorageItem(HashPair(proofLeaf, proofSibling).ToArray()));

            Assert.ThrowsExactly<InvalidOperationException>(() =>
                CallAsScript(NativeContract.NeoHubEmergencyManager, snapshot, sender, block,
                    "escapeHatchExit", Integer(singleEntryChainId), Hash160(sender), Hash256(singleEntryLeaf)));

            NativeContract.NeoHubEmergencyManager.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(council), block,
                "pause");

            Assert.ThrowsExactly<InvalidOperationException>(() =>
                CallAsScript(NativeContract.NeoHubEmergencyManager, snapshot, otherSender, block,
                    "escapeHatchExit", Integer(singleEntryChainId), Hash160(sender), Hash256(singleEntryLeaf)));

            CallAsScript(NativeContract.NeoHubEmergencyManager, snapshot, sender, block,
                "escapeHatchExit", Integer(singleEntryChainId), Hash160(sender), Hash256(singleEntryLeaf));
            Assert.IsTrue(NativeContract.NeoHubEmergencyManager.Call(snapshot,
                "isEscapeConsumed", Integer(singleEntryChainId), Hash256(singleEntryLeaf)).GetBoolean());
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                CallAsScript(NativeContract.NeoHubEmergencyManager, snapshot, sender, block,
                    "escapeHatchExit", Integer(singleEntryChainId), Hash160(sender), Hash256(singleEntryLeaf)));

            Assert.ThrowsExactly<InvalidOperationException>(() =>
                CallAsScript(NativeContract.NeoHubEmergencyManager, snapshot, sender, block,
                    "escapeHatchExitWithProof", Integer(proofChainId), Hash160(sender), Hash256(proofLeaf),
                    ArrayParam(Bytes(proofSibling.ToArray())), Integer(1)));
            CallAsScript(NativeContract.NeoHubEmergencyManager, snapshot, sender, block,
                "escapeHatchExitWithProof", Integer(proofChainId), Hash160(sender), Hash256(proofLeaf),
                ArrayParam(Bytes(proofSibling.ToArray())), Integer(0));
            Assert.IsTrue(NativeContract.NeoHubEmergencyManager.Call(snapshot,
                "isEscapeConsumed", Integer(proofChainId), Hash256(proofLeaf)).GetBoolean());
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                CallAsScript(NativeContract.NeoHubEmergencyManager, snapshot, sender, block,
                    "escapeHatchExitWithProof", Integer(proofChainId), Hash160(sender), Hash256(proofLeaf),
                    ArrayParam(Bytes(proofSibling.ToArray())), Integer(0)));
        }

        [TestMethod]
        public void GovernanceController_ConfiguresCouncilAdmissionAndApprovedSets()
        {
            var snapshot = _snapshotCache.CloneCache();
            var block = Block();
            var committee = NativeContract.NEO.GetCommitteeAddress(snapshot);
            var owner = H(0xf1);
            var member1 = Key(0x31);
            var member2 = Key(0x32);
            var verifier = H(0xf2);
            var bridge = H(0xf3);

            NativeContract.NeoHubGovernanceController.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), block,
                "configure", Hash160(owner), PublicKeyArray(member1.PublicKey, member2.PublicKey), Integer(2), Integer(2));

            Assert.AreEqual(owner, AsUInt160(NativeContract.NeoHubGovernanceController.Call(snapshot, "getOwner")));
            Assert.AreEqual(2, NativeContract.NeoHubGovernanceController.Call(snapshot, "getCouncilCount").GetInteger());
            Assert.AreEqual(2, NativeContract.NeoHubGovernanceController.Call(snapshot, "getThreshold").GetInteger());
            Assert.AreEqual(2, NativeContract.NeoHubGovernanceController.Call(snapshot, "getTimelockSeconds").GetInteger());
            Assert.AreEqual(0, NativeContract.NeoHubGovernanceController.Call(snapshot, "getAdmissionMode").GetInteger());
            Assert.IsTrue(NativeContract.NeoHubGovernanceController.Call(snapshot,
                "isCouncilMember", PublicKey(member1.PublicKey)).GetBoolean());
            Assert.IsFalse(NativeContract.NeoHubGovernanceController.Call(snapshot,
                "isCouncilMember", PublicKey(Key(0x33).PublicKey)).GetBoolean());

            Assert.ThrowsExactly<InvalidOperationException>(() =>
                NativeContract.NeoHubGovernanceController.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(), block,
                    "setAdmissionMode", Integer(1)));
            NativeContract.NeoHubGovernanceController.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "setAdmissionMode", Integer(1));
            Assert.AreEqual(1, NativeContract.NeoHubGovernanceController.Call(snapshot, "getAdmissionMode").GetInteger());
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                NativeContract.NeoHubGovernanceController.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                    "setAdmissionMode", Integer(3)));

            NativeContract.NeoHubGovernanceController.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "approveVerifier", Hash160(verifier));
            NativeContract.NeoHubGovernanceController.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "approveBridgeAdapter", Hash160(bridge));
            Assert.IsTrue(NativeContract.NeoHubGovernanceController.Call(snapshot,
                "isApprovedVerifier", Hash160(verifier)).GetBoolean());
            Assert.IsTrue(NativeContract.NeoHubGovernanceController.Call(snapshot,
                "isApprovedBridgeAdapter", Hash160(bridge)).GetBoolean());
            NativeContract.NeoHubGovernanceController.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "revokeVerifier", Hash160(verifier));
            NativeContract.NeoHubGovernanceController.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "revokeBridgeAdapter", Hash160(bridge));
            Assert.IsFalse(NativeContract.NeoHubGovernanceController.Call(snapshot,
                "isApprovedVerifier", Hash160(verifier)).GetBoolean());
            Assert.IsFalse(NativeContract.NeoHubGovernanceController.Call(snapshot,
                "isApprovedBridgeAdapter", Hash160(bridge)).GetBoolean());
        }

        [TestMethod]
        public void GovernanceController_ProposalsTimelocksStagesAndImmutableFlags()
        {
            var snapshot = _snapshotCache.CloneCache();
            var committee = NativeContract.NEO.GetCommitteeAddress(snapshot);
            var owner = H(0xfa);
            var member1 = Key(0x41);
            var member2 = Key(0x42);
            var member1Hash = Contract.CreateSignatureRedeemScript(member1.PublicKey).ToScriptHash();
            var member2Hash = Contract.CreateSignatureRedeemScript(member2.PublicKey).ToScriptHash();
            var payload = new byte[] { 0x01, 0x02, 0x03 };

            NativeContract.NeoHubGovernanceController.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), Block(1000),
                "configure", Hash160(owner), PublicKeyArray(member1.PublicKey, member2.PublicKey), Integer(2), Integer(2));

            var proposalId = NativeContract.NeoHubGovernanceController.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(member1Hash), Block(1000),
                "createProposal", PublicKey(member1.PublicKey), Bytes(payload)).GetInteger();
            Assert.AreEqual(1, proposalId);
            CollectionAssert.AreEqual(payload, NativeContract.NeoHubGovernanceController.Call(snapshot,
                "getProposal", Integer(1)).GetSpan().ToArray());

            Assert.AreEqual(1, NativeContract.NeoHubGovernanceController.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(member1Hash), Block(1000),
                "approve", Integer(1), PublicKey(member1.PublicKey)).GetInteger());
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                NativeContract.NeoHubGovernanceController.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(member1Hash), Block(1000),
                    "approve", Integer(1), PublicKey(member1.PublicKey)));
            Assert.AreEqual(2, NativeContract.NeoHubGovernanceController.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(member2Hash), Block(1000),
                "approve", Integer(1), PublicKey(member2.PublicKey)).GetInteger());
            Assert.AreEqual(2, NativeContract.NeoHubGovernanceController.Call(snapshot,
                "getApprovalCount", Integer(1)).GetInteger());
            Assert.AreEqual(1000, NativeContract.NeoHubGovernanceController.Call(snapshot,
                "getApprovedAt", Integer(1)).GetInteger());

            Assert.IsFalse(NativeContract.NeoHubGovernanceController.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(), Block(2999),
                "isApprovedAndTimelocked", Integer(1)).GetBoolean());
            Assert.IsTrue(NativeContract.NeoHubGovernanceController.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(), Block(3000),
                "isApprovedAndTimelocked", Integer(1)).GetBoolean());
            Assert.AreEqual(NeoHubGovernanceControllerContract.StageExecutable, NativeContract.NeoHubGovernanceController.Call(snapshot,
                new Nep17NativeContractExtensions.ManualWitness(), Block(3000), "getProposalStage", Integer(1)).GetInteger());

            NativeContract.NeoHubGovernanceController.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), Block(3000),
                "markProposalExecuted", Integer(1));
            Assert.AreEqual(3000, NativeContract.NeoHubGovernanceController.Call(snapshot,
                "getProposalExecutedAt", Integer(1)).GetInteger());
            Assert.AreEqual(NeoHubGovernanceControllerContract.StageCooldown, NativeContract.NeoHubGovernanceController.Call(snapshot,
                new Nep17NativeContractExtensions.ManualWitness(), Block(3001), "getProposalStage", Integer(1)).GetInteger());
            Assert.AreEqual(NeoHubGovernanceControllerContract.StageComplete, NativeContract.NeoHubGovernanceController.Call(snapshot,
                new Nep17NativeContractExtensions.ManualWitness(), Block(5000), "getProposalStage", Integer(1)).GetInteger());

            NativeContract.NeoHubGovernanceController.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), Block(5000),
                "setImmutableFlag", Integer(7));
            Assert.IsTrue(NativeContract.NeoHubGovernanceController.Call(snapshot, "isImmutable", Integer(7)).GetBoolean());

            var proposal2 = NativeContract.NeoHubGovernanceController.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(member1Hash), Block(5000),
                "createProposal", PublicKey(member1.PublicKey), Bytes([0x09])).GetInteger();
            NativeContract.NeoHubGovernanceController.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(member1Hash), Block(5000),
                "approve", Integer((ulong)proposal2), PublicKey(member1.PublicKey));
            NativeContract.NeoHubGovernanceController.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(member2Hash), Block(5000),
                "approve", Integer((ulong)proposal2), PublicKey(member2.PublicKey));
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                NativeContract.NeoHubGovernanceController.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(), Block(6999),
                    "setImmutableFlagViaProposal", Integer(8), Integer((ulong)proposal2)));
            NativeContract.NeoHubGovernanceController.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(), Block(7000),
                "setImmutableFlagViaProposal", Integer(8), Integer((ulong)proposal2));
            Assert.IsTrue(NativeContract.NeoHubGovernanceController.Call(snapshot, "isImmutable", Integer(8)).GetBoolean());
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                NativeContract.NeoHubGovernanceController.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(), Block(7000),
                    "setImmutableFlagViaProposal", Integer(8), Integer((ulong)proposal2)));
        }

        [TestMethod]
        public void GovernanceController_GatesSemiPermissionlessChainRegistration()
        {
            var snapshot = _snapshotCache.CloneCache();
            var block = Block();
            var committee = NativeContract.NEO.GetCommitteeAddress(snapshot);
            var owner = H(0xfb);
            var member = Key(0x51);
            var verifier = H(0xfc);
            var bridge = H(0xfd);
            const uint chainId = 1017;
            var config = ChainConfig(chainId, securityLevel: 3, daMode: 1, gatewayEnabled: true,
                permissionlessExit: true, sequencerModel: 1, exitModel: 0, active: true);
            verifier.ToArray().CopyTo(config, 24);
            bridge.ToArray().CopyTo(config, 44);

            NativeContract.NeoHubGovernanceController.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), block,
                "configure", Hash160(owner), PublicKeyArray(member.PublicKey), Integer(1), Integer(1));
            NativeContract.NeoHubGovernanceController.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "setAdmissionMode", Integer(1));
            NativeContract.NeoHubGovernanceController.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "approveVerifier", Hash160(verifier));

            NativeContract.NeoHubChainRegistry.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), block,
                "configure", Hash160(owner));
            NativeContract.NeoHubChainRegistry.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "setGovernanceController", Hash160(NativeContract.NeoHubGovernanceController.Hash));

            Assert.ThrowsExactly<InvalidOperationException>(() =>
                NativeContract.NeoHubChainRegistry.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(), block,
                    "registerChainPublic", Integer(chainId), Bytes(config)));

            NativeContract.NeoHubGovernanceController.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "approveBridgeAdapter", Hash160(bridge));
            NativeContract.NeoHubChainRegistry.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(), block,
                "registerChainPublic", Integer(chainId), Bytes(config));

            Assert.IsTrue(NativeContract.NeoHubChainRegistry.Call(snapshot,
                "isActive", Integer(chainId)).GetBoolean());
        }

        [TestMethod]
        public void SequencerBond_ConfiguresSlashersAndMinimumBond()
        {
            var snapshot = _snapshotCache.CloneCache();
            var block = Block();
            var committee = NativeContract.NEO.GetCommitteeAddress(snapshot);
            var owner = H(0xc1);
            var slasher = H(0xc2);
            var nextSlasher = H(0xc3);
            var token = TransferTokenContract();
            var bond = NeoHubSequencerBond();

            snapshot.AddContract(token.Hash, token);

            bond.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), block,
                "configure", Hash160(owner), Hash160(token.Hash), ArrayParam(Hash160(slasher)));

            Assert.AreEqual(owner, AsUInt160(bond.Call(snapshot, "getOwner")));
            Assert.AreEqual(token.Hash, AsUInt160(bond.Call(snapshot, "getBondAsset")));
            Assert.AreEqual(new BigInteger(1_000_000UL), bond.Call(snapshot, "getMinBond").GetInteger());
            Assert.IsTrue(bond.Call(snapshot, "isSlasher", Hash160(slasher)).GetBoolean());

            Assert.ThrowsExactly<InvalidOperationException>(() =>
                bond.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(H(0xc4)), block,
                    "setMinBond", Integer(2_000_000)));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                bond.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                    "setMinBond", Integer(0)));

            bond.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "setMinBond", Integer(2_000_000));
            Assert.AreEqual(new BigInteger(2_000_000), bond.Call(snapshot, "getMinBond").GetInteger());

            bond.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "registerSlasher", Hash160(nextSlasher));
            Assert.IsTrue(bond.Call(snapshot, "isSlasher", Hash160(nextSlasher)).GetBoolean());

            bond.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "revokeSlasher", Hash160(slasher));
            Assert.IsFalse(bond.Call(snapshot, "isSlasher", Hash160(slasher)).GetBoolean());
        }

        [TestMethod]
        public void SequencerBond_DepositsSlashesAndWithdraws()
        {
            var snapshot = _snapshotCache.CloneCache();
            var block = Block();
            var committee = NativeContract.NEO.GetCommitteeAddress(snapshot);
            var owner = H(0xd1);
            var slasher = H(0xd2);
            var sponsor = H(0xd3);
            var sequencer = H(0xd4);
            var recipient = H(0xd5);
            const uint chainId = 1018;
            const long depositAmount = 1_500_000;
            const long slashAmount = 500_000;
            const long withdrawAmount = 400_000;
            var token = TransferTokenContract();
            var bond = NeoHubSequencerBond();

            snapshot.AddContract(token.Hash, token);

            Assert.IsFalse(bond.Call(snapshot, "hasMinBond", Integer(chainId), Hash160(sequencer)).GetBoolean());

            bond.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), block,
                "configure", Hash160(owner), Hash160(token.Hash), ArrayParam(Hash160(slasher)));

            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                CallAsScript(bond, snapshot, sponsor, block,
                    "deposit", Integer(0), Hash160(sequencer), Integer(depositAmount)));

            CallAsScript(bond, snapshot, sponsor, block,
                "deposit", Integer(chainId), Hash160(sequencer), Integer(depositAmount));

            Assert.AreEqual(new BigInteger(depositAmount), bond.Call(snapshot,
                "getBalance", Integer(chainId), Hash160(sequencer)).GetInteger());
            Assert.IsTrue(bond.Call(snapshot, "hasMinBond", Integer(chainId), Hash160(sequencer)).GetBoolean());

            Assert.ThrowsExactly<InvalidOperationException>(() =>
                CallAsScript(bond, snapshot, H(0xd6), block,
                    "slash", Integer(chainId), Hash160(sequencer), Integer(slashAmount), Hash160(recipient)));

            CallAsScript(bond, snapshot, slasher, block,
                "slash", Integer(chainId), Hash160(sequencer), Integer(slashAmount), Hash160(recipient));

            Assert.AreEqual(new BigInteger(1_000_000), bond.Call(snapshot,
                "getBalance", Integer(chainId), Hash160(sequencer)).GetInteger());
            Assert.IsTrue(bond.Call(snapshot, "hasMinBond", Integer(chainId), Hash160(sequencer)).GetBoolean());

            bond.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "withdraw", Integer(chainId), Hash160(sequencer), Integer(withdrawAmount));

            Assert.AreEqual(new BigInteger(600_000), bond.Call(snapshot,
                "getBalance", Integer(chainId), Hash160(sequencer)).GetInteger());
            Assert.IsFalse(bond.Call(snapshot, "hasMinBond", Integer(chainId), Hash160(sequencer)).GetBoolean());
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                bond.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                    "withdraw", Integer(chainId), Hash160(sequencer), Integer(1_000_000)));
        }

        [TestMethod]
        public void SequencerRegistry_ConfiguresPolicyAndRequiresBondedSequencer()
        {
            var snapshot = _snapshotCache.CloneCache();
            var block = Block();
            var committee = NativeContract.NEO.GetCommitteeAddress(snapshot);
            var owner = H(0xe1);
            var slasher = H(0xe2);
            var sponsor = H(0xe3);
            var sequencerAddress = H(0xe4);
            var secondSequencerAddress = H(0xe5);
            var sequencer = Key(0x61);
            var secondSequencer = Key(0x62);
            var sequencerAccount = Contract.CreateSignatureRedeemScript(sequencer.PublicKey).ToScriptHash();
            var secondSequencerAccount = Contract.CreateSignatureRedeemScript(secondSequencer.PublicKey).ToScriptHash();
            const uint chainId = 1019;
            var token = TransferTokenContract();
            var bond = NeoHubSequencerBond();
            var registry = NeoHubSequencerRegistry();

            snapshot.AddContract(token.Hash, token);

            bond.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), block,
                "configure", Hash160(owner), Hash160(token.Hash), ArrayParam(Hash160(slasher)));
            registry.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), block,
                "configure", Hash160(owner), Hash160(bond.Hash));

            Assert.AreEqual(owner, AsUInt160(registry.Call(snapshot, "getOwner")));
            Assert.AreEqual(21, registry.Call(snapshot, "getMaxCommitteeSize").GetInteger());
            Assert.AreEqual(86400, registry.Call(snapshot, "getExitWindowSeconds").GetInteger());

            Assert.ThrowsExactly<InvalidOperationException>(() =>
                registry.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(sequencerAccount), block,
                    "register", Integer(chainId), PublicKey(sequencer.PublicKey), Hash160(sequencerAddress)));

            CallAsScript(bond, snapshot, sponsor, block,
                "deposit", Integer(chainId), Hash160(sequencerAddress), Integer(NeoHubSequencerBondContract.DefaultMinBond));
            registry.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(sequencerAccount), block,
                "register", Integer(chainId), PublicKey(sequencer.PublicKey), Hash160(sequencerAddress));

            Assert.IsTrue(registry.Call(snapshot, "isRegistered", Integer(chainId), PublicKey(sequencer.PublicKey)).GetBoolean());
            Assert.AreEqual(1, registry.Call(snapshot, "getActiveCount", Integer(chainId)).GetInteger());
            Assert.AreEqual(1,
                registry.Call(snapshot, "getStatus", Integer(chainId), PublicKey(sequencer.PublicKey)).GetInteger());
            Assert.AreEqual(sequencerAddress,
                AsUInt160(registry.Call(snapshot, "getSequencerAddress", Integer(chainId), PublicKey(sequencer.PublicKey))));

            Assert.ThrowsExactly<InvalidOperationException>(() =>
                registry.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(sequencerAccount), block,
                    "register", Integer(chainId), PublicKey(sequencer.PublicKey), Hash160(sequencerAddress)));

            registry.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "setMaxCommitteeSize", Integer(1));
            CallAsScript(bond, snapshot, sponsor, block,
                "deposit", Integer(chainId), Hash160(secondSequencerAddress), Integer(NeoHubSequencerBondContract.DefaultMinBond));
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                registry.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(secondSequencerAccount), block,
                    "register", Integer(chainId), PublicKey(secondSequencer.PublicKey), Hash160(secondSequencerAddress)));
        }

        [TestMethod]
        public void SequencerRegistry_ExitWindowKeepsSequencerUntilFinalized()
        {
            var snapshot = _snapshotCache.CloneCache();
            var committee = NativeContract.NEO.GetCommitteeAddress(snapshot);
            var owner = H(0xf1);
            var slasher = H(0xf2);
            var sponsor = H(0xf3);
            var sequencerAddress = H(0xf4);
            var sequencer = Key(0x71);
            var sequencerAccount = Contract.CreateSignatureRedeemScript(sequencer.PublicKey).ToScriptHash();
            const uint chainId = 1020;
            var token = TransferTokenContract();
            var bond = NeoHubSequencerBond();
            var registry = NeoHubSequencerRegistry();

            snapshot.AddContract(token.Hash, token);

            bond.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), Block(1000),
                "configure", Hash160(owner), Hash160(token.Hash), ArrayParam(Hash160(slasher)));
            registry.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), Block(1000),
                "configure", Hash160(owner), Hash160(bond.Hash));
            registry.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), Block(1000),
                "setExitWindowSeconds", Integer(60));
            CallAsScript(bond, snapshot, sponsor, Block(1000),
                "deposit", Integer(chainId), Hash160(sequencerAddress), Integer(NeoHubSequencerBondContract.DefaultMinBond));
            registry.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(sequencerAccount), Block(1000),
                "register", Integer(chainId), PublicKey(sequencer.PublicKey), Hash160(sequencerAddress));

            Assert.ThrowsExactly<InvalidOperationException>(() =>
                registry.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(H(0xf5)), Block(1000),
                    "unregister", Integer(chainId), PublicKey(sequencer.PublicKey)));

            var exitsAt = registry.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(sequencerAccount), Block(1000),
                "unregister", Integer(chainId), PublicKey(sequencer.PublicKey)).GetInteger();

            Assert.AreEqual(61, exitsAt);
            Assert.IsTrue(registry.Call(snapshot, "isRegistered", Integer(chainId), PublicKey(sequencer.PublicKey)).GetBoolean());
            Assert.AreEqual(1, registry.Call(snapshot, "getActiveCount", Integer(chainId)).GetInteger());
            Assert.AreEqual(2,
                registry.Call(snapshot, "getStatus", Integer(chainId), PublicKey(sequencer.PublicKey)).GetInteger());

            Assert.ThrowsExactly<InvalidOperationException>(() =>
                registry.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(), Block(60000),
                    "finalize", Integer(chainId), PublicKey(sequencer.PublicKey)));

            registry.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(), Block(61000),
                "finalize", Integer(chainId), PublicKey(sequencer.PublicKey));

            Assert.IsFalse(registry.Call(snapshot, "isRegistered", Integer(chainId), PublicKey(sequencer.PublicKey)).GetBoolean());
            Assert.AreEqual(0, registry.Call(snapshot, "getActiveCount", Integer(chainId)).GetInteger());
            Assert.AreEqual(0, registry.Call(snapshot, "getStatus", Integer(chainId), PublicKey(sequencer.PublicKey)).GetInteger());
            Assert.AreEqual(UInt160.Zero,
                AsUInt160(registry.Call(snapshot, "getSequencerAddress", Integer(chainId), PublicKey(sequencer.PublicKey))));
        }

        [TestMethod]
        public void ForcedInclusion_ConfiguresFeesAndEnqueuesBeforeStateChanges()
        {
            var snapshot = _snapshotCache.CloneCache();
            var block = Block(1000);
            var committee = NativeContract.NEO.GetCommitteeAddress(snapshot);
            var owner = H(0x91);
            var settlementManager = H(0x92);
            var feeRecipient = H(0x93);
            var sender = H(0x94);
            const uint chainId = 1021;
            var gas = TransferTokenContract();
            var tx = new byte[] { 0x01, 0x02, 0x03 };
            var txHash = H256(0x95);
            var forced = NeoHubForcedInclusion();

            snapshot.AddContract(gas.Hash, gas);

            forced.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), block,
                "configure", Hash160(owner), Hash160(settlementManager));

            Assert.AreEqual(owner, AsUInt160(forced.Call(snapshot, "getOwner")));
            Assert.AreEqual(settlementManager, AsUInt160(forced.Call(snapshot, "getSettlementManager")));
            Assert.AreEqual(7200, forced.Call(snapshot, "getDeadlineSeconds").GetInteger());
            Assert.AreEqual(BigInteger.Zero, forced.Call(snapshot, "getFee").GetInteger());

            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                forced.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                    "setDeadlineSeconds", Integer(59)));
            forced.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "setDeadlineSeconds", Integer(60));
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                forced.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                    "setFee", Integer(10)));
            forced.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "setFeeRecipient", Hash160(feeRecipient));
            forced.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "setGasToken", Hash160(gas.Hash));
            forced.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "setFee", Integer(10));

            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                CallAsScript(forced, snapshot, sender, block,
                    "enqueueForcedTransaction", Integer(0), Bytes(tx), Hash256(txHash)));
            Assert.AreEqual(0, forced.Call(snapshot, "getEntry", Integer(chainId), Integer(1)).GetSpan().Length);

            var nonce = CallAsScript(forced, snapshot, sender, block,
                "enqueueForcedTransaction", Integer(chainId), Bytes(tx), Hash256(txHash)).GetInteger();

            Assert.AreEqual(1, nonce);
            var entry = forced.Call(snapshot, "getEntry", Integer(chainId), Integer(1)).GetSpan().ToArray();
            Assert.AreEqual(20 + 32 + 4 + tx.Length + 4, entry.Length);
            Assert.AreEqual(sender, new UInt160(entry.AsSpan(0, UInt160.Length)));
            Assert.AreEqual(txHash, new UInt256(entry.AsSpan(20, UInt256.Length)));
            Assert.AreEqual((uint)tx.Length, ReadU32(entry.AsSpan(52)));
            CollectionAssert.AreEqual(tx, entry.Skip(56).Take(tx.Length).ToArray());
            Assert.AreEqual(61u, ReadU32(entry.AsSpan(56 + tx.Length)));

            Assert.IsFalse(forced.Call(snapshot, "isConsumed", Integer(chainId), Integer(1)).GetBoolean());
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                forced.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(sender), block,
                    "markConsumed", Integer(chainId), Integer(1)));
            forced.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(settlementManager), block,
                "markConsumed", Integer(chainId), Integer(1));
            Assert.IsTrue(forced.Call(snapshot, "isConsumed", Integer(chainId), Integer(1)).GetBoolean());
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                forced.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(settlementManager), block,
                    "markConsumed", Integer(chainId), Integer(1)));
        }

        [TestMethod]
        public void ForcedInclusion_ReportsCensorshipAfterDeadlineOnce()
        {
            var snapshot = _snapshotCache.CloneCache();
            var committee = NativeContract.NEO.GetCommitteeAddress(snapshot);
            var owner = H(0x96);
            var settlementManager = H(0x97);
            var sender = H(0x98);
            var sequencer = H(0x99);
            const uint chainId = 1022;
            var forced = NeoHubForcedInclusion();

            forced.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), Block(1000),
                "configure", Hash160(owner), Hash160(settlementManager));
            forced.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), Block(1000),
                "setDeadlineSeconds", Integer(60));

            CallAsScript(forced, snapshot, sender, Block(1000),
                "enqueueForcedTransaction", Integer(chainId), Bytes([0x0a]), Hash256(H256(0x9a)));
            CallAsScript(forced, snapshot, sender, Block(1000),
                "enqueueForcedTransaction", Integer(chainId), Bytes([0x0b]), Hash256(H256(0x9b)));

            Assert.IsFalse(forced.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(), Block(60000),
                "reportCensorship", Integer(chainId), Integer(1), Hash160(sequencer)).GetBoolean());
            Assert.IsTrue(forced.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(), Block(61000),
                "reportCensorship", Integer(chainId), Integer(1), Hash160(sequencer)).GetBoolean());
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                forced.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(), Block(61000),
                    "reportCensorship", Integer(chainId), Integer(1), Hash160(sequencer)));

            forced.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(settlementManager), Block(1000),
                "markConsumed", Integer(chainId), Integer(2));
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                forced.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(), Block(61000),
                    "reportCensorship", Integer(chainId), Integer(2), Hash160(sequencer)));
        }

        [TestMethod]
        public void OptimisticChallenge_ConfiguresOpensAndFinalizesExpiredWindow()
        {
            var snapshot = _snapshotCache.CloneCache();
            var committee = NativeContract.NEO.GetCommitteeAddress(snapshot);
            var owner = H(0xa1);
            var sequencer = H(0xa2);
            const uint chainId = 1023;
            const ulong batchNumber = 7;
            var challenge = NeoHubOptimisticChallenge();
            var settlement = NativeContract.NeoHubSettlementManager;
            var chainRegistry = NativeContract.NeoHubChainRegistry;
            var daValidator = AlwaysTrueContract();
            var postStateRoot = H256(0xa3);
            var header = BatchCommitment(chainId, batchNumber, 2, postStateRoot, H256(0xa4), H256(0xa5));

            snapshot.AddContract(daValidator.Hash, daValidator);
            chainRegistry.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), Block(1000),
                "configure", Hash160(owner));
            chainRegistry.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), Block(1000),
                "registerChain", Integer(chainId), Bytes(ChainConfig(chainId, 3, 0, true, true, 1, 0, true)));
            settlement.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), Block(1000),
                "configure", Hash160(owner), Hash160(chainRegistry.Hash), Hash160(NativeContract.NeoHubVerifierRegistry.Hash));
            settlement.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), Block(1000),
                "setOptimisticChallenge", Hash160(challenge.Hash));
            settlement.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), Block(1000),
                "setDAValidator", Hash160(daValidator.Hash));
            snapshot.Add(SettlementKey(0x01, chainId, batchNumber), new StorageItem([NeoHubSettlementManagerContract.StatusChallengeable]));
            snapshot.Add(SettlementKey(0x02, chainId, batchNumber), new StorageItem(header));

            challenge.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), Block(1000),
                "configure", Hash160(owner), Hash160(settlement.Hash), Hash160(NativeContract.NeoHubSequencerBond.Hash));

            Assert.AreEqual(owner, AsUInt160(challenge.Call(snapshot, "getOwner")));
            Assert.AreEqual(settlement.Hash, AsUInt160(challenge.Call(snapshot, "getSettlementManager")));
            Assert.AreEqual(NativeContract.NeoHubSequencerBond.Hash, AsUInt160(challenge.Call(snapshot, "getSequencerBond")));
            Assert.AreEqual(3600, challenge.Call(snapshot, "getWindowSeconds").GetInteger());
            Assert.AreEqual(5000, challenge.Call(snapshot, "getChallengerRewardBps").GetInteger());

            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                challenge.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), Block(1000),
                    "setWindowSeconds", Integer(59)));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                challenge.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), Block(1000),
                    "setChallengerRewardBps", Integer(0)));
            challenge.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), Block(1000),
                "setWindowSeconds", Integer(60));
            challenge.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), Block(1000),
                "setChallengerRewardBps", Integer(2500));

            Assert.ThrowsExactly<InvalidOperationException>(() =>
                challenge.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), Block(1000),
                    "openWindow", Integer(chainId), Integer(batchNumber), Hash160(sequencer)));

            var deadline = challenge.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(settlement.Hash), Block(1000),
                "openWindow", Integer(chainId), Integer(batchNumber), Hash160(sequencer)).GetInteger();

            Assert.AreEqual(61, deadline);
            Assert.AreEqual(61, challenge.Call(snapshot, "getDeadline", Integer(chainId), Integer(batchNumber)).GetInteger());
            Assert.AreEqual(sequencer, AsUInt160(challenge.Call(snapshot, "getSequencer", Integer(chainId), Integer(batchNumber))));
            Assert.IsTrue(challenge.Call(snapshot, "isWindowOpen", Integer(chainId), Integer(batchNumber), Integer(61)).GetBoolean());
            Assert.IsFalse(challenge.Call(snapshot, "isWindowOpen", Integer(chainId), Integer(batchNumber), Integer(62)).GetBoolean());
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                challenge.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(settlement.Hash), Block(1000),
                    "openWindow", Integer(chainId), Integer(batchNumber), Hash160(sequencer)));
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                CallAsScript(challenge, snapshot, H(0xa6), Block(61000),
                    "finalizeIfPastWindow", Integer(chainId), Integer(batchNumber)));

            CallAsScript(challenge, snapshot, H(0xa6), Block(62000),
                "finalizeIfPastWindow", Integer(chainId), Integer(batchNumber));

            Assert.AreEqual(NeoHubSettlementManagerContract.StatusFinalized,
                settlement.Call(snapshot, "getBatchStatus", Integer(chainId), Integer(batchNumber)).GetInteger());
            Assert.AreEqual(batchNumber, (ulong)settlement.Call(snapshot, "getLatestFinalizedBatch", Integer(chainId)).GetInteger());
            Assert.AreEqual(postStateRoot, AsUInt256(settlement.Call(snapshot, "getCanonicalStateRoot", Integer(chainId))));
        }

        [TestMethod]
        public void OptimisticChallenge_AcceptsFraudOnceAndSlashesSequencerBond()
        {
            var snapshot = _snapshotCache.CloneCache();
            var committee = NativeContract.NEO.GetCommitteeAddress(snapshot);
            var owner = H(0xb1);
            var sponsor = H(0xb2);
            var sequencer = H(0xb3);
            var challenger = H(0xb4);
            const uint chainId = 1024;
            const ulong batchNumber = 8;
            const long bondAmount = 1_500_000;
            var token = TransferTokenContract();
            var verifier = FraudVerifierContract(true);
            var rejectingVerifier = FraudVerifierContract(false);
            var challenge = NeoHubOptimisticChallenge();
            var settlement = NativeContract.NeoHubSettlementManager;
            var bond = NeoHubSequencerBond();

            snapshot.AddContract(token.Hash, token);
            snapshot.AddContract(verifier.Hash, verifier);
            snapshot.AddContract(rejectingVerifier.Hash, rejectingVerifier);
            settlement.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), Block(1000),
                "configure", Hash160(owner), Hash160(NativeContract.NeoHubChainRegistry.Hash), Hash160(NativeContract.NeoHubVerifierRegistry.Hash));
            settlement.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), Block(1000),
                "setOptimisticChallenge", Hash160(challenge.Hash));
            snapshot.Add(SettlementKey(0x01, chainId, batchNumber), new StorageItem([NeoHubSettlementManagerContract.StatusChallengeable]));
            bond.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), Block(1000),
                "configure", Hash160(owner), Hash160(token.Hash), ArrayParam(Hash160(challenge.Hash)));
            CallAsScript(bond, snapshot, sponsor, Block(1000),
                "deposit", Integer(chainId), Hash160(sequencer), Integer(bondAmount));

            challenge.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), Block(1000),
                "configure", Hash160(owner), Hash160(settlement.Hash), Hash160(bond.Hash));
            challenge.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), Block(1000),
                "setWindowSeconds", Integer(60));
            challenge.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(settlement.Hash), Block(1000),
                "openWindow", Integer(chainId), Integer(batchNumber), Hash160(sequencer));

            Assert.ThrowsExactly<InvalidOperationException>(() =>
                challenge.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(challenger), Block(1000),
                    "challenge", Integer(chainId), Integer(batchNumber), Hash160(challenger), Bytes([0x01]), Hash160(rejectingVerifier.Hash)));
            Assert.ThrowsExactly<ArgumentException>(() =>
                challenge.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(challenger), Block(1000),
                    "challenge", Integer(chainId), Integer(batchNumber), Hash160(challenger), Bytes([]), Hash160(verifier.Hash)));
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                challenge.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(), Block(1000),
                    "challenge", Integer(chainId), Integer(batchNumber), Hash160(challenger), Bytes([0x01]), Hash160(verifier.Hash)));

            challenge.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(challenger), Block(1000),
                "challenge", Integer(chainId), Integer(batchNumber), Hash160(challenger), Bytes([0x01, 0x02]), Hash160(verifier.Hash));

            Assert.IsTrue(challenge.Call(snapshot, "isFraudAccepted", Integer(chainId), Integer(batchNumber)).GetBoolean());
            Assert.AreEqual(challenger, AsUInt160(challenge.Call(snapshot, "getAcceptedFraud", Integer(chainId), Integer(batchNumber))));
            Assert.AreEqual(BigInteger.Zero, bond.Call(snapshot, "getBalance", Integer(chainId), Hash160(sequencer)).GetInteger());
            Assert.AreEqual(NeoHubSettlementManagerContract.StatusReverted,
                settlement.Call(snapshot, "getBatchStatus", Integer(chainId), Integer(batchNumber)).GetInteger());
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                challenge.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(challenger), Block(1000),
                    "challenge", Integer(chainId), Integer(batchNumber), Hash160(challenger), Bytes([0x03]), Hash160(verifier.Hash)));
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                CallAsScript(challenge, snapshot, H(0xb5), Block(62000),
                    "finalizeIfPastWindow", Integer(chainId), Integer(batchNumber)));
        }

        [TestMethod]
        public void GovernanceFraudVerifier_VerifiesV1AndV2StructuralPayloads()
        {
            var snapshot = _snapshotCache.CloneCache();
            var verifier = NeoHubGovernanceFraudVerifier();
            var claimed = H256(0xc1);
            var replayed = H256(0xc2);
            const uint chainId = 1025;
            const ulong batchNumber = 9;

            Assert.AreEqual(101, NeoHubGovernanceFraudVerifierContract.FraudProofPayloadSize);
            Assert.AreEqual(105, NeoHubGovernanceFraudVerifierContract.V2HeaderSize);
            Assert.AreEqual(64 * 1024, NeoHubGovernanceFraudVerifierContract.MaxDisputedTxBytes);

            var methods = verifier.GetContractState(TestProtocolSettings.Default, 0).Manifest.Abi.Methods;
            Assert.IsTrue(methods.Single(m => m.Name == "verifyFraud").Safe);

            var acceptedEvents = new System.Collections.Generic.List<NotifyEventArgs>();
            Assert.IsTrue(verifier.Call(snapshot, "verifyFraud", (_, e) => acceptedEvents.Add(e),
                Integer(chainId), Integer(batchNumber), Bytes(GovernanceFraudPayload(1, claimed, replayed))).GetBoolean());
            Assert.HasCount(1, acceptedEvents);
            Assert.AreEqual("FraudProofAccepted", acceptedEvents[0].EventName);
            Assert.AreEqual(chainId, (uint)acceptedEvents[0].State[0].GetInteger());
            Assert.AreEqual(batchNumber, (ulong)acceptedEvents[0].State[1].GetInteger());
            Assert.AreEqual(claimed, AsUInt256(acceptedEvents[0].State[2]));
            Assert.AreEqual(replayed, AsUInt256(acceptedEvents[0].State[3]));

            var v2Payload = GovernanceFraudPayload(2, claimed, replayed, [0x10, 0x11, 0x12]);
            Assert.IsTrue(verifier.Call(snapshot, "verifyFraud",
                Integer(chainId), Integer(batchNumber), Bytes(v2Payload)).GetBoolean());
        }

        [TestMethod]
        public void GovernanceFraudVerifier_RejectsMalformedPayloadsWithReasons()
        {
            var snapshot = _snapshotCache.CloneCache();
            var verifier = NeoHubGovernanceFraudVerifier();
            var claimed = H256(0xc3);
            var replayed = H256(0xc4);
            const uint chainId = 1026;
            const ulong batchNumber = 10;

            AssertRejected(verifier, snapshot, GovernanceFraudPayload(1, claimed, replayed).Take(100).ToArray(),
                NeoHubGovernanceFraudVerifierContract.ReasonBadLength);
            AssertRejected(verifier, snapshot, GovernanceFraudPayload(9, claimed, replayed),
                NeoHubGovernanceFraudVerifierContract.ReasonBadVersion);
            AssertRejected(verifier, snapshot, GovernanceFraudPayload(1, claimed, claimed),
                NeoHubGovernanceFraudVerifierContract.ReasonNoDiscrepancy);
            AssertRejected(verifier, snapshot, GovernanceFraudPayload(2, claimed, replayed).Take(104).ToArray(),
                NeoHubGovernanceFraudVerifierContract.ReasonBadLength);

            var oversizedWitness = GovernanceFraudPayload(2, claimed, replayed);
            WriteU32(oversizedWitness, 101, NeoHubGovernanceFraudVerifierContract.MaxDisputedTxBytes + 1U);
            AssertRejected(verifier, snapshot, oversizedWitness,
                NeoHubGovernanceFraudVerifierContract.ReasonOversizedWitness);

            var mismatchedWitness = GovernanceFraudPayload(2, claimed, replayed, [0x01, 0x02]);
            WriteU32(mismatchedWitness, 101, 3);
            AssertRejected(verifier, snapshot, mismatchedWitness,
                NeoHubGovernanceFraudVerifierContract.ReasonBadLength);

            void AssertRejected(NativeContract contract, DataCache cache, byte[] payload, byte reason)
            {
                var rejectedEvents = new System.Collections.Generic.List<NotifyEventArgs>();
                Assert.IsFalse(contract.Call(cache, "verifyFraud", (_, e) => rejectedEvents.Add(e),
                    Integer(chainId), Integer(batchNumber), Bytes(payload)).GetBoolean());
                Assert.HasCount(1, rejectedEvents);
                Assert.AreEqual("FraudProofRejected", rejectedEvents[0].EventName);
                Assert.AreEqual(chainId, (uint)rejectedEvents[0].State[0].GetInteger());
                Assert.AreEqual(batchNumber, (ulong)rejectedEvents[0].State[1].GetInteger());
                Assert.AreEqual(reason, (byte)rejectedEvents[0].State[2].GetInteger());
            }
        }

        [TestMethod]
        public void RestrictedExecutionFraudVerifier_VerifiesV3StorageProofPayload()
        {
            var snapshot = _snapshotCache.CloneCache();
            var verifier = NeoHubRestrictedExecutionFraudVerifier();
            var keyA = new byte[] { 0xaa };
            var preValueA = new byte[] { 0x00 };
            var postValueA = new byte[] { 0x11 };
            var keyB = new byte[] { 0xbb };
            var valueB = new byte[] { 0x22 };
            var preLeafA = RestrictedHashEntry(keyA, preValueA);
            var postLeafA = RestrictedHashEntry(keyA, postValueA);
            var leafB = RestrictedHashEntry(keyB, valueB);
            var preRoot = HashPair(preLeafA, leafB);
            var postRoot = HashPair(postLeafA, leafB);
            var claimedRoot = H256(0xde);
            const uint chainId = 1027;
            const ulong batchNumber = 11;

            Assert.AreEqual(101, NeoHubRestrictedExecutionFraudVerifierContract.V1HeaderSize);
            Assert.AreEqual(105, NeoHubRestrictedExecutionFraudVerifierContract.V2HeaderSize);
            Assert.AreEqual(3, NeoHubRestrictedExecutionFraudVerifierContract.SupportedVersion3);
            Assert.AreEqual(64 * 1024, NeoHubRestrictedExecutionFraudVerifierContract.MaxDisputedTxBytes);
            Assert.AreEqual(32, NeoHubRestrictedExecutionFraudVerifierContract.MaxStorageProofsPerPayload);
            Assert.AreEqual(256, NeoHubRestrictedExecutionFraudVerifierContract.MaxKeyBytes);
            Assert.AreEqual(4096, NeoHubRestrictedExecutionFraudVerifierContract.MaxValueBytes);
            Assert.AreEqual(64, NeoHubRestrictedExecutionFraudVerifierContract.MaxSiblingDepth);

            var methods = verifier.GetContractState(TestProtocolSettings.Default, 0).Manifest.Abi.Methods;
            Assert.IsTrue(methods.Single(m => m.Name == "verifyFraud").Safe);

            var acceptedEvents = new System.Collections.Generic.List<NotifyEventArgs>();
            var payload = RestrictedFraudPayload(preRoot, claimedRoot, postRoot, [0x42, 0x43],
                keyA, preValueA, postValueA, 0, [leafB], [leafB]);
            Assert.IsTrue(verifier.Call(snapshot, "verifyFraud", (_, e) => acceptedEvents.Add(e),
                Integer(chainId), Integer(batchNumber), Bytes(payload)).GetBoolean());
            Assert.HasCount(1, acceptedEvents);
            Assert.AreEqual("FraudProofAccepted", acceptedEvents[0].EventName);
            Assert.AreEqual(chainId, (uint)acceptedEvents[0].State[0].GetInteger());
            Assert.AreEqual(batchNumber, (ulong)acceptedEvents[0].State[1].GetInteger());
            Assert.AreEqual(claimedRoot, AsUInt256(acceptedEvents[0].State[2]));
            Assert.AreEqual(postRoot, AsUInt256(acceptedEvents[0].State[3]));
        }

        [TestMethod]
        public void RestrictedExecutionFraudVerifier_RejectsMalformedPayloadsWithReasons()
        {
            var snapshot = _snapshotCache.CloneCache();
            var verifier = NeoHubRestrictedExecutionFraudVerifier();
            var key = new byte[] { 0x31 };
            var preValue = new byte[] { 0x01 };
            var postValue = new byte[] { 0x02 };
            var preRoot = RestrictedHashEntry(key, preValue);
            var postRoot = RestrictedHashEntry(key, postValue);
            var claimedRoot = H256(0xdf);
            var valid = RestrictedFraudPayload(preRoot, claimedRoot, postRoot, [],
                key, preValue, postValue, 0, [], []);
            const uint chainId = 1028;
            const ulong batchNumber = 12;

            AssertRejected([], NeoHubRestrictedExecutionFraudVerifierContract.ReasonBadLength);

            var badVersion = (byte[])valid.Clone();
            badVersion[0] = 2;
            AssertRejected(badVersion, NeoHubRestrictedExecutionFraudVerifierContract.ReasonBadVersion);
            AssertRejected(valid.Take(104).ToArray(), NeoHubRestrictedExecutionFraudVerifierContract.ReasonBadLength);

            var oversizedWitness = new byte[NeoHubRestrictedExecutionFraudVerifierContract.V2HeaderSize];
            oversizedWitness[0] = NeoHubRestrictedExecutionFraudVerifierContract.SupportedVersion3;
            WriteU32(oversizedWitness, 101, NeoHubRestrictedExecutionFraudVerifierContract.MaxDisputedTxBytes + 1U);
            AssertRejected(oversizedWitness, NeoHubRestrictedExecutionFraudVerifierContract.ReasonOversizedWitness);

            var zeroProofs = (byte[])valid.Clone();
            WriteU32(zeroProofs, NeoHubRestrictedExecutionFraudVerifierContract.V2HeaderSize, 0);
            AssertRejected(zeroProofs, NeoHubRestrictedExecutionFraudVerifierContract.ReasonProofCountInvalid);

            AssertRejected(RestrictedFraudPayload(preRoot, postRoot, postRoot, [],
                key, preValue, postValue, 0, [], []), NeoHubRestrictedExecutionFraudVerifierContract.ReasonNoDiscrepancy);

            var largeKey = Enumerable.Repeat((byte)0x55, NeoHubRestrictedExecutionFraudVerifierContract.MaxKeyBytes + 1).ToArray();
            AssertRejected(RestrictedFraudPayload(RestrictedHashEntry(largeKey, preValue), H256(0xe1),
                RestrictedHashEntry(largeKey, postValue), [], largeKey, preValue, postValue, 0, [], []),
                NeoHubRestrictedExecutionFraudVerifierContract.ReasonInvalidStorageProof);

            var badPreRoot = (byte[])valid.Clone();
            badPreRoot[1] ^= 0xff;
            AssertRejected(badPreRoot, NeoHubRestrictedExecutionFraudVerifierContract.ReasonPreStateRootMismatch);

            var badPostRoot = (byte[])valid.Clone();
            badPostRoot[65] ^= 0xff;
            AssertRejected(badPostRoot, NeoHubRestrictedExecutionFraudVerifierContract.ReasonReplayedPostStateRootMismatch);

            AssertRejected(AppendByte(valid, 0xff), NeoHubRestrictedExecutionFraudVerifierContract.ReasonBadLength);

            void AssertRejected(byte[] payload, byte reason)
            {
                var rejectedEvents = new System.Collections.Generic.List<NotifyEventArgs>();
                Assert.IsFalse(verifier.Call(snapshot, "verifyFraud", (_, e) => rejectedEvents.Add(e),
                    Integer(chainId), Integer(batchNumber), Bytes(payload)).GetBoolean());
                Assert.HasCount(1, rejectedEvents);
                Assert.AreEqual("FraudProofRejected", rejectedEvents[0].EventName);
                Assert.AreEqual(chainId, (uint)rejectedEvents[0].State[0].GetInteger());
                Assert.AreEqual(batchNumber, (ulong)rejectedEvents[0].State[1].GetInteger());
                Assert.AreEqual(reason, (byte)rejectedEvents[0].State[2].GetInteger());
            }
        }

        private static Block Block(ulong timestamp = 1000) => new()
        {
            Header = new Header
            {
                PrevHash = UInt256.Zero,
                MerkleRoot = UInt256.Zero,
                Index = 100,
                Timestamp = timestamp,
                NextConsensus = UInt160.Zero,
                Witness = Witness.Empty
            },
            Transactions = []
        };

        private static UInt160 H(byte value) => new(Enumerable.Repeat(value, UInt160.Length).ToArray());

        private static UInt256 H256(byte value) => new(Enumerable.Repeat(value, UInt256.Length).ToArray());

        private static ContractParameter Hash160(UInt160 value) => new(ContractParameterType.Hash160) { Value = value };

        private static ContractParameter Hash256(UInt256 value) => new(ContractParameterType.Hash256) { Value = value };

        private static ContractParameter PublicKey(ECPoint value) => new(ContractParameterType.PublicKey) { Value = value };

        private static ContractParameter PublicKeyArray(params ECPoint[] values) =>
            new(ContractParameterType.Array) { Value = values.Select(PublicKey).ToList() };

        private static ContractParameter Integer(long value) => new(ContractParameterType.Integer) { Value = value };

        private static ContractParameter Integer(ulong value) => new(ContractParameterType.Integer) { Value = value };

        private static ContractParameter Boolean(bool value) => new(ContractParameterType.Boolean) { Value = value };

        private static ContractParameter Bytes(byte[] value) => new(ContractParameterType.ByteArray) { Value = value };

        private static ContractParameter ArrayParam(params ContractParameter[] value) =>
            new(ContractParameterType.Array) { Value = value.ToList() };

        private static NativeContract NeoHubSequencerBond() => NativeContract.NeoHubSequencerBond;

        private static NativeContract NeoHubSequencerRegistry() => NativeContract.NeoHubSequencerRegistry;

        private static NativeContract NeoHubForcedInclusion() => NativeContract.NeoHubForcedInclusion;

        private static NativeContract NeoHubOptimisticChallenge() => NativeContract.NeoHubOptimisticChallenge;

        private static NativeContract NeoHubGovernanceFraudVerifier() => NativeContract.NeoHubGovernanceFraudVerifier;

        private static NativeContract NeoHubRestrictedExecutionFraudVerifier() => NativeContract.NeoHubRestrictedExecutionFraudVerifier;

        private static UInt160 AsUInt160(StackItem item) => new(item.GetSpan());

        private static UInt256 AsUInt256(StackItem item) => new(item.GetSpan());

        private static KeyPair Key(byte seed)
        {
            var bytes = Enumerable.Repeat(seed, 32).ToArray();
            return new KeyPair(bytes);
        }

        private static byte[] DAAttestationProof(byte[] message, params KeyPair[] keys)
        {
            return DAAttestationProof(message, keys.Select((key, index) => ((byte)index, key)).ToArray());
        }

        private static byte[] DAAttestationProof(byte[] message, params (byte SignerIndex, KeyPair Key)[] signers)
        {
            var proof = new byte[2 + signers.Length * 65];
            proof[0] = (byte)signers.Length;
            proof[1] = (byte)(signers.Length >> 8);
            for (var i = 0; i < signers.Length; i++)
            {
                var offset = 2 + i * 65;
                proof[offset] = signers[i].SignerIndex;
                Crypto.Sign(message, signers[i].Key.PrivateKey, ECCurve.Secp256r1, Neo.Cryptography.HashAlgorithm.SHA256)
                    .CopyTo(proof, offset + 1);
            }
            return proof;
        }

        private static byte[] DAAttestationMessage(uint chainId, ulong batchNumber, UInt256 commitment, byte daMode)
        {
            var bytes = new byte[49];
            var offset = 0;
            bytes[offset++] = 0x4e;
            bytes[offset++] = 0x34;
            bytes[offset++] = 0x44;
            bytes[offset++] = 0x41;
            WriteU32(bytes, offset, chainId);
            offset += 4;
            WriteU64(bytes, offset, batchNumber);
            offset += 8;
            commitment.ToArray().CopyTo(bytes, offset);
            offset += UInt256.Length;
            bytes[offset] = daMode;
            return bytes;
        }

        private static ContractState AlwaysTrueContract()
        {
            using var script = new ScriptBuilder();
            const int verifyOffset = 0;
            script.Emit(OpCode.DROP);
            script.EmitPush(true);
            script.Emit(OpCode.RET);
            var validateOffset = script.ToArray().Length;
            script.Emit(OpCode.DROP);
            script.Emit(OpCode.DROP);
            script.Emit(OpCode.DROP);
            script.Emit(OpCode.DROP);
            script.EmitPush(true);
            script.Emit(OpCode.RET);

            var manifest = Neo.UnitTests.TestUtils.CreateDefaultManifest();
            manifest.Name = "NeoHubAlwaysTrue";
            manifest.Abi.Methods =
            [
                new ContractMethodDescriptor
                {
                    Name = "verify",
                    Parameters =
                    [
                        new ContractParameterDefinition { Name = "commitmentBytes", Type = ContractParameterType.ByteArray }
                    ],
                    ReturnType = ContractParameterType.Boolean,
                    Offset = verifyOffset,
                    Safe = true
                },
                new ContractMethodDescriptor
                {
                    Name = "validate",
                    Parameters =
                    [
                        new ContractParameterDefinition { Name = "chainId", Type = ContractParameterType.Integer },
                        new ContractParameterDefinition { Name = "batchNumber", Type = ContractParameterType.Integer },
                        new ContractParameterDefinition { Name = "daCommitment", Type = ContractParameterType.Hash256 },
                        new ContractParameterDefinition { Name = "daMode", Type = ContractParameterType.Integer }
                    ],
                    ReturnType = ContractParameterType.Boolean,
                    Offset = validateOffset,
                    Safe = true
                }
            ];
            return Neo.UnitTests.TestUtils.GetContract(script.ToArray(), manifest);
        }

        private static ContractState TransferTokenContract()
        {
            using var script = new ScriptBuilder();
            script.Emit(OpCode.DROP);
            script.Emit(OpCode.DROP);
            script.Emit(OpCode.DROP);
            script.Emit(OpCode.DROP);
            script.EmitPush(true);
            script.Emit(OpCode.RET);

            var manifest = Neo.UnitTests.TestUtils.CreateDefaultManifest();
            manifest.Name = "NeoHubTransferToken";
            manifest.Abi.Methods =
            [
                new ContractMethodDescriptor
                {
                    Name = "transfer",
                    Parameters =
                    [
                        new ContractParameterDefinition { Name = "from", Type = ContractParameterType.Hash160 },
                        new ContractParameterDefinition { Name = "to", Type = ContractParameterType.Hash160 },
                        new ContractParameterDefinition { Name = "amount", Type = ContractParameterType.Integer },
                        new ContractParameterDefinition { Name = "data", Type = ContractParameterType.Any }
                    ],
                    ReturnType = ContractParameterType.Boolean,
                    Offset = 0,
                    Safe = false
                }
            ];
            return Neo.UnitTests.TestUtils.GetContract(script.ToArray(), manifest);
        }

        private static ContractState FraudVerifierContract(bool result)
        {
            using var script = new ScriptBuilder();
            script.Emit(OpCode.DROP);
            script.Emit(OpCode.DROP);
            script.Emit(OpCode.DROP);
            script.EmitPush(result);
            script.Emit(OpCode.RET);

            var manifest = Neo.UnitTests.TestUtils.CreateDefaultManifest();
            manifest.Name = result ? "NeoHubFraudVerifierTrue" : "NeoHubFraudVerifierFalse";
            manifest.Abi.Methods =
            [
                new ContractMethodDescriptor
                {
                    Name = "verifyFraud",
                    Parameters =
                    [
                        new ContractParameterDefinition { Name = "chainId", Type = ContractParameterType.Integer },
                        new ContractParameterDefinition { Name = "batchNumber", Type = ContractParameterType.Integer },
                        new ContractParameterDefinition { Name = "fraudProofBytes", Type = ContractParameterType.ByteArray }
                    ],
                    ReturnType = ContractParameterType.Boolean,
                    Offset = 0,
                    Safe = true
                }
            ];
            return Neo.UnitTests.TestUtils.GetContract(script.ToArray(), manifest);
        }

        private static StackItem CallAsScript(
            NativeContract contract,
            DataCache snapshot,
            UInt160 callingScriptHash,
            Block block,
            string method,
            params ContractParameter[] args)
        {
            using var engine = ApplicationEngine.Create(TriggerType.Application,
                new Nep17NativeContractExtensions.ManualWitness(callingScriptHash), snapshot, block,
                settings: TestProtocolSettings.Default);
            using var script = new ScriptBuilder();
            script.EmitDynamicCall(contract.Hash, method, args);
            engine.LoadScript(script.ToArray(), configureState: state =>
            {
                state.NativeCallingScriptHash = callingScriptHash;
                state.ScriptHash = callingScriptHash;
            });

            if (engine.Execute() != VM.VMState.HALT)
                throw engine.FaultException;

            return engine.ResultStack.Count > 0 ? engine.ResultStack.Pop() : StackItem.Null;
        }

        private static byte[] ChainConfig(
            uint chainId,
            byte securityLevel,
            byte daMode,
            bool gatewayEnabled,
            bool permissionlessExit,
            byte sequencerModel,
            byte exitModel,
            bool active)
        {
            var bytes = new byte[NeoHubChainRegistryContract.ConfigSize];
            WriteU32(bytes, 0, chainId);
            H(0x51).ToArray().CopyTo(bytes, 4);
            H(0x52).ToArray().CopyTo(bytes, 24);
            H(0x53).ToArray().CopyTo(bytes, 44);
            H(0x54).ToArray().CopyTo(bytes, 64);
            bytes[NeoHubChainRegistryContract.OffsetSecurityLevel] = securityLevel;
            bytes[NeoHubChainRegistryContract.OffsetDAMode] = daMode;
            bytes[NeoHubChainRegistryContract.OffsetGatewayEnabled] = gatewayEnabled ? (byte)1 : (byte)0;
            bytes[NeoHubChainRegistryContract.OffsetPermissionlessExit] = permissionlessExit ? (byte)1 : (byte)0;
            bytes[NeoHubChainRegistryContract.OffsetSequencerModel] = sequencerModel;
            bytes[NeoHubChainRegistryContract.OffsetExitModel] = exitModel;
            bytes[^1] = active ? (byte)1 : (byte)0;
            return bytes;
        }

        private static byte[] AssetMapping(UInt160 l1Asset, uint chainId, UInt160 l2Asset, bool active)
        {
            var bytes = new byte[NeoHubTokenRegistryContract.MappingSize];
            l1Asset.ToArray().CopyTo(bytes, 0);
            WriteU32(bytes, 20, chainId);
            l2Asset.ToArray().CopyTo(bytes, 24);
            bytes[44] = 1;
            bytes[45] = 1;
            bytes[46] = 0;
            bytes[47] = active ? (byte)1 : (byte)0;
            return bytes;
        }

        private static byte[] BatchCommitment(uint chainId, ulong batchNumber, byte proofType)
        {
            return BatchCommitment(chainId, batchNumber, proofType, H256(0x85), H256(0x86), H256(0x84));
        }

        private static byte[] BatchCommitment(
            uint chainId,
            ulong batchNumber,
            byte proofType,
            UInt256 postStateRoot,
            UInt256 withdrawalRoot,
            UInt256 daCommitment)
        {
            var bytes = new byte[317];
            WriteU32(bytes, 0, chainId);
            WriteU64(bytes, 4, batchNumber);
            postStateRoot.ToArray().CopyTo(bytes, 60);
            withdrawalRoot.ToArray().CopyTo(bytes, 156);
            daCommitment.ToArray().CopyTo(bytes, 252);
            bytes[316] = proofType;
            return bytes;
        }

        private static byte[] GovernanceFraudPayload(byte version, UInt256 claimedRoot, UInt256 replayedRoot, byte[] disputedTxBytes = null)
        {
            disputedTxBytes ??= [];
            var length = version == 2 ? 105 + disputedTxBytes.Length : 101;
            var bytes = new byte[length];
            bytes[0] = version;
            H256(0xc0).ToArray().CopyTo(bytes, 1);
            claimedRoot.ToArray().CopyTo(bytes, 33);
            replayedRoot.ToArray().CopyTo(bytes, 65);
            WriteU32(bytes, 97, 3);
            if (version == 2)
            {
                WriteU32(bytes, 101, (uint)disputedTxBytes.Length);
                disputedTxBytes.CopyTo(bytes, 105);
            }
            return bytes;
        }

        private static byte[] RestrictedFraudPayload(
            UInt256 preRoot,
            UInt256 claimedRoot,
            UInt256 replayedRoot,
            byte[] disputedTxBytes,
            byte[] key,
            byte[] preValue,
            byte[] postValue,
            ulong leafIndex,
            UInt256[] preSiblings,
            UInt256[] postSiblings)
        {
            var proofLength = 2 + key.Length + 4 + preValue.Length + 4 + postValue.Length + 8
                + 1 + UInt256.Length * preSiblings.Length
                + 1 + UInt256.Length * postSiblings.Length;
            var bytes = new byte[NeoHubRestrictedExecutionFraudVerifierContract.V2HeaderSize
                + disputedTxBytes.Length + 4 + proofLength];
            var offset = 0;
            bytes[offset++] = NeoHubRestrictedExecutionFraudVerifierContract.SupportedVersion3;
            preRoot.ToArray().CopyTo(bytes, offset);
            offset += UInt256.Length;
            claimedRoot.ToArray().CopyTo(bytes, offset);
            offset += UInt256.Length;
            replayedRoot.ToArray().CopyTo(bytes, offset);
            offset += UInt256.Length;
            WriteU32(bytes, offset, 0);
            offset += 4;
            WriteU32(bytes, offset, (uint)disputedTxBytes.Length);
            offset += 4;
            disputedTxBytes.CopyTo(bytes, offset);
            offset += disputedTxBytes.Length;
            WriteU32(bytes, offset, 1);
            offset += 4;
            WriteU16(bytes, offset, (ushort)key.Length);
            offset += 2;
            key.CopyTo(bytes, offset);
            offset += key.Length;
            WriteU32(bytes, offset, (uint)preValue.Length);
            offset += 4;
            preValue.CopyTo(bytes, offset);
            offset += preValue.Length;
            WriteU32(bytes, offset, (uint)postValue.Length);
            offset += 4;
            postValue.CopyTo(bytes, offset);
            offset += postValue.Length;
            WriteU64(bytes, offset, leafIndex);
            offset += 8;
            bytes[offset++] = (byte)preSiblings.Length;
            foreach (var sibling in preSiblings)
            {
                sibling.ToArray().CopyTo(bytes, offset);
                offset += UInt256.Length;
            }
            bytes[offset++] = (byte)postSiblings.Length;
            foreach (var sibling in postSiblings)
            {
                sibling.ToArray().CopyTo(bytes, offset);
                offset += UInt256.Length;
            }
            return bytes;
        }

        private static UInt256 RestrictedHashEntry(byte[] key, byte[] value)
        {
            var bytes = new byte[4 + key.Length + 4 + value.Length];
            WriteU32(bytes, 0, (uint)key.Length);
            key.CopyTo(bytes, 4);
            WriteU32(bytes, 4 + key.Length, (uint)value.Length);
            value.CopyTo(bytes, 8 + key.Length);
            return new UInt256(Crypto.Hash256(bytes));
        }

        private static byte[] AppendByte(byte[] source, byte value)
        {
            var bytes = new byte[source.Length + 1];
            source.CopyTo(bytes, 0);
            bytes[^1] = value;
            return bytes;
        }

        private static StorageKey SettlementKey(byte prefix, uint chainId, ulong batchNumber)
        {
            var key = new byte[12];
            WriteU32(key, 0, chainId);
            WriteU64(key, 4, batchNumber);
            return StorageKey.Create(NativeContract.NeoHubSettlementManager.Id, prefix, key);
        }

        private static StorageKey SettlementKey(byte prefix, uint chainId)
        {
            var key = new byte[4];
            WriteU32(key, 0, chainId);
            return StorageKey.Create(NativeContract.NeoHubSettlementManager.Id, prefix, key);
        }

        private static UInt256 HashPair(UInt256 left, UInt256 right)
        {
            var bytes = new byte[UInt256.Length * 2];
            left.ToArray().CopyTo(bytes, 0);
            right.ToArray().CopyTo(bytes, UInt256.Length);
            return new UInt256(Crypto.Hash256(bytes));
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
            var bytes = new byte[20 + 20 + 20 + 20 + 4 + amountBytes.Length + 8];
            var offset = 0;
            emittingContract.ToArray().CopyTo(bytes, offset);
            offset += UInt160.Length;
            l2Sender.ToArray().CopyTo(bytes, offset);
            offset += UInt160.Length;
            l1Recipient.ToArray().CopyTo(bytes, offset);
            offset += UInt160.Length;
            l2Asset.ToArray().CopyTo(bytes, offset);
            offset += UInt160.Length;
            WriteU32(bytes, offset, (uint)amountBytes.Length);
            offset += 4;
            amountBytes.CopyTo(bytes, offset);
            offset += amountBytes.Length;
            WriteU64(bytes, offset, nonce);
            return new UInt256(Crypto.Hash256(bytes));
        }

        private static byte[] ToUnsignedLittleEndian(BigInteger value)
        {
            var raw = value.ToByteArray();
            var length = raw.Length;
            while (length > 1 && raw[length - 1] == 0) length--;
            return raw.Take(length).ToArray();
        }

        private static uint ReadU32(ReadOnlySpan<byte> bytes)
        {
            return bytes[0]
                | ((uint)bytes[1] << 8)
                | ((uint)bytes[2] << 16)
                | ((uint)bytes[3] << 24);
        }

        private static ulong ReadU64(ReadOnlySpan<byte> bytes)
        {
            return bytes[0]
                | ((ulong)bytes[1] << 8)
                | ((ulong)bytes[2] << 16)
                | ((ulong)bytes[3] << 24)
                | ((ulong)bytes[4] << 32)
                | ((ulong)bytes[5] << 40)
                | ((ulong)bytes[6] << 48)
                | ((ulong)bytes[7] << 56);
        }

        private static void WriteU16(byte[] bytes, int offset, ushort value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
        }

        private static void WriteU32(byte[] bytes, int offset, uint value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
            bytes[offset + 2] = (byte)(value >> 16);
            bytes[offset + 3] = (byte)(value >> 24);
        }

        private static void WriteU64(byte[] bytes, int offset, ulong value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
            bytes[offset + 2] = (byte)(value >> 16);
            bytes[offset + 3] = (byte)(value >> 24);
            bytes[offset + 4] = (byte)(value >> 32);
            bytes[offset + 5] = (byte)(value >> 40);
            bytes[offset + 6] = (byte)(value >> 48);
            bytes[offset + 7] = (byte)(value >> 56);
        }
    }
}
