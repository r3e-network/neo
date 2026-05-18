// Copyright (C) 2015-2026 The Neo Project.
//
// NeoHub native-contract tests are maintained by r3e-network in the
// r3e/neo-n3-core branch.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.Extensions;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using Neo.UnitTests.Extensions;
using Neo.VM.Types;
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
            NativeContract.NeoHubL1TxFilter
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

        private static UInt160 AsUInt160(StackItem item) => new(item.GetSpan());

        private static UInt256 AsUInt256(StackItem item) => new(item.GetSpan());

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

        private static void WriteU32(byte[] bytes, int offset, uint value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
            bytes[offset + 2] = (byte)(value >> 16);
            bytes[offset + 3] = (byte)(value >> 24);
        }
    }
}
