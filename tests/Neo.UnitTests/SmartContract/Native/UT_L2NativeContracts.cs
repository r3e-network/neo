using Neo.SmartContract.Native;
using Neo.Network.P2P.Payloads;
using Neo.SmartContract;
using Neo.UnitTests.Extensions;
using System.Numerics;

namespace Neo.UnitTests.SmartContract.Native;

[TestClass]
public class UT_L2NativeContracts
{
    private static readonly string[] ExpectedNames =
    [
        "L2BridgeContract",
        "L2MessageContract",
        "L2BatchInfoContract",
        "L2FeeContract",
        "L2PaymasterContract",
        "L2SystemConfigContract",
        "L2NativeExternalBridgeContract",
        "BridgedNep17Contract",
        "L2AccountAbstraction",
        "L2InteropVerifier",
    ];

    [TestMethod]
    public void AllN4L2Contracts_AreRegisteredAsNativeContracts()
    {
        var byName = NativeContract.Contracts.ToDictionary(c => c.Name);

        foreach (var name in ExpectedNames)
        {
            Assert.IsTrue(byName.TryGetValue(name, out var contract), $"{name} is not registered as a native contract.");
            Assert.IsTrue(NativeContract.IsNative(contract.Hash), $"{name} hash is not recognized as native.");
            Assert.IsLessThan(0, contract.Id, $"{name} must use a negative native-contract id.");
        }
    }

    [TestMethod]
    public void AllN4L2Contracts_HaveNativeManifestsWithoutDeploymentEntrypoints()
    {
        var byName = NativeContract.Contracts.ToDictionary(c => c.Name);

        foreach (var name in ExpectedNames)
        {
            var contract = byName[name];
            var manifest = contract.GetContractState(ProtocolSettings.Default, 0).Manifest;
            var methodNames = manifest.Abi.Methods.Select(m => m.Name).ToArray();

            CollectionAssert.DoesNotContain(methodNames, "_deploy", $"{name} must not be a later-deployed devpack contract.");
            CollectionAssert.DoesNotContain(methodNames, "deploy", $"{name} must not expose a deployment method.");
            CollectionAssert.DoesNotContain(methodNames, "update", $"{name} must not expose an update method.");
        }
    }

    [TestMethod]
    public void L2BatchInfo_UsesNativeAuthorizationAndStorage()
    {
        var snapshot = TestBlockchain.GetTestSnapshotCache().CloneCache();
        var block = CreatePersistingBlock();
        var committee = NativeContract.Governance.GetCommitteeAddress(snapshot);
        var owner = UInt160.Parse("0x0101010101010101010101010101010101010101");
        var systemAccount = UInt160.Parse("0x0202020202020202020202020202020202020202");

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            NativeContract.L2BatchInfo.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "configure", Hash160(owner), Hash160(systemAccount), Integer(1099)));

        NativeContract.L2BatchInfo.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), block,
            "configure", Hash160(owner), Hash160(systemAccount), Integer(1099));

        Assert.AreEqual(1099u, NativeContract.L2BatchInfo.GetChainId(snapshot));
        Assert.AreEqual(0ul, NativeContract.L2BatchInfo.GetBatchNumber(snapshot));
        Assert.AreEqual(0u, NativeContract.L2BatchInfo.GetL1FinalizedHeight(snapshot));

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            NativeContract.L2BatchInfo.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "advance", Integer(1), Integer(50)));

        NativeContract.L2BatchInfo.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(systemAccount), block,
            "advance", Integer(1), Integer(50));

        Assert.AreEqual(1ul, NativeContract.L2BatchInfo.GetBatchNumber(snapshot));
        Assert.AreEqual(50u, NativeContract.L2BatchInfo.GetL1FinalizedHeight(snapshot));

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            NativeContract.L2BatchInfo.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(systemAccount), block,
                "advance", Integer(3), Integer(60)));
    }

    private static ContractParameter Hash160(UInt160 value)
    {
        return new ContractParameter(ContractParameterType.Hash160) { Value = value };
    }

    private static ContractParameter Integer(BigInteger value)
    {
        return new ContractParameter(ContractParameterType.Integer) { Value = value };
    }

    private static Block CreatePersistingBlock()
    {
        return new Block
        {
            Header = new Header
            {
                Index = 1,
                MerkleRoot = UInt256.Zero,
                NextConsensus = UInt160.Zero,
                PrevHash = UInt256.Zero,
                Witness = Witness.Empty,
            },
            Transactions = []
        };
    }
}
