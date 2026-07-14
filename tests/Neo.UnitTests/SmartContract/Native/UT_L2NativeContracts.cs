// Copyright (C) 2015-2026 The Neo Project.
//
// UT_L2NativeContracts.cs file belongs to the neo project and is free
// software distributed under the MIT software license, see the
// accompanying file LICENSE in the main directory of the
// repository or http://www.opensource.org/licenses/mit-license.php
// for more details.
//
// Redistribution and use in source and binary forms with or without
// modifications are permitted.

using Neo.Cryptography;
using Neo.Cryptography.ECC;
using Neo.Extensions.VM;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using Neo.UnitTests.Extensions;
using Neo.VM;
using Neo.VM.Types;
using Neo.Wallets;
using System.Buffers.Binary;
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
    public void L2SystemConfig_SequencerValidatorsDriveNativeDbftSelector()
    {
        var snapshot = TestBlockchain.GetTestSnapshotCache().CloneCache();
        var block = CreatePersistingBlock();
        var committee = NativeContract.Governance.GetCommitteeAddress(snapshot);
        var owner = UInt160.Parse("0x0101010101010101010101010101010101010101");
        var systemAccount = UInt160.Parse("0x0202020202020202020202020202020202020202");
        var validators = Enumerable.Range(1, TestProtocolSettings.Default.ValidatorsCount)
            .Select(static index => new KeyPair(Enumerable.Repeat((byte)(index + 64), 32).ToArray()).PublicKey)
            .ToArray();

        NativeContract.L2SystemConfig.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), block,
            "configure", Hash160(owner), Hash160(systemAccount), Integer(1099));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            NativeContract.L2SystemConfig.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "configure", Hash160(owner), Hash160(systemAccount), Integer(1100)));
        NativeContract.L2SystemConfig.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
            "setSequencerValidators", PublicKeys(validators));

        var expected = validators.OrderBy(static validator => validator).ToArray();
        var genesis = TestProtocolSettings.Default.StandbyValidators.OrderBy(static validator => validator).ToArray();
        CollectionAssert.AreEqual(genesis, NativeContract.L2SystemConfig.GetSequencerValidators(snapshot));
        CollectionAssert.AreEqual(expected, NativeContract.L2SystemConfig.GetPendingSequencerValidators(snapshot));
        CollectionAssert.AreEqual(
            genesis,
            NativeContract.NEO.GetNextBlockValidators(snapshot, TestProtocolSettings.Default.ValidatorsCount));
        CollectionAssert.AreEqual(
            expected,
            NativeContract.NEO.ComputeNextBlockValidators(snapshot, TestProtocolSettings.Default));
        Assert.AreNotEqual(Contract.GetBFTAddress(genesis), Contract.GetBFTAddress(expected));

        block.Header.Index = (uint)TestProtocolSettings.Default.CommitteeMembersCount;
        using var script = new ScriptBuilder();
        script.EmitSysCall(ApplicationEngine.System_Contract_NativeOnPersist);
        using var engine = ApplicationEngine.Create(
            TriggerType.OnPersist,
            null,
            snapshot,
            block,
            settings: TestProtocolSettings.Default);
        engine.LoadScript(script.ToArray());
        Assert.AreEqual(VMState.HALT, engine.Execute(), engine.FaultException?.ToString());

        CollectionAssert.AreEqual(expected, NativeContract.L2SystemConfig.GetSequencerValidators(snapshot));
        CollectionAssert.AreEqual(System.Array.Empty<ECPoint>(), NativeContract.L2SystemConfig.GetPendingSequencerValidators(snapshot));
        CollectionAssert.AreEqual(
            expected,
            NativeContract.NEO.GetNextBlockValidators(snapshot, TestProtocolSettings.Default.ValidatorsCount));
        Assert.AreSame(NativeContract.Governance, NativeContract.NEO);
        Assert.AreEqual(
            Governance.ShouldRefreshCommittee(21, 21),
            NeoToken.ShouldRefreshCommittee(21, 21));
    }

    [TestMethod]
    public void L2SystemConfig_SequencerValidatorsFailClosedOnInvalidUpdates()
    {
        var snapshot = TestBlockchain.GetTestSnapshotCache().CloneCache();
        var block = CreatePersistingBlock();
        var committee = NativeContract.Governance.GetCommitteeAddress(snapshot);
        var owner = UInt160.Parse("0x0101010101010101010101010101010101010101");
        var systemAccount = UInt160.Parse("0x0202020202020202020202020202020202020202");
        var validators = TestProtocolSettings.Default.StandbyCommittee
            .Take(TestProtocolSettings.Default.ValidatorsCount)
            .ToArray();

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            NativeContract.L2SystemConfig.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), block,
                "setSequencerValidators", PublicKeys(validators)));

        NativeContract.L2SystemConfig.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), block,
            "configure", Hash160(owner), Hash160(systemAccount), Integer(1099));

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            NativeContract.L2SystemConfig.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(systemAccount), block,
                "setSequencerValidators", PublicKeys(validators)));
        Assert.ThrowsExactly<ArgumentException>(() =>
            NativeContract.L2SystemConfig.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "setSequencerValidators", PublicKeys(validators[..^1])));

        var duplicate = validators.ToArray();
        duplicate[^1] = duplicate[0];
        Assert.ThrowsExactly<ArgumentException>(() =>
            NativeContract.L2SystemConfig.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "setSequencerValidators", PublicKeys(duplicate)));
    }

    [TestMethod]
    public void BridgedNep17_InitializesPlatformTokensAtGenesis()
    {
        var snapshot = TestBlockchain.GetTestSnapshotCache().CloneCache();

        AssertPlatformToken(NativeContract.BridgedNep17.L2NeoTokenId, "NEO", "NEO", 8, BigInteger.Parse("10000000000000000"));
        AssertPlatformToken(TokenManagement.GetAssetId(NativeContract.BridgedNep17.Hash, "USDT"), "USDT", "USDT", 6, BigInteger.MinusOne);
        AssertPlatformToken(TokenManagement.GetAssetId(NativeContract.BridgedNep17.Hash, "USDC"), "USDC", "USDC", 6, BigInteger.MinusOne);
        AssertPlatformToken(TokenManagement.GetAssetId(NativeContract.BridgedNep17.Hash, "BTC"), "BTC", "BTC", 8, BigInteger.Parse("2100000000000000"));

        void AssertPlatformToken(UInt160 assetId, string name, string symbol, byte decimals, BigInteger maxSupply)
        {
            var token = NativeContract.TokenManagement.GetTokenInfo(snapshot, assetId);
            Assert.IsNotNull(token, $"Every N4 L2 must expose built-in {symbol} metadata at genesis.");
            Assert.AreEqual(TokenType.Fungible, token.Type);
            Assert.AreEqual(NativeContract.BridgedNep17.Hash, token.Owner);
            Assert.AreEqual(name, token.Name);
            Assert.AreEqual(symbol, token.Symbol);
            Assert.AreEqual(decimals, token.Decimals);
            Assert.AreEqual(BigInteger.Zero, token.TotalSupply);
            Assert.AreEqual(maxSupply, token.MaxSupply);
        }
    }

    [TestMethod]
    public void L2MappedNeo_UsesDecimalizedNativeBridgeMetadata()
    {
        var snapshot = TestBlockchain.GetTestSnapshotCache().CloneCache();
        var block = CreatePersistingBlock();
        var committee = NativeContract.Governance.GetCommitteeAddress(snapshot);
        var owner = UInt160.Parse("0x0101010101010101010101010101010101010101");
        var l1Neo = UInt160.Parse("0x0909090909090909090909090909090909090909");

        NativeContract.BridgedNep17.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), block,
            "configure", Hash160(owner), Hash160(NativeContract.L2Bridge.Hash));
        var asset = (ByteString)NativeContract.BridgedNep17.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
            "createBridgedToken", Text("NEO"), Text("NEO"), Integer(8), Hash160(l1Neo), Integer(BigInteger.Parse("10000000000000000")))!;
        var l2Neo = new UInt160(asset.GetSpan());
        var neo = NativeContract.TokenManagement.GetTokenInfo(snapshot, l2Neo)!;
        var gas = NativeContract.TokenManagement.GetTokenInfo(snapshot, NativeContract.Governance.GasTokenId)!;

        Assert.AreEqual(NativeContract.BridgedNep17.L2NeoTokenId, l2Neo);
        Assert.AreEqual(l1Neo, NativeContract.BridgedNep17.GetL1Asset(snapshot, l2Neo));
        Assert.AreEqual((byte)0, Governance.NeoTokenDecimals);
        Assert.AreEqual((byte)8, Governance.GasTokenDecimals);
        Assert.AreEqual((byte)8, neo.Decimals);
        Assert.AreEqual((byte)8, gas.Decimals);
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
            "registerMapping", Hash160(l1Asset), Hash160(l2Asset), Integer(8), Integer(8));
        NativeContract.L2Bridge.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(systemAccount), block,
            "applyDeposit", Integer(0), Integer(1), Hash160(l1Asset), Hash160(l2User), Integer(100));

        Assert.AreEqual(100, BalanceOf(snapshot, l2Asset, l2User));

        CallAsScript(NativeContract.L2Bridge, snapshot, l2User, new Nep17NativeContractExtensions.ManualWitness(l2User), block,
            "initiateWithdrawal", Hash160(l2Asset), Integer(40), Hash160(l1Recipient));

        Assert.AreEqual(60, BalanceOf(snapshot, l2Asset, l2User));
        Assert.AreEqual(60, NativeContract.TokenManagement.GetTokenInfo(snapshot, l2Asset)!.TotalSupply);
    }

    [TestMethod]
    public void L2Bridge_ConvertsBetweenIndivisibleL1NeoAndDecimalizedL2Mapping()
    {
        var snapshot = TestBlockchain.GetTestSnapshotCache().CloneCache();
        var block = CreatePersistingBlock();
        var committee = NativeContract.Governance.GetCommitteeAddress(snapshot);
        var owner = UInt160.Parse("0x0101010101010101010101010101010101010101");
        var systemAccount = UInt160.Parse("0x0202020202020202020202020202020202020202");
        var l1Neo = UInt160.Parse("0x0909090909090909090909090909090909090909");
        var l1Recipient = UInt160.Parse("0x0404040404040404040404040404040404040404");
        var l2User = UInt160.Parse("0x0505050505050505050505050505050505050505");

        NativeContract.L2Bridge.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), block,
            "configure", Hash160(owner), Hash160(systemAccount));
        NativeContract.BridgedNep17.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), block,
            "configure", Hash160(owner), Hash160(NativeContract.L2Bridge.Hash));
        var asset = (ByteString)NativeContract.BridgedNep17.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
            "createBridgedToken", Text("NEO"), Text("NEO"), Integer(8), Hash160(l1Neo), Integer(BigInteger.Parse("10000000000000000")))!;
        var l2Neo = new UInt160(asset.GetSpan());
        NativeContract.L2Bridge.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
            "registerMapping", Hash160(l1Neo), Hash160(l2Neo), Integer(0), Integer(8));

        NativeContract.L2Bridge.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(systemAccount), block,
            "applyDeposit", Integer(0), Integer(1), Hash160(l1Neo), Hash160(l2User), Integer(2));

        Assert.AreEqual(new BigInteger(200_000_000), BalanceOf(snapshot, l2Neo, l2User));

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            CallAsScript(NativeContract.L2Bridge, snapshot, l2User, new Nep17NativeContractExtensions.ManualWitness(l2User), block,
                "initiateWithdrawal", Hash160(l2Neo), Integer(1), Hash160(l1Recipient)));

        CallAsScript(NativeContract.L2Bridge, snapshot, l2User, new Nep17NativeContractExtensions.ManualWitness(l2User), block,
            "initiateWithdrawal", Hash160(l2Neo), Integer(100_000_000), Hash160(l1Recipient));

        Assert.AreEqual(new BigInteger(100_000_000), BalanceOf(snapshot, l2Neo, l2User));
    }

    [TestMethod]
    public void L2Bridge_RejectsWrongPlatformDecimals()
    {
        var snapshot = TestBlockchain.GetTestSnapshotCache().CloneCache();
        var block = CreatePersistingBlock();
        var committee = NativeContract.Governance.GetCommitteeAddress(snapshot);
        var owner = UInt160.Parse("0x0101010101010101010101010101010101010101");
        var systemAccount = UInt160.Parse("0x0202020202020202020202020202020202020202");
        var l1Gas = UInt160.Parse("0x0808080808080808080808080808080808080808");
        var l1Neo = UInt160.Parse("0x0909090909090909090909090909090909090909");
        var l1Usdt = UInt160.Parse("0x0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a");
        var l1Usdc = UInt160.Parse("0x0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b");
        var l1Btc = UInt160.Parse("0x0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c");
        var l2Usdt = TokenManagement.GetAssetId(NativeContract.BridgedNep17.Hash, "USDT");
        var l2Usdc = TokenManagement.GetAssetId(NativeContract.BridgedNep17.Hash, "USDC");
        var l2Btc = TokenManagement.GetAssetId(NativeContract.BridgedNep17.Hash, "BTC");

        NativeContract.L2Bridge.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), block,
            "configure", Hash160(owner), Hash160(systemAccount));

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            NativeContract.L2Bridge.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "registerMapping", Hash160(l1Gas), Hash160(NativeContract.Governance.GasTokenId), Integer(0), Integer(8)));

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            NativeContract.L2Bridge.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "registerMapping", Hash160(l1Neo), Hash160(NativeContract.BridgedNep17.L2NeoTokenId), Integer(8), Integer(8)));

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            NativeContract.L2Bridge.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "registerMapping", Hash160(l1Usdt), Hash160(l2Usdt), Integer(8), Integer(8)));

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            NativeContract.L2Bridge.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "registerMapping", Hash160(l1Usdc), Hash160(l2Usdc), Integer(6), Integer(8)));

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            NativeContract.L2Bridge.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
                "registerMapping", Hash160(l1Btc), Hash160(l2Btc), Integer(6), Integer(6)));
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
        var foreignSender = UInt160.Parse("0x1717171717171717171717171717171717171717");
        var sourceTransaction = UInt256.Parse("0x1818181818181818181818181818181818181818181818181818181818181818");
        const uint bscMainnet = 0xe0000038;
        const uint neoChainId = 1099;

        NativeContract.L2NativeExternalBridge.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(committee), block,
            "configure", Hash160(owner), Hash160(systemAccount), Integer(neoChainId));
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

        NativeContract.L2NativeExternalBridge.Call(snapshot, new Nep17NativeContractExtensions.ManualWitness(owner), block,
            "registerAssetMapping", Integer(bscMainnet), Hash160(foreignAsset), Hash160(l2Asset));
        Assert.IsTrue(NativeContract.L2NativeExternalBridge.IsL2AssetRegistered(snapshot, bscMainnet, l2Asset));

        var messageBytes = ExternalPayoutMessage(
            bscMainnet, neoChainId, 1, foreignSender, foreignAsset, l2User, 250,
            sourceTransaction);
        var messageHash = new UInt256(Crypto.Hash256(messageBytes));
        var payoutTransaction = PayoutTransaction(systemAccount, 1);
        var strangerTransaction = PayoutTransaction(l2User, 2);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            NativeContract.L2NativeExternalBridge.Call(snapshot, strangerTransaction, block,
                "applyPayout", Integer(bscMainnet), Integer(neoChainId), Integer(1),
                Hash160(foreignAsset), Hash160(l2Asset), Hash160(l2User), Integer(250),
                Integer(0), Hash256(sourceTransaction), Hash256(messageHash), ByteArray(messageBytes)));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            NativeContract.L2NativeExternalBridge.Call(snapshot, payoutTransaction, block,
                "applyPayout", Integer(bscMainnet), Integer(neoChainId), Integer(1),
                Hash160(foreignAsset), Hash160(l2Asset), Hash160(l2User), Integer(251),
                Integer(0), Hash256(sourceTransaction), Hash256(messageHash), ByteArray(messageBytes)));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            NativeContract.L2NativeExternalBridge.Call(snapshot, payoutTransaction, block,
                "applyPayout", Integer(bscMainnet), Integer(neoChainId), Integer(1),
                Hash160(foreignAsset), Hash160(unregisteredL2Asset), Hash160(l2User), Integer(250),
                Integer(0), Hash256(sourceTransaction), Hash256(messageHash), ByteArray(messageBytes)));

        NativeContract.L2NativeExternalBridge.Call(snapshot, payoutTransaction, block,
            "applyPayout", Integer(bscMainnet), Integer(neoChainId), Integer(1),
            Hash160(foreignAsset), Hash160(l2Asset), Hash160(l2User), Integer(250),
            Integer(0), Hash256(sourceTransaction), Hash256(messageHash), ByteArray(messageBytes));

        Assert.AreEqual(250, BalanceOf(snapshot, l2Asset, l2User));
        Assert.AreEqual(messageHash,
            NativeContract.L2NativeExternalBridge.GetInboundMessageHash(snapshot, bscMainnet, 1));
        Assert.AreEqual(payoutTransaction.Hash,
            NativeContract.L2NativeExternalBridge.GetInboundTransactionHash(snapshot, bscMainnet, 1));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            NativeContract.L2NativeExternalBridge.Call(snapshot, payoutTransaction, block,
                "applyPayout", Integer(bscMainnet), Integer(neoChainId), Integer(1),
                Hash160(foreignAsset), Hash160(l2Asset), Hash160(l2User), Integer(250),
                Integer(0), Hash256(sourceTransaction), Hash256(messageHash), ByteArray(messageBytes)));
        Assert.AreEqual(250, BalanceOf(snapshot, l2Asset, l2User),
            "replay must not credit the recipient twice");

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

    private static ContractParameter Hash256(UInt256 value)
    {
        return new ContractParameter(ContractParameterType.Hash256) { Value = value };
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

    private static ContractParameter PublicKeys(IEnumerable<ECPoint> validators)
    {
        return new ContractParameter(ContractParameterType.Array)
        {
            Value = validators
                .Select(static validator => new ContractParameter(ContractParameterType.ByteArray)
                {
                    Value = validator.EncodePoint(true)
                })
                .ToList()
        };
    }

    private static ContractParameter Boolean(bool value)
    {
        return new ContractParameter(ContractParameterType.Boolean) { Value = value };
    }

    private static byte[] ExternalPayoutMessage(
        uint externalChainId,
        uint neoChainId,
        ulong nonce,
        UInt160 foreignSender,
        UInt160 foreignAsset,
        UInt160 recipient,
        BigInteger amount,
        UInt256 sourceTransaction)
    {
        var amountBytes = amount.ToByteArray(isUnsigned: true, isBigEndian: false);
        var payloadLength = 24 + amountBytes.Length;
        var bytes = new byte[102 + payloadLength];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0, 4), externalChainId);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), neoChainId);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8, 8), nonce);
        bytes[16] = 2;
        foreignSender.GetSpan().CopyTo(bytes.AsSpan(17, UInt160.Length));
        recipient.GetSpan().CopyTo(bytes.AsSpan(37, UInt160.Length));
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(57, 8), 0);
        sourceTransaction.GetSpan().CopyTo(bytes.AsSpan(65, UInt256.Length));
        bytes[97] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(98, 4), (uint)payloadLength);
        foreignAsset.GetSpan().CopyTo(bytes.AsSpan(102, UInt160.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(122, 4), (uint)amountBytes.Length);
        amountBytes.CopyTo(bytes, 126);
        return bytes;
    }

    private static Transaction PayoutTransaction(UInt160 signer, uint nonce)
    {
        return new Transaction
        {
            Version = 0,
            Nonce = nonce,
            SystemFee = 0,
            NetworkFee = 0,
            ValidUntilBlock = 100,
            Signers = [new Signer { Account = signer, Scopes = WitnessScope.Global }],
            Attributes = [],
            Script = new byte[] { (byte)OpCode.RET },
            Witnesses = [Witness.Empty],
        };
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
