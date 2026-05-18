using Neo.Extensions.VM;
using Neo.SmartContract.Native;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.SmartContract;
using Neo.UnitTests.Extensions;
using Neo.VM;
using Neo.VM.Types;
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

    [TestMethod]
    public void L2Bridge_UsesNativeBridgedNep17AssetIdForDepositAndWithdrawal()
    {
        var snapshot = TestBlockchain.GetTestSnapshotCache().CloneCache();
        var block = CreatePersistingBlock();
        var committee = NativeContract.Governance.GetCommitteeAddress(snapshot);
        var owner = UInt160.Parse("0x0101010101010101010101010101010101010101");
        var systemAccount = UInt160.Parse("0x0202020202020202020202020202020202020202");
        var l1Asset = UInt160.Parse("0x0303030303030303030303030303030303030303");
        var l1Recipient = UInt160.Parse("0x0404040404040404040404040404040404040404");
        var l2User = UInt160.Parse("0x0505050505050505050505050505050505050505");

        NativeContract.L2Bridge.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), block,
            "configure", Hash160(owner), Hash160(systemAccount));
        NativeContract.BridgedNep17.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), block,
            "configure", Hash160(owner), Hash160(NativeContract.L2Bridge.Hash));

        var asset = (ByteString)NativeContract.BridgedNep17.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
            "createBridgedToken", Text("Wrapped GAS"), Text("WGAS"), Integer(8), Hash160(l1Asset), Integer(1_000_000))!;
        var l2Asset = new UInt160(asset.GetSpan());

        NativeContract.L2Bridge.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
            "registerMapping", Hash160(l1Asset), Hash160(l2Asset));
        NativeContract.L2Bridge.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(systemAccount), block,
            "applyDeposit", Integer(0), Integer(1), Hash160(l1Asset), Hash160(l2User), Integer(100));

        Assert.AreEqual(100, BalanceOf(snapshot, l2Asset, l2User));

        CallAsScript(NativeContract.L2Bridge, snapshot, l2User, new Nep17NativeContractExtensions.ManualWitness(l2User), block,
            "initiateWithdrawal", Hash160(l2Asset), Integer(40), Hash160(l1Recipient));

        Assert.AreEqual(60, BalanceOf(snapshot, l2Asset, l2User));
        Assert.AreEqual(60, NativeContract.TokenManagement.GetTokenInfo(snapshot, l2Asset)!.TotalSupply);
    }

    [TestMethod]
    public void L2Fee_DistributeRequiresSuccessfulFeeAssetTransfers()
    {
        var snapshot = TestBlockchain.GetTestSnapshotCache().CloneCache();
        var block = CreatePersistingBlock();
        var committee = NativeContract.Governance.GetCommitteeAddress(snapshot);
        var owner = UInt160.Parse("0x0606060606060606060606060606060606060606");
        var sequencer = UInt160.Parse("0x0707070707070707070707070707070707070707");
        var prover = UInt160.Parse("0x0808080808080808080808080808080808080808");
        var da = UInt160.Parse("0x0909090909090909090909090909090909090909");

        NativeContract.L2Fee.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), block,
            "configure", Hash160(owner), Hash160(NativeContract.Governance.GasTokenId), Hash160(sequencer), Hash160(prover), Hash160(da),
            Integer(5_000), Integer(3_000), Integer(2_000));

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            NativeContract.L2Fee.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "distribute", Integer(10)));
    }

    [TestMethod]
    public void L2ExternalBridge_MintsAndBurnsNativeBridgedNep17AssetIds()
    {
        var snapshot = TestBlockchain.GetTestSnapshotCache().CloneCache();
        var block = CreatePersistingBlock();
        var committee = NativeContract.Governance.GetCommitteeAddress(snapshot);
        var owner = UInt160.Parse("0x1111111111111111111111111111111111111111");
        var systemAccount = UInt160.Parse("0x1212121212121212121212121212121212121212");
        var foreignAsset = UInt160.Parse("0x1313131313131313131313131313131313131313");
        var l2User = UInt160.Parse("0x1414141414141414141414141414141414141414");
        var externalRecipient = UInt160.Parse("0x1515151515151515151515151515151515151515");
        var unregisteredForeignAsset = UInt160.Parse("0x1616161616161616161616161616161616161616");
        const uint bscMainnet = 0xe0000038;

        NativeContract.L2NativeExternalBridge.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), block,
            "configure", Hash160(owner), Hash160(systemAccount));
        NativeContract.BridgedNep17.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), block,
            "configure", Hash160(owner), Hash160(NativeContract.L2Bridge.Hash));
        NativeContract.BridgedNep17.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
            "authorizeBridge", Hash160(NativeContract.L2NativeExternalBridge.Hash), Boolean(true));

        var asset = (ByteString)NativeContract.BridgedNep17.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
            "createBridgedToken", Text("BSC USDT"), Text("BUSDT"), Integer(6), Hash160(foreignAsset), Integer(1_000_000))!;
        var l2Asset = new UInt160(asset.GetSpan());
        var unregisteredAsset = (ByteString)NativeContract.BridgedNep17.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
            "createBridgedToken", Text("BSC DAI"), Text("BDAI"), Integer(18), Hash160(unregisteredForeignAsset), Integer(1_000_000))!;
        var unregisteredL2Asset = new UInt160(unregisteredAsset.GetSpan());

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            NativeContract.L2NativeExternalBridge.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(systemAccount), block,
                "applyInbound", Integer(bscMainnet), Integer(1), Hash160(foreignAsset), Hash160(l2User), Hash160(unregisteredL2Asset), Integer(1)));

        NativeContract.L2NativeExternalBridge.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
            "registerAssetMapping", Integer(bscMainnet), Hash160(foreignAsset), Hash160(l2Asset));
        Assert.IsTrue(NativeContract.L2NativeExternalBridge.IsL2AssetRegistered(snapshot, bscMainnet, l2Asset));
        NativeContract.L2NativeExternalBridge.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(systemAccount), block,
            "applyInbound", Integer(bscMainnet), Integer(1), Hash160(foreignAsset), Hash160(l2User), Hash160(l2Asset), Integer(250));

        Assert.AreEqual(250, BalanceOf(snapshot, l2Asset, l2User));

        CallAsScript(NativeContract.L2NativeExternalBridge, snapshot, l2User, new Nep17NativeContractExtensions.ManualWitness(l2User), block,
            "send", Integer(bscMainnet), Hash160(externalRecipient), Hash160(l2Asset), Integer(75), ByteArray([]), Integer(0));

        Assert.AreEqual(175, BalanceOf(snapshot, l2Asset, l2User));
        Assert.AreEqual(175, NativeContract.TokenManagement.GetTokenInfo(snapshot, l2Asset)!.TotalSupply);
        Assert.AreEqual(1ul, NativeContract.L2NativeExternalBridge.GetLastOutboundNonce(snapshot, bscMainnet));
    }

    private static ContractParameter Hash160(UInt160 value)
    {
        return new ContractParameter(ContractParameterType.Hash160) { Value = value };
    }

    private static ContractParameter Integer(BigInteger value)
    {
        return new ContractParameter(ContractParameterType.Integer) { Value = value };
    }

    private static ContractParameter Text(string value)
    {
        return new ContractParameter(ContractParameterType.String) { Value = value };
    }

    private static ContractParameter ByteArray(byte[] value)
    {
        return new ContractParameter(ContractParameterType.ByteArray) { Value = value };
    }

    private static ContractParameter Boolean(bool value)
    {
        return new ContractParameter(ContractParameterType.Boolean) { Value = value };
    }

    private static BigInteger BalanceOf(DataCache snapshot, UInt160 assetId, UInt160 account)
    {
        return NativeContract.BridgedNep17.Call(snapshot, "balanceOf", Hash160(assetId), Hash160(account))!.GetInteger();
    }

    private static StackItem? CallAsScript(NativeContract contract, DataCache snapshot, UInt160 callingScriptHash,
        IVerifiable? container, Block block, string method, params ContractParameter[] args)
    {
        using var engine = ApplicationEngine.Create(TriggerType.Application, container, snapshot, block, settings: TestProtocolSettings.Default);
        using var script = new ScriptBuilder();
        script.EmitDynamicCall(contract.Hash, method, args);
        var context = engine.LoadScript(script.ToArray());
        context.GetState<ExecutionContextState>().ScriptHash = callingScriptHash;

        if (engine.Execute() != VMState.HALT)
        {
            var exception = engine.FaultException!;
            while (exception.InnerException is not null) exception = exception.InnerException;
            throw exception;
        }

        return engine.ResultStack.Count > 0 ? engine.ResultStack.Pop() : null;
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
