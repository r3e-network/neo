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
            NativeContract.NeoHubDAValidator
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

        private static Block Block() => new()
        {
            Header = new Header
            {
                PrevHash = UInt256.Zero,
                MerkleRoot = UInt256.Zero,
                Index = 100,
                Timestamp = 1000,
                NextConsensus = UInt160.Zero,
                Witness = Witness.Empty
            },
            Transactions = []
        };

        private static UInt160 H(byte value) => new(Enumerable.Repeat(value, UInt160.Length).ToArray());

        private static UInt256 H256(byte value) => new(Enumerable.Repeat(value, UInt256.Length).ToArray());

        private static ContractParameter Hash160(UInt160 value) => new(ContractParameterType.Hash160) { Value = value };

        private static ContractParameter Hash256(UInt256 value) => new(ContractParameterType.Hash256) { Value = value };

        private static ContractParameter Integer(long value) => new(ContractParameterType.Integer) { Value = value };

        private static ContractParameter Integer(ulong value) => new(ContractParameterType.Integer) { Value = value };

        private static ContractParameter Boolean(bool value) => new(ContractParameterType.Boolean) { Value = value };

        private static ContractParameter Bytes(byte[] value) => new(ContractParameterType.ByteArray) { Value = value };

        private static ContractParameter ArrayParam(params ContractParameter[] value) =>
            new(ContractParameterType.Array) { Value = value.ToList() };

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

        private static StorageKey SettlementKey(byte prefix, uint chainId, ulong batchNumber)
        {
            var key = new byte[12];
            WriteU32(key, 0, chainId);
            WriteU64(key, 4, batchNumber);
            return StorageKey.Create(NativeContract.NeoHubSettlementManager.Id, prefix, key);
        }

        private static UInt256 HashPair(UInt256 left, UInt256 right)
        {
            var bytes = new byte[UInt256.Length * 2];
            left.ToArray().CopyTo(bytes, 0);
            right.ToArray().CopyTo(bytes, UInt256.Length);
            return new UInt256(Crypto.Hash256(bytes));
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
