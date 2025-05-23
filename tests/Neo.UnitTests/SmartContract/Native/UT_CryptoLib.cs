// Copyright (C) 2015-2025 The Neo Project.
//
// UT_CryptoLib.cs file belongs to the neo project and is free
// software distributed under the MIT software license, see the
// accompanying file LICENSE in the main directory of the
// repository or http://www.opensource.org/licenses/mit-license.php
// for more details.
//
// Redistribution and use in source and binary forms with or without
// modifications are permitted.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.Cryptography;
using Neo.Cryptography.BLS12_381;
using Neo.Cryptography.ECC;
using Neo.Extensions;
using Neo.Ledger;
using Neo.Network.P2P;
using Neo.Network.P2P.Payloads;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using Neo.VM;
using Org.BouncyCastle.Utilities.Encoders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Neo.UnitTests.SmartContract.Native
{
    [TestClass]
    public class UT_CryptoLib
    {
        private static readonly string s_g1Hex =
            "97f1d3a73197d7942695638c4fa9ac0fc3688c4f9774b905a14e3a3f171bac586c55e83ff97a1aeffb3af00adb22c6bb";

        private static readonly string s_g2Hex =
            "93e02b6052719f607dacd3a088274f65596bd0d09920b61ab5da61bbdc7f5049334cf11213945d57e5ac7d055d042b7e" +
            "024aa2b2f08f0a91260805272dc51051c6e47ad4fa403b02b4510b647ae3d1770bac0326a805bbefd48056c8c121bdb8";

        private static readonly string s_gtHex =
            "0f41e58663bf08cf068672cbd01a7ec73baca4d72ca93544deff686bfd6df543d48eaa24afe47e1efde449383b676631" +
            "04c581234d086a9902249b64728ffd21a189e87935a954051c7cdba7b3872629a4fafc05066245cb9108f0242d0fe3ef" +
            "03350f55a7aefcd3c31b4fcB6ce5771cc6a0e9786ab5973320c806ad360829107ba810c5a09ffdd9be2291a0c25a99a2" +
            "11b8b424cd48bf38fcef68083b0b0ec5c81a93b330ee1a677d0d15ff7b984e8978ef48881e32fac91b93b47333e2ba57" +
            "06fba23eb7c5af0d9f80940ca771b6ffd5857baaf222eb95a7d2809d61bfe02e1bfd1b68ff02f0b8102ae1c2d5d5ab1a" +
            "19f26337d205fb469cd6bd15c3d5a04dc88784fbb3d0b2dbdea54d43b2b73f2cbb12d58386a8703e0f948226e47ee89d" +
            "018107154f25a764bd3c79937a45b84546da634b8f6be14a8061e55cceba478b23f7dacaa35c8ca78beae9624045b4b6" +
            "01b2f522473d171391125ba84dc4007cfbf2f8da752f7c74185203fcca589ac719c34dffbbaad8431dad1c1fb597aaa5" +
            "193502b86edb8857c273fa075a50512937e0794e1e65a7617c90d8bd66065b1fffe51d7a579973b1315021ec3c19934f" +
            "1368bb445c7c2d209703f239689ce34c0378a68e72a6b3b216da0e22a5031b54ddff57309396b38c881c4c849ec23e87" +
            "089a1c5b46e5110b86750ec6a532348868a84045483c92b7af5af689452eafabf1a8943e50439f1d59882a98eaa0170f" +
            "1250ebd871fc0a92a7b2d83168d0d727272d441befa15c503dd8e90ce98db3e7b6d194f60839c508a84305aaca1789b6";

        private readonly byte[] g1 = s_g1Hex.HexToBytes();
        private readonly byte[] g2 = s_g2Hex.HexToBytes();
        private readonly byte[] gt = s_gtHex.HexToBytes();


        private readonly byte[] notG1 =
            "8123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
            .HexToBytes();

        private readonly byte[] notG2 =
            ("8123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef" +
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")
            .HexToBytes();

        [TestMethod]
        public void TestG1()
        {
            var snapshotCache = TestBlockchain.GetTestSnapshotCache();
            using ScriptBuilder script = new();
            script.EmitDynamicCall(NativeContract.CryptoLib.Hash, "bls12381Deserialize", g1);

            using var engine = ApplicationEngine.Create(TriggerType.Application, null, snapshotCache,
                settings: TestProtocolSettings.Default);
            engine.LoadScript(script.ToArray());
            Assert.AreEqual(VMState.HALT, engine.Execute());
            var result = engine.ResultStack.Pop();
            Assert.AreEqual(s_g1Hex, result.GetInterface<G1Affine>().ToCompressed().ToHexString());
        }

        [TestMethod]
        public void TestG2()
        {
            var snapshotCache = TestBlockchain.GetTestSnapshotCache();
            using ScriptBuilder script = new();
            script.EmitDynamicCall(NativeContract.CryptoLib.Hash, "bls12381Deserialize", g2);

            using var engine = ApplicationEngine.Create(TriggerType.Application, null, snapshotCache,
                settings: TestProtocolSettings.Default);
            engine.LoadScript(script.ToArray());
            Assert.AreEqual(VMState.HALT, engine.Execute());
            var result = engine.ResultStack.Pop();
            Assert.AreEqual(s_g2Hex, result.GetInterface<G2Affine>().ToCompressed().ToHexString());
        }

        [TestMethod]
        public void TestNotG1()
        {
            var snapshotCache = TestBlockchain.GetTestSnapshotCache();
            using ScriptBuilder script = new();
            script.EmitDynamicCall(NativeContract.CryptoLib.Hash, "bls12381Deserialize", notG1);

            using var engine = ApplicationEngine.Create(TriggerType.Application, null, snapshotCache,
                settings: TestProtocolSettings.Default);
            engine.LoadScript(script.ToArray());
            Assert.AreEqual(VMState.FAULT, engine.Execute());
        }

        [TestMethod]
        public void TestNotG2()
        {
            var snapshotCache = TestBlockchain.GetTestSnapshotCache();
            using ScriptBuilder script = new();
            script.EmitDynamicCall(NativeContract.CryptoLib.Hash, "bls12381Deserialize", notG2);

            using var engine = ApplicationEngine.Create(TriggerType.Application, null, snapshotCache,
                settings: TestProtocolSettings.Default);
            engine.LoadScript(script.ToArray());
            Assert.AreEqual(VMState.FAULT, engine.Execute());
        }

        [TestMethod]
        public void TestBls12381Add()
        {
            var snapshotCache = TestBlockchain.GetTestSnapshotCache();
            using ScriptBuilder script = new();
            script.EmitDynamicCall(NativeContract.CryptoLib.Hash, "bls12381Deserialize", gt);
            script.EmitDynamicCall(NativeContract.CryptoLib.Hash, "bls12381Deserialize", gt);
            script.EmitPush(2);
            script.Emit(OpCode.PACK);
            script.EmitPush(CallFlags.All);
            script.EmitPush("bls12381Add");
            script.EmitPush(NativeContract.CryptoLib.Hash);
            script.EmitSysCall(ApplicationEngine.System_Contract_Call);

            using var engine = ApplicationEngine.Create(TriggerType.Application, null, snapshotCache,
                settings: TestProtocolSettings.Default);
            engine.LoadScript(script.ToArray());
            Assert.AreEqual(VMState.HALT, engine.Execute());
            var result = engine.ResultStack.Pop();
            var expected =
                "079AB7B345EB23C944C957A36A6B74C37537163D4CBF73BAD9751DE1DD9C68EF72CB21447E259880F72A871C3EDA1B0C" +
                "017F1C95CF79B22B459599EA57E613E00CB75E35DE1F837814A93B443C54241015AC9761F8FB20A44512FF5CFC04AC7F" +
                "0F6B8B52B2B5D0661CBF232820A257B8C5594309C01C2A45E64C6A7142301E4FB36E6E16B5A85BD2E437599D103C3ACE" +
                "06D8046C6B3424C4CD2D72CE98D279F2290A28A87E8664CB0040580D0C485F34DF45267F8C215DCBCD862787AB555C7E" +
                "113286DEE21C9C63A458898BEB35914DC8DAAAC453441E7114B21AF7B5F47D559879D477CF2A9CBD5B40C86BECD07128" +
                "0900410BB2751D0A6AF0FE175DCF9D864ECAAC463C6218745B543F9E06289922434EE446030923A3E4C4473B4E3B1914" +
                "081ABD33A78D31EB8D4C1BB3BAAB0529BB7BAF1103D848B4CEAD1A8E0AA7A7B260FBE79C67DBE41CA4D65BA8A54A72B6" +
                "1692A61CE5F4D7A093B2C46AA4BCA6C4A66CF873D405EBC9C35D8AA639763720177B23BEFFAF522D5E41D3C5310EA333" +
                "1409CEBEF9EF393AA00F2AC64673675521E8FC8FDDAF90976E607E62A740AC59C3DDDF95A6DE4FBA15BEB30C43D4E3F8" +
                "03A3734DBEB064BF4BC4A03F945A4921E49D04AB8D45FD753A28B8FA082616B4B17BBCB685E455FF3BF8F60C3BD32A0C" +
                "185EF728CF41A1B7B700B7E445F0B372BC29E370BC227D443C70AE9DBCF73FEE8ACEDBD317A286A53266562D817269C0" +
                "04FB0F149DD925D2C590A960936763E519C2B62E14C7759F96672CD852194325904197B0B19C6B528AB33566946AF39B";
            Assert.AreEqual(expected.ToLower(), result.GetInterface<Gt>().ToArray().ToHexString());
        }

        [TestMethod]
        public void TestBls12381Mul()
        {
            var data = new byte[32];
            data[0] = 0x03;
            var snapshotCache = TestBlockchain.GetTestSnapshotCache();
            using (ScriptBuilder script = new())
            {
                script.EmitPush(false);
                script.EmitPush(data);
                script.EmitDynamicCall(NativeContract.CryptoLib.Hash, "bls12381Deserialize", gt);
                script.EmitPush(3);
                script.Emit(OpCode.PACK);
                script.EmitPush(CallFlags.All);
                script.EmitPush("bls12381Mul");
                script.EmitPush(NativeContract.CryptoLib.Hash);
                script.EmitSysCall(ApplicationEngine.System_Contract_Call);

                using var engine = ApplicationEngine.Create(TriggerType.Application, null, snapshotCache,
                    settings: TestProtocolSettings.Default);
                engine.LoadScript(script.ToArray());
                Assert.AreEqual(VMState.HALT, engine.Execute());
                var result = engine.ResultStack.Pop();
                var expected =
                    "18B2DB6B3286BAEA116CCAD8F5554D170A69B329A6DE5B24C50B8834965242001A1C58089FD872B211ACD3263897FA66" +
                    "0B117248D69D8AC745283A3E6A4CCEC607F6CF7CEDEE919575D4B7C8AE14C36001F76BE5FCA50ADC296EF8DF4926FA7F" +
                    "0B55A75F255FE61FC2DA7CFFE56ADC8775AAAB54C50D0C4952AD919D90FB0EB221C41ABB9F2352A11BE2D7F176ABE41E" +
                    "0E30AFB34FC2CE16136DE66900D92068F30011E9882C0A56E7E7B30F08442BE9E58D093E1888151136259D059FB53921" +
                    "0D635BC491D5244A16CA28FDCF10546EC0F7104D3A419DDC081BA30ECB0CD2289010C2D385946229B7A9735ADC827369" +
                    "14FE61AD26C6C38B787775DE3B939105DE055F8D7004358272A0823F6F1787A7ABB6C3C59C8C9CBD1674AC9005126328" +
                    "18CDD273F0D38833C07467EAF77743B70C924D43975D3821D47110A358757F926FCF970660FBDD74EF15D93B81E3AA29" +
                    "0C78F59CBC6ED0C1E0DCBADFD11A73EB7137850D29EFEB6FA321330D0CF70F5C7F6B004BCF86AC99125F8FECF8315793" +
                    "0BEC2AF89F8B378C6D7F63B0A07B3651F5207A84F62CEE929D574DA154EBE795D519B661086F069C9F061BA3B53DC491" +
                    "0EA1614C87B114E2F9EF328AC94E93D00440B412D5AE5A3C396D52D26C0CDF2156EBD3D3F60EA500C42120A7CE1F7EF8" +
                    "0F15323118956B17C09E80E96ED4E1572461D604CDE2533330C684F86680406B1D3EE830CBAFE6D29C9A0A2F41E03E26" +
                    "095B713EB7E782144DB1EC6B53047FCB606B7B665B3DD1F52E95FCF2AE59C4AB159C3F98468C0A43C36C022B548189B6";
                Assert.AreEqual(expected.ToLower(), result.GetInterface<Gt>().ToArray().ToHexString());
            }
            using (ScriptBuilder script = new())
            {
                script.EmitPush(true);
                script.EmitPush(data);
                script.EmitDynamicCall(NativeContract.CryptoLib.Hash, "bls12381Deserialize", gt);
                script.EmitPush(3);
                script.Emit(OpCode.PACK);
                script.EmitPush(CallFlags.All);
                script.EmitPush("bls12381Mul");
                script.EmitPush(NativeContract.CryptoLib.Hash);
                script.EmitSysCall(ApplicationEngine.System_Contract_Call);

                using var engine = ApplicationEngine.Create(TriggerType.Application, null, snapshotCache, settings: TestProtocolSettings.Default);
                engine.LoadScript(script.ToArray());
                Assert.AreEqual(VMState.HALT, engine.Execute());
                var result = engine.ResultStack.Pop();
                var expected =
                    "014E367F06F92BB039AEDCDD4DF65FC05A0D985B4CA6B79AA2254A6C605EB424048FA7F6117B8D4DA8522CD9C767B045" +
                    "0EEF9FA162E25BD305F36D77D8FEDE115C807C0805968129F15C1AD8489C32C41CB49418B4AEF52390900720B6D8B02C" +
                    "0EAB6A8B1420007A88412AB65DE0D04FEECCA0302E7806761483410365B5E771FCE7E5431230AD5E9E1C280E8953C68D" +
                    "0BD06236E9BD188437ADC14D42728C6E7177399B6B5908687F491F91EE6CCA3A391EF6C098CBEAEE83D962FA604A718A" +
                    "0C9DB625A7AAC25034517EB8743B5868A3803B37B94374E35F152F922BA423FB8E9B3D2B2BBF9DD602558CA5237D3742" +
                    "0502B03D12B9230ED2A431D807B81BD18671EBF78380DD3CF490506187996E7C72F53C3914C76342A38A536FFAED4783" +
                    "18CDD273F0D38833C07467EAF77743B70C924D43975D3821D47110A358757F926FCF970660FBDD74EF15D93B81E3AA29" +
                    "0C78F59CBC6ED0C1E0DCBADFD11A73EB7137850D29EFEB6FA321330D0CF70F5C7F6B004BCF86AC99125F8FECF8315793" +
                    "0BEC2AF89F8B378C6D7F63B0A07B3651F5207A84F62CEE929D574DA154EBE795D519B661086F069C9F061BA3B53DC491" +
                    "0EA1614C87B114E2F9EF328AC94E93D00440B412D5AE5A3C396D52D26C0CDF2156EBD3D3F60EA500C42120A7CE1F7EF8" +
                    "0F15323118956B17C09E80E96ED4E1572461D604CDE2533330C684F86680406B1D3EE830CBAFE6D29C9A0A2F41E03E26" +
                    "095B713EB7E782144DB1EC6B53047FCB606B7B665B3DD1F52E95FCF2AE59C4AB159C3F98468C0A43C36C022B548189B6";
                Assert.AreEqual(expected.ToLower(), result.GetInterface<Gt>().ToArray().ToHexString());
            }
        }

        [TestMethod]
        public void TestBls12381Pairing()
        {
            var snapshotCache = TestBlockchain.GetTestSnapshotCache();
            using ScriptBuilder script = new();
            script.EmitDynamicCall(NativeContract.CryptoLib.Hash, "bls12381Deserialize", g2);
            script.EmitDynamicCall(NativeContract.CryptoLib.Hash, "bls12381Deserialize", g1);
            script.EmitPush(2);
            script.Emit(OpCode.PACK);
            script.EmitPush(CallFlags.All);
            script.EmitPush("bls12381Pairing");
            script.EmitPush(NativeContract.CryptoLib.Hash);
            script.EmitSysCall(ApplicationEngine.System_Contract_Call);

            using var engine = ApplicationEngine.Create(TriggerType.Application, null, snapshotCache,
                settings: TestProtocolSettings.Default);
            engine.LoadScript(script.ToArray());
            Assert.AreEqual(VMState.HALT, engine.Execute());
            var result = engine.ResultStack.Pop();
            var expected =
                "0F41E58663BF08CF068672CBD01A7EC73BACA4D72CA93544DEFF686BFD6DF543D48EAA24AFE47E1EFDE449383B676631" +
                "04C581234D086A9902249B64728FFD21A189E87935A954051C7CDBA7B3872629A4FAFC05066245CB9108F0242D0FE3EF" +
                "03350F55A7AEFCD3C31B4FCB6CE5771CC6A0E9786AB5973320C806AD360829107BA810C5A09FFDD9BE2291A0C25A99A2" +
                "11B8B424CD48BF38FCEF68083B0B0EC5C81A93B330EE1A677D0D15FF7B984E8978EF48881E32FAC91B93B47333E2BA57" +
                "06FBA23EB7C5AF0D9F80940CA771B6FFD5857BAAF222EB95A7D2809D61BFE02E1BFD1B68FF02F0B8102AE1C2D5D5AB1A" +
                "19F26337D205FB469CD6BD15C3D5A04DC88784FBB3D0B2DBDEA54D43B2B73F2CBB12D58386A8703E0F948226E47EE89D" +
                "018107154F25A764BD3C79937A45B84546DA634B8F6BE14A8061E55CCEBA478B23F7DACAA35C8CA78BEAE9624045B4B6" +
                "01B2F522473D171391125BA84DC4007CFBF2F8DA752F7C74185203FCCA589AC719C34DFFBBAAD8431DAD1C1FB597AAA5" +
                "193502B86EDB8857C273FA075A50512937E0794E1E65A7617C90D8BD66065B1FFFE51D7A579973B1315021EC3C19934F" +
                "1368BB445C7C2D209703F239689CE34C0378A68E72A6B3B216DA0E22A5031B54DDFF57309396B38C881C4C849EC23E87" +
                "089A1C5B46E5110B86750EC6A532348868A84045483C92B7AF5AF689452EAFABF1A8943E50439F1D59882A98EAA0170F" +
                "1250EBD871FC0A92A7B2D83168D0D727272D441BEFA15C503DD8E90CE98DB3E7B6D194F60839C508A84305AACA1789B6";
            Assert.AreEqual(expected.ToLower(), result.GetInterface<Gt>().ToArray().ToHexString());
        }

        [TestMethod]
        public void Bls12381Equal()
        {
            var snapshotCache = TestBlockchain.GetTestSnapshotCache();
            using ScriptBuilder script = new();
            script.EmitDynamicCall(NativeContract.CryptoLib.Hash, "bls12381Deserialize", g1);
            script.EmitDynamicCall(NativeContract.CryptoLib.Hash, "bls12381Deserialize", g1);
            script.EmitPush(2);
            script.Emit(OpCode.PACK);
            script.EmitPush(CallFlags.All);
            script.EmitPush("bls12381Equal");
            script.EmitPush(NativeContract.CryptoLib.Hash);
            script.EmitSysCall(ApplicationEngine.System_Contract_Call);

            using var engine = ApplicationEngine.Create(TriggerType.Application, null, snapshotCache,
                settings: TestProtocolSettings.Default);
            engine.LoadScript(script.ToArray());
            Assert.AreEqual(VMState.HALT, engine.Execute());
            var result = engine.ResultStack.Pop();
            Assert.IsTrue(result.GetBoolean());
        }

        private enum BLS12381PointType : byte
        {
            G1Proj,
            G2Proj,
            GT
        }

        private void CheckBls12381ScalarMul_Compat(string point, string mul, bool negative, string expected, BLS12381PointType expectedType)
        {
            var data = new byte[32];
            data[0] = 0x03;
            var snapshotCache = TestBlockchain.GetTestSnapshotCache();
            using (ScriptBuilder script = new())
            {
                script.EmitPush(negative);
                script.EmitPush(mul.ToLower().HexToBytes());
                script.EmitDynamicCall(NativeContract.CryptoLib.Hash, "bls12381Deserialize", point.ToLower().HexToBytes());
                script.EmitPush(3);
                script.Emit(OpCode.PACK);
                script.EmitPush(CallFlags.All);
                script.EmitPush("bls12381Mul");
                script.EmitPush(NativeContract.CryptoLib.Hash);
                script.EmitSysCall(ApplicationEngine.System_Contract_Call);

                using var engine = ApplicationEngine.Create(TriggerType.Application, null, snapshotCache,
                    settings: TestProtocolSettings.Default);
                engine.LoadScript(script.ToArray());
                Assert.AreEqual(VMState.HALT, engine.Execute());
                var result = engine.ResultStack.Pop();
                switch (expectedType)
                {
                    case BLS12381PointType.G1Proj:
                        Assert.AreEqual(expected, new G1Affine(result.GetInterface<G1Projective>()).ToCompressed().ToHexString());
                        break;
                    case BLS12381PointType.G2Proj:
                        Assert.AreEqual(expected, new G2Affine(result.GetInterface<G2Projective>()).ToCompressed().ToHexString());
                        break;
                    case BLS12381PointType.GT:
                        Assert.AreEqual(expected, result.GetInterface<Gt>().ToArray().ToHexString());
                        break;
                    default:
                        Assert.Fail("Unknown result point type.");
                        break;
                }
            }
        }

        [TestMethod]
        public void TestBls12381ScalarMul_Compat()
        {
            // GT mul by positive scalar.
            CheckBls12381ScalarMul_Compat(
                "14fd52fe9bfd08bbe23fcdf1d3bc5390c62e75a8786a72f8a343123a30a7c5f8d18508a21a2bf902f4db2c068913bc1c" +
                "130e7ce13260d601c89ee717acfd3d4e1d80f409dd2a5c38b176f0b64d3d0a224c502717270dfecf2b825ac24608215c" +
                "0d7fcfdf3c1552ada42b7e0521bc2e7389436660c352ecbf2eedf30b77b6b501df302399e6240473af47abe56fc97478" +
                "0c214542fcc0cf10e3001fa5e82d398f6ba1ddd1ccdf133bfd75e033eae50aec66bd5e884b8c74d4c1c6ac7c01278ac5" +
                "164a54600cb2e24fec168f82542fbf98234dbb9ddf06503dc3c497da88b73db584ba19e685b1b398b51f40160e6c8f09" +
                "17b4a68dedcc04674e5f5739cf0d845ba801263f712ed4ddda59c1d9909148e3f28124ae770682c9b19233bf0bcfa00d" +
                "05bfe708d381b066b83a883ba8251ce2ea6772cbde51e1322d82b2c8a026a2153f4822e20cb69b8b05003ee74e09cb48" +
                "1728d688caa8a671f90b55488e272f48c7c5ae32526d3635a5343eb02640358d9ac445c76a5d8f52f653bbaee04ba5ce" +
                "03c68b88c25be6fd3611cc21c9968e4f87e541beeccc5170b8696a439bb666ad8a6608ab30ebc7dfe56eaf0dd9ab8439" +
                "171a6e4e0d608e6e6c8ac5ddcf8d6d2a950d06051e6b6c4d3feb6dc8dac2acadd345cadfb890454a2101a112f7471f0e" +
                "001701f60f3d4352c4d388c0f198854908c0e939719709c1b3f82d2a25cc7156a3838bc141e041c259849326fbd0839f" +
                "15cea6a78b89349dcd1c03695a74e72d3657af4ee2cf267337bc96363ef4a1c5d5d7a673cc3a3c1a1350043f99537d62",
                "8463159bd9a1d1e1fd815172177ec24c0c291353ed88b3d1838fd9d63b1efd0b",
                false,
                "03dc980ce0c037634816f9fc1edb2e1807e38a51f838e3a684f195d6c52c41d6a8a5b64d57d3fda507bebe3bd4b661af" +
                "0e4f7c46754b373c955982b4d64a24838cbc010d04b6ceb499bf411d114dab77eaf70f96ab66c2868dcd63706b602b07" +
                "010c487fc16c90b61e1c2ad33c31c8f3fc86d114a59b127ac584640f149f3597102c55dd1ed8a305a10c052c0a724e57" +
                "0fc079e410123735a6144ccd88d9e4e91d7b889f80b18a1741eacd6f244fce3cf57795e619b6648b9238053b4b8e4ed6" +
                "115c905fbcb61525370667ff43144e12b700662a7344ac1af97f11d09779ca6865973f95ff318b42ff00df7c6eb95816" +
                "0947a0ab6cb25534af51ce1f0b076907c6eb5ce0760bd7670cab8814cc3308766eb6e52b5427dbf85d6424990fd33545" +
                "15ab880358bc55075a08f36b855694c02ee0bd63adefe235ba4ee41dc600a1cae950c1dc760bf7b1edd8712e9e90eebb" +
                "19de705e29f4feb870129441bd4b9e91c3d37e60c12fa79a5b1e4132ba9498044e6fbf2de37e4dd88b4e9095b46f1220" +
                "19e73a561ba3967b32813c3ec74b8e1b6ab619eeab698e6638114cb29ca9c3d353192db3d392fee2b4dfdfd36b13db44" +
                "0534dd754417cffcd470f4d4cfdcb6d7896181c27b8b30622d7a4ca0a05a7ea67ca011cab07738235b115bbd33023969" +
                "1487d2de5d679a8cad2fe5c7fff16b0b0f3f929619c8005289c3d7ffe5bcd5ea19651bfc9366682a2790cab45ee9a988" +
                "15bb7e58dc666e2209cd9d700546cf181ceb43fe719243930984b696b0d18d4cd1f5d960e149a2b753b1396e4f8f3b16",
                BLS12381PointType.GT
            );
            var testData =
                "0e0c651ff4a57adebab1fa41aa8d1e53d1cf6a6cc554282a24bb460ea0dc169d3ede8b5a93a331698f3926d273a729aa" +
                "18788543413f43ada55a6a7505e3514f0db7e14d58311c3211962a350bcf908b3af90fbae31ff536fe542328ad25cd3e" +
                "044a796200c8a8ead7edbc3a8a37209c5d37433ca7d8b0e644d7aac9726b524c41fef1cf0d546c252d795dffc445ddee" +
                "07041f57c4c9a673bd314294e280ab61390731c09ad904bdd7b8c087d0ce857ea86e78f2d98e75d9b5e377e5751d67cf" +
                "1717cbce31bc7ea6df95132549bf6d284a68005c53228127671afa54ecfd4c5c4debc437c4c6d9b9aeeee8b4159a5691" +
                "128c6dc68b309fd822b14f3ce8ff390bd6834d30147e8ab2edc59d0d7b14cc13c79e6eed5fd6cae1795ba3760345d59c" +
                "0c585f79c900902515e3e95938d9929ad8310e71fc7fd54be9c7529f244af40dadaca0b3bd8afd911f24b261079de48b" +
                "161dd8f340d42bd84e717275193a0375d9e10fbe048bbea30abd64d3fe085c15b9be192f7baaa0b3a9658bcbb4292a0c" +
                "0149beb30e54b065a75df45e5da77583f4471e3454cea90a00b5a9a224c15e2ebe01f0ab8aa86591c1012c618d41fdce" +
                "07ecfcaddc8dc408b7176b79d8711a4161a56f41a5be6714cbcaa70e53387ab049826ac9e636640bc6da919e52f86f32" +
                "09572b62d9bfd48bd2b5ef217932237b90a70d40167623d0f25a73b753e3214310bc5b6e017aebc1a9ca0c8067a97da6" +
                "162c70cc754f1b2ac3b05ba834712758c8de4641ef09237edf588989182ab3047ee42da2b840fd3633fa0f34d46ad961";
            // GT mul by positive scalar.
            CheckBls12381ScalarMul_Compat(
                testData,
                "06c93a0ebbc8b5cd3af798b8f72442a67aa885b395452a08e48ec80b4e9f1b3f",
                false,
                "0d6d91f120ab61e14a3163601ce584f053f1de9dc0a548b6fbf37a776ec7b6ce6b866e8c8b0fc0ac8d32a9a9747c98bf" +
                "0e6aee5bddd058313958bfc3ac1ed75284628f92bb9b99fee101e1bee9d74bad7812287ea76bdbe07f20ff9998d6e9f0" +
                "16689be1cfc4337433644a679945d5c34a6d4dd984c56d6c28428438268b385cb1d86f69b0377b18f9b084e1d0b65962" +
                "13233d559a1b5caaba38be853f667fc3b1f9f2c4c9020584502ff5f370b0aba7768a1a4ca4328bc3c7be2bc9c3949f5e" +
                "16fd3bfc16b11da41b7393e56e777640b000db15b6e6192e5c59dfece90c6fc0b6071fdeef7061974b5e967c5b88b1db" +
                "09f7c92077c16f56aff9e9627f5e09928e965daee17d05ef3fdc0c502b649db473b5b2bba867d829b04d32cfeab73876" +
                "14190b265382378f75e4e085a5537d4f200fe56b74b7c52c5546b30d51862e1ac1f60eba157880090a42ea9b0295529f" +
                "134c1fc90f19a4c20dc0be105b07e0c67218b2f5619a66d8d770d539658eb74c255743e5847bc437fef3077d0a6c4f17" +
                "198d63cf17e6957f2ad9449269af009635697e92254a3f67be9b8760fd9f974826a1829fedb4cf66968b7c63b0c88c51" +
                "0da12e6d52255256757afa03ad29b5c1624292ef7eb463eb4bc81ac7426f36db3fe1513bdd31bc138bfe903bbb0c5207" +
                "001335f708c16cea15ef6b77c3215326a779e927b8c2081b15adffe71ba75164e376665533c5bb59373b27dbe93a0a0e" +
                "1796d821a1b9ff01846446c5ad53064cb9b941f97aa870285395e1a44c9f6e5144ea5a0cf57b9fdd962a5ec3ff1f72fe",
                BLS12381PointType.GT
            );
            // GT mul by positive scalar.
            CheckBls12381ScalarMul_Compat(
                testData,
                "b0010000000000005e0000000000000071f30400000000006d9189c813000000",
                false,
                "0919ad29cdbe0b6bbd636fbe3c8930a1b959e5aa37294a6cc7d018e2776580768bb98bf91ce1bc97f2e6fa647e7dad7b" +
                "15db564645d2e4868129ed414b7e369e831b8ff93997a22b6ca0e2ba288783f535aed4b44cf3e952897db1536da18a12" +
                "0a70da2b9dd901bd12a5a7047d3b6346ba1aea53b642b7355a91f957687fccd840ef24af100d0ada6b49e35183456ec3" +
                "0b505098526b975477b6ca0273d3a841c85e4a8319b950e76ec217a4f939844baa6b875a4046a30c618636fe9b25c620" +
                "030f31044f883789945c2bcb75d7d4099b2bc97665e75c1bee27bc3864e7e5e2ccb57a9da0b57be1a6aca217a6cfda09" +
                "0c4fd222f7b8cfdc32969da4fe8828a59ee1314546efdf99ef7ede1a42df6e7a126fe83b4c41b5e70a56bd9ab499f7e8" +
                "0e27a08884be05f1d2a527417fc6e30448333c0724463bf92d722ef5fd6f06949e294e6f941976d24c856038b55a2ec2" +
                "00d14d958a688f23b572993bd0f18cbbc20defe88e423b262c552dcc4d9f63ad78e85efbcea9449f81f39e1a887eb79b" +
                "07056bb5a672444e240660617ba7a40985a622c687c1d05c12cee7b086abfc5f39a83a5ad7638ee559f710013b772d42" +
                "07924687cb30100bcd4e8c83c9fa19dce7785bf3ae7681a0968fd9661c990e2dace05902dceeed65aacf51a04e72f0fd" +
                "04858ea70fb72f2a3807dc1839a385d85b536abfd3ec76d4931b3bc5ec4d90e2ebc0342567c9507abdfafa602fc6983f" +
                "13f20eb26b4169dc3908109fe3c1887db4be8f30edad989dc8caa234f9818ac488b110ad30a30f769277168650b6910e",
                BLS12381PointType.GT
            );
            // GT mul by negative scalar.
            CheckBls12381ScalarMul_Compat(
                "0bdbfc3b68e7067630a1908de2ce15e1890d57b855ffc2ee0fe765293581c304d0507254fd9921d8ff4bff3185b1e8ae" +
                "017091a6b9e243c3108b4302f30e2f4cb452c4574d23d06942cf915fb0b64c3546aa0bfbba5182dc42b63ebd09cd950f" +
                "06ebf85ff360032e63d5422fed5969b80ed4abaf58d29317d9cf8e5a55744993ffc0ccc586a187c63f9c47d4b41870aa" +
                "0fd73e13a4f7d3b072407a3bfa6539f8d56856542b17326ab77833df274e61a41c237a6dbf20a333698a675fded6ab1a" +
                "114891795eabbedcb81590ff9bfb4b23b66c8b8376a69cf58511c80f3ac83d52c0c950be8c30d01108479f232d8e4e89" +
                "19d869dc85db0b9d6ccf40eb8f8ab08e43a910c341737a55e751fa4a097ee82c5ac83d38c543d957bd9850af16039d1a" +
                "00c96575d2ee24e9990b3401153446aa6593d3afb6ce7ca57d6432b8dda31aaa1a08834ad38deae5a807d11663adc5c2" +
                "0ae7227a2cbb7917d1489175b89ed1ba415e4fc55b7d0a286caf2f5f40b0dd39cdd8fc8c271d8a7ae952fe6ece5f7c10" +
                "19bfab0167af86314a73bfa37fd16bc6edff6d9ee75610a4eec1818c668ef9f509b1cdd54542e73dc0e343a4fd6e3bb6" +
                "18540c1d060b60b63b645a895105425eb813b08b6ac91be3145da04040f2a45ffcf06e96b685519fca93b0f15238dc0e" +
                "030c2199127ba82fa8a193f5f01ae24270e9669923653db38cae711d68169aa25df51a8915f3f8219892f4f5e67d550b" +
                "00910011685017dcc1777a9d48689ce590d57c1fc942d49cfad0ed7efc0169a95d7e7378af26bafb90d1619bcdab64cd",
                "688e58217305c1fd2fe0637cbd8e7414d4d0a2113314eb05592f97930d23b34d",
                true,
                "056fdc84f044148950c0b7c4c0613f5710fcaeb1b023b9d8f814dc39d48702db70ce41aa276566960e37237f22b086b0" +
                "17b9ed0e264e2b7872c8a7affb8b9f847a528d092a038dab4ac58d3a33d30e2e5078b5e39ebb7441c56ae7556b63ecd6" +
                "139ed9be1c5eb9f987cc704c913c1e23d44d2e04377347f6c471edc40cdb2cd4e32c396194363cd21ceff9bedbd164a4" +
                "1050e701012f0456383210f8054e76c0906e3f37e10d4a3d6342e79e39d566ea785b385bb692cddbd6c16456dfabf19f" +
                "0f84c27ec4bce096af0369ac070747cd89d97bc287afe5ed5e495ed2d743adbd8eec47df6c3a69628e803e23d8248458" +
                "00e44a8d874756a7541128892e55e9df1d1fe0583ef967db6740617a9ff50766866c0fa631aed8639cd0c13d3d6f6f21" +
                "0b340ee315caec4cc31c916d651db5e002e259fca081fb605258ccf692d786bd5bb45a054c4d8498ac2a7fa241870df6" +
                "0ba0fd8a2b063740af11e7530db1e758a8e2858a443104b8337e18c083035768a0e93126f116bb9c50c8cebe30e0ceaa" +
                "0c0b53eb2b6a1f96b34b6cc36f3417edda184e19ae1790d255337f14315323e1d2d7382b344bdc0b6b2cfab5837c24c9" +
                "16640ca351539d5459389a9c7f9b0d79e04e4a8392e0c2495dcecf7d48b10c7043825b7c6709108d81856ebf98385f0d" +
                "099e6521714c48b8eb5d2e97665375175f47c57d427d35a9dc44064a99d1c079028e36d34540baba947333ab3c8976b8" +
                "01ea48578159f041e740ea5bf73c1de3c1043a6e03311d0f2463b72694249ccc5d603e4a93cfd8a6713fb0470383c23f",
                BLS12381PointType.GT
            );

            // GT mul by zero scalar.
            CheckBls12381ScalarMul_Compat(
                "176ec726aa447f1791e69fc70a71103c84b17385094ef06a9a0235ac7241f6635377f55ad486c216c8701d61ea2ace3e" +
                "05ca1605f238dc8f29f868b795e45645c6f7ff8d9d8ffd77b5e149b0325c2a8f24dde40e80a3381ae72a9a1104ef02d7" +
                "0af7cf8f2fe6ff38961b352b0fde6f8536424fc9aa5805b8e12313bdfc01d5c1db1c0a37654c307fbd252c265dcbfc04" +
                "0ee5605ffd6ac20aab15b0343e47831f4157a20ecedd7350d2cf070c0c7d423786fd97aa7236b99f4462fb23e1735288" +
                "15bf2cf3ccbfc38303fa8154d70ee5e1e3158cbb14d5c87a773cbe948a5cfec2763c5e7129940906920aed344453b0f8" +
                "01760fd3eac8e254ce8e0ae4edd30c914bea9e2935acd4a6a9d42d185a9a6e786c8e462b769b2112423f6591b0933477" +
                "18897438ba918b9e4525888194b20ee17709f7dea319cfd053bb1c222783340326953fd3763eb6feaaa4d1458ee6ca00" +
                "1818ad88222a97e43a71dca8d2abaef70657b9ff7b94ca422d0c50ddb4265fa35514ed534217ce2f0219c6985ec2827a" +
                "0ee1dc17940926551072d693d89e36e6d14162f414b52587e5612ed4a562c9ac15df9d5fa68ccf61d52fea64b2f5d7a6" +
                "00e0a8fa735105bc9a2ecb69b6d9161e55a4ccdc2285164c6846fa5bdc106d1e0693ebd5fe86432e5e88c55f0159ec32" +
                "17332c8492332dfbd93970f002a6a05f23484e081f38815785e766779c843765d58b2444295a87939ad7f8fa4c11e853" +
                "0a62426063c9a57cf3481a00372e443dc014fd6ef4723dd4636105d7ce7b96c4b2b3b641c3a2b6e0fa9be6187e5bfaf9",
                "0000000000000000000000000000000000000000000000000000000000000000",
                false,
                string.Concat(Enumerable.Repeat("0", 1151)) + "1",
                BLS12381PointType.GT
            );
            // G1Affine mul by positive scalar.
            CheckBls12381ScalarMul_Compat(
                "a1f9855f7670a63e4c80d64dfe6ddedc2ed2bfaebae27e4da82d71ba474987a39808e8921d3df97df6e5d4b979234de8",
                "8463159bd9a1d1e1fd815172177ec24c0c291353ed88b3d1838fd9d63b1efd0b",
                false,
                "ae85e3e2d677c9e3424ed79b5a7554262c3d6849202b84d2e7024e4b1f2e9dd3f7cf20b807a9f2a67d87e47e9e94d361",
                BLS12381PointType.G1Proj
            );
            // G1Affine mul by negative scalar.
            CheckBls12381ScalarMul_Compat(
                "a1f9855f7670a63e4c80d64dfe6ddedc2ed2bfaebae27e4da82d71ba474987a39808e8921d3df97df6e5d4b979234de8",
                "8463159bd9a1d1e1fd815172177ec24c0c291353ed88b3d1838fd9d63b1efd0b",
                true,
                "8e85e3e2d677c9e3424ed79b5a7554262c3d6849202b84d2e7024e4b1f2e9dd3f7cf20b807a9f2a67d87e47e9e94d361",
                BLS12381PointType.G1Proj
            );
            // G1Affine mul by zero scalar.
            CheckBls12381ScalarMul_Compat(
                "a1f9855f7670a63e4c80d64dfe6ddedc2ed2bfaebae27e4da82d71ba474987a39808e8921d3df97df6e5d4b979234de8",
                "0000000000000000000000000000000000000000000000000000000000000000",
                false,
                "c00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000",
                BLS12381PointType.G1Proj
            );
            // G2Affine mul by positive scalar.
            CheckBls12381ScalarMul_Compat(
                "a41e586fdd58d39616fea921a855e65417a5732809afc35e28466e3acaeed3d53dd4b97ca398b2f29bf6bbcaca026a66" +
                "09a42bdeaaeef42813ae225e35c23c61c293e6ecb6759048fb76ac648ba3bc49f0fcf62f73fca38cdc5e7fa5bf511365",
                "cbfffe3e37e53e31306addde1a1725641fbe88cd047ee7477966c44a3f764b47",
                false,
                "88ae9bba988e854877c66dfb7ff84aa5e107861aa51d1a2a8dac2414d716a7e219bc4b0239e4b12d2182f57b5eea8283" +
                "0639f2e6713098ae8d4b4c3942f366614bac35c91c83ecb57fa90fe03094aca1ecd3555a7a6fdfa2417b5bb06917732e",
                BLS12381PointType.G2Proj
            );
            // G2Affine mul by negative scalar.
            CheckBls12381ScalarMul_Compat(
                "a41e586fdd58d39616fea921a855e65417a5732809afc35e28466e3acaeed3d53dd4b97ca398b2f29bf6bbcaca026a66" +
                "09a42bdeaaeef42813ae225e35c23c61c293e6ecb6759048fb76ac648ba3bc49f0fcf62f73fca38cdc5e7fa5bf511365",
                "cbfffe3e37e53e31306addde1a1725641fbe88cd047ee7477966c44a3f764b47",
                true,
                "a8ae9bba988e854877c66dfb7ff84aa5e107861aa51d1a2a8dac2414d716a7e219bc4b0239e4b12d2182f57b5eea8283" +
                "0639f2e6713098ae8d4b4c3942f366614bac35c91c83ecb57fa90fe03094aca1ecd3555a7a6fdfa2417b5bb06917732e",
                BLS12381PointType.G2Proj
            );
            // G2Affine mul by negative scalar.
            CheckBls12381ScalarMul_Compat(
                "a41e586fdd58d39616fea921a855e65417a5732809afc35e28466e3acaeed3d53dd4b97ca398b2f29bf6bbcaca026a66" +
                "09a42bdeaaeef42813ae225e35c23c61c293e6ecb6759048fb76ac648ba3bc49f0fcf62f73fca38cdc5e7fa5bf511365",
                "0000000000000000000000000000000000000000000000000000000000000000",
                false,
                "c00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000" +
                "000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000",
                BLS12381PointType.G2Proj
            );
        }

        /// <summary>
        /// Keccak256 cases are verified in https://emn178.github.io/online-tools/keccak_256.html
        /// </summary>
        [TestMethod]
        public void TestKeccak256_HelloWorld()
        {
            // Arrange
            byte[] inputData = "Hello, World!"u8.ToArray();
            string expectedHashHex = "acaf3289d7b601cbd114fb36c4d29c85bbfd5e133f14cb355c3fd8d99367964f";

            // Act
            byte[] outputData = CryptoLib.Keccak256(inputData);
            string outputHashHex = Hex.ToHexString(outputData);

            // Assert
            Assert.AreEqual(expectedHashHex, outputHashHex, "Keccak256 hash did not match expected value for 'Hello, World!'.");
        }
        [TestMethod]
        public void TestKeccak256_Keccak()
        {
            // Arrange
            byte[] inputData = "Keccak"u8.ToArray();
            string expectedHashHex = "868c016b666c7d3698636ee1bd023f3f065621514ab61bf26f062c175fdbe7f2";

            // Act
            byte[] outputData = CryptoLib.Keccak256(inputData);
            string outputHashHex = Hex.ToHexString(outputData);

            // Assert
            Assert.AreEqual(expectedHashHex, outputHashHex, "Keccak256 hash did not match expected value for 'Keccak'.");
        }

        [TestMethod]
        public void TestKeccak256_Cryptography()
        {
            // Arrange
            byte[] inputData = "Cryptography"u8.ToArray();
            string expectedHashHex = "53d49d225dd2cfe77d8c5e2112bcc9efe77bea1c7aa5e5ede5798a36e99e2d29";

            // Act
            byte[] outputData = CryptoLib.Keccak256(inputData);
            string outputHashHex = Hex.ToHexString(outputData);

            // Assert
            Assert.AreEqual(expectedHashHex, outputHashHex, "Keccak256 hash did not match expected value for 'Cryptography'.");
        }

        [TestMethod]
        public void TestKeccak256_Testing123()
        {
            // Arrange
            byte[] inputData = "Testing123"u8.ToArray();
            string expectedHashHex = "3f82db7b16b0818a1c6b2c6152e265f682d5ebcf497c9aad776ad38bc39cb6ca";

            // Act
            byte[] outputData = CryptoLib.Keccak256(inputData);
            string outputHashHex = Hex.ToHexString(outputData);

            // Assert
            Assert.AreEqual(expectedHashHex, outputHashHex, "Keccak256 hash did not match expected value for 'Testing123'.");
        }

        [TestMethod]
        public void TestKeccak256_LongString()
        {
            // Arrange
            byte[] inputData = "This is a longer string for Keccak256 testing purposes."u8.ToArray();
            string expectedHashHex = "24115e5c2359f85f6840b42acd2f7ea47bc239583e576d766fa173bf711bdd2f";

            // Act
            byte[] outputData = CryptoLib.Keccak256(inputData);
            string outputHashHex = Hex.ToHexString(outputData);

            // Assert
            Assert.AreEqual(expectedHashHex, outputHashHex, "Keccak256 hash did not match expected value for the longer string.");
        }

        [TestMethod]
        public void TestKeccak256_BlankString()
        {
            // Arrange
            byte[] inputData = ""u8.ToArray();
            string expectedHashHex = "c5d2460186f7233c927e7db2dcc703c0e500b653ca82273b7bfad8045d85a470";

            // Act
            byte[] outputData = CryptoLib.Keccak256(inputData);
            string outputHashHex = Hex.ToHexString(outputData);

            // Assert
            Assert.AreEqual(expectedHashHex, outputHashHex, "Keccak256 hash did not match expected value for blank string.");
        }

        // TestVerifyWithECDsa_CustomTxWitness_SingleSig builds custom witness verification script for single Koblitz public key
        // and ensures witness check is passed for the following message:
        //
        //	keccak256([4-bytes-network-magic-LE, txHash-bytes-BE])
        //
        // The proposed witness verification script has 110 bytes length, verification costs 2154270  * 10e-8GAS including Invocation script execution.
        // The user has to sign the keccak256([4-bytes-network-magic-LE, txHash-bytes-BE]).
        [TestMethod]
        public void TestVerifyWithECDsa_CustomTxWitness_SingleSig()
        {
            byte[] privkey = "7177f0d04c79fa0b8c91fe90c1cf1d44772d1fba6e5eb9b281a22cd3aafb51fe".HexToBytes();
            var pubHex = "04" + "fd0a8c1ce5ae5570fdd46e7599c16b175bf0ebdfe9c178f1ab848fb16dac74a5" +
                "d301b0534c7bcf1b3760881f0c420d17084907edd771e1c9c8e941bbf6ff9108";
            ECPoint pubKey = ECPoint.Parse(pubHex, ECCurve.Secp256k1);

            // vrf is a builder of witness verification script corresponding to the public key.
            using ScriptBuilder vrf = new();
            vrf.EmitPush((byte)NamedCurveHash.secp256k1Keccak256); // push Koblitz curve identifier and Keccak256 hasher.
            vrf.Emit(OpCode.SWAP); // swap curve identifier with the signature.
            vrf.EmitPush(pubKey.EncodePoint(true)); // emit the caller's public key.

            // Construct and push the signed message. The signed message is effectively the network-dependent transaction hash,
            // i.e. msg = [4-network-magic-bytes-LE, tx-hash-BE]
            // Firstly, retrieve network magic (it's uint32 wrapped into BigInteger and represented as Integer stackitem on stack).
            vrf.EmitSysCall(ApplicationEngine.System_Runtime_GetNetwork); // push network magic (Integer stackitem), can have 0-5 bytes length serialized.

            // Convert network magic to 4-bytes-length LE byte array representation.
            vrf.EmitPush(0x100000000); // push 0x100000000.
            vrf.Emit(OpCode.ADD, // the result is some new number that is 5 bytes at least when serialized, but first 4 bytes are intact network value (LE).
                    OpCode.PUSH4, // cut the first 4 bytes out of a number that is at least 5 bytes long,
                    OpCode.LEFT); // the result is 4-bytes-length LE network representation.

            // Retrieve executing transaction hash.
            vrf.EmitSysCall(ApplicationEngine.System_Runtime_GetScriptContainer); // push the script container (executing transaction, actually).
            vrf.Emit(OpCode.PUSH0, OpCode.PICKITEM); // pick 0-th transaction item (the transaction hash).

            // Concatenate network magic and transaction hash.
            vrf.Emit(OpCode.CAT); // this instruction will convert network magic to bytes using BigInteger rules of conversion.

            // Continue construction of 'verifyWithECDsa' call.
            vrf.Emit(OpCode.PUSH4, OpCode.PACK); // pack arguments for 'verifyWithECDsa' call.
            EmitAppCallNoArgs(vrf, CryptoLib.CryptoLib.Hash, "verifyWithECDsa", CallFlags.None); // emit the call to 'verifyWithECDsa' itself.

            // Account is a hash of verification script.
            var vrfScript = vrf.ToArray();
            var acc = vrfScript.ToScriptHash();

            var tx = new Transaction
            {
                Attributes = [],
                NetworkFee = 1_0000_0000,
                Nonce = (uint)Environment.TickCount,
                Script = new byte[Transaction.MaxTransactionSize / 100],
                Signers = [new Signer { Account = acc }],
                SystemFee = 0,
                ValidUntilBlock = 10,
                Version = 0,
                Witnesses = []
            };
            var signData = tx.GetSignData(TestProtocolSettings.Default.Network);
            var txSignature = Crypto.Sign(signData, privkey, ECCurve.Secp256k1, HashAlgorithm.Keccak256);

            // inv is a builder of witness invocation script corresponding to the public key.
            using ScriptBuilder inv = new();
            inv.EmitPush(txSignature); // push signature.

            tx.Witnesses =
            [
                new Witness { InvocationScript = inv.ToArray(), VerificationScript = vrfScript }
            ];

            Assert.AreEqual(VerifyResult.Succeed, tx.VerifyStateIndependent(TestProtocolSettings.Default));

            var snapshotCache = TestBlockchain.GetTestSnapshotCache();

            // Create fake balance to pay the fees.
            ApplicationEngine engine = ApplicationEngine.Create(TriggerType.Application, null, snapshotCache,
                settings: TestProtocolSettings.Default, gas: long.MaxValue);
            _ = NativeContract.GAS.Mint(engine, acc, 5_0000_0000, false);
            snapshotCache.Commit();

            Assert.AreEqual(VerifyResult.Succeed, tx.VerifyStateDependent(TestProtocolSettings.Default, snapshotCache, new(), []));

            // The resulting witness verification cost is 2154270   * 10e-8GAS.
            // The resulting witness Invocation script (66 bytes length):
            // NEO-VM > loadbase64 DEARoaaEjM/3VulrBDUod7eiZgWQS2iXIM0+I24iyJYmffhosZoQjfnnRymF/7+FaBPb9qvQwxLLSVo9ROlrdFdC
            // READY: loaded 66 instructions
            // NEO-VM 0 > ops
            // INDEX    OPCODE       PARAMETER
            // 0        PUSHDATA1    11a1a6848ccff756e96b04352877b7a26605904b689720cd3e236e22c896267df868b19a108df9e7472985ffbf856813dbf6abd0c312cb495a3d44e96b745742    <<
            //
            //
            // The resulting witness verificaiton script (110 bytes):
            // NEO-VM 0 > loadbase64:
            // "ABhQDCEC/QqMHOWuVXD91G51mcFrF1vw69/pwXjxq4SPsW2sdKVBxfug4AMAAAAAAQAAAJ4UjUEtUQgwEM6LFMAfDA92ZXJpZnlX" +
            // "aXRoRUNEc2EMFBv1dasRiWiEE2EKNaEohs3gtmxyQWJ9W1I="
            // READY: loaded 110 instructions
            // NEO-VM 0 > pos
            // Error: No help topic for 'pos'
            // NEO-VM 0 > ops
            // INDEX    OPCODE       PARAMETER
            // 0        PUSHINT8     122 (7a)    <<
            // 2        SWAP
            // 3        PUSHDATA1    02fd0a8c1ce5ae5570fdd46e7599c16b175bf0ebdfe9c178f1ab848fb16dac74a5
            // 38       SYSCALL      System.Runtime.GetNetwork (c5fba0e0)
            // 43       PUSHINT64    4294967296 (0000000001000000)
            // 52       ADD
            // 53       PUSH4
            // 54       LEFT
            // 55       SYSCALL      System.Runtime.GetScriptContainer (2d510830)
            // 60       PUSH0
            // 61       PICKITEM
            // 62       CAT
            // 63       PUSH4
            // 64       PACK
            // 65       PUSH0
            // 66       PUSHDATA1    766572696679576974684543447361 ("verifyWithECDsa")
            // 83       PUSHDATA1    1bf575ab1189688413610a35a12886cde0b66c72 ("NNToUmdQBe5n8o53BTzjTFAnSEcpouyy3B", "0x726cb6e0cd8628a1350a611384688911ab75f51b")
            // 105      SYSCALL      System.Contract.Call (627d5b52)
        }

        // TestVerifyWithECDsa_CustomTxWitness_MultiSig builds custom multisignature witness verification script for Koblitz public keys
        // and ensures witness check is passed for the M out of N multisignature of message:
        //
        //	keccak256([4-bytes-network-magic-LE, txHash-bytes-BE])
        //
        // The proposed witness verification script has 264 bytes length, verification costs 8390070  * 10e-8GAS including Invocation script execution.
        // The users have to sign the keccak256([4-bytes-network-magic-LE, txHash-bytes-BE]).
        [TestMethod]
        public void TestVerifyWithECDsa_CustomTxWitness_MultiSig()
        {
            var privkey1 = "b2dde592bfce654ef03f1ceea452d2b0112e90f9f52099bcd86697a2bd0a2b60".HexToBytes();
            var pubKey1 = ECPoint.Parse("04" +
                "0486468683c112125978ffe876245b2006bfe739aca8539b67335079262cb27a" +
                "d0dedc9e5583f99b61c6f46bf80b97eaec3654b87add0e5bd7106c69922a229d", ECCurve.Secp256k1);

            var privkey2 = "b9879e26941872ee6c9e6f01045681496d8170ed2cc4a54ce617b39ae1891b3a".HexToBytes();
            var pubKey2 = ECPoint.Parse("04" +
                "0d26fc2ad3b1aae20f040b5f83380670f8ef5c2b2ac921ba3bdd79fd0af05251" +
                "77715fd4370b1012ddd10579698d186ab342c223da3e884ece9cab9b6638c7bb", ECCurve.Secp256k1);

            var privkey3 = "4e1fe2561a6da01ee030589d504d62b23c26bfd56c5e07dfc9b8b74e4602832a".HexToBytes();
            var pubKey3 = ECPoint.Parse("04" +
                "7b4e72ae854b6a0955b3e02d92651ab7fa641a936066776ad438f95bb674a269" +
                "a63ff98544691663d91a6cfcd215831f01bfb7a226363a6c5c67ef14541dba07", ECCurve.Secp256k1);

            var privkey4 = "6dfd066bb989d3786043aa5c1f0476215d6f5c44f5fc3392dd15e2599b67a728".HexToBytes();
            var pubKey4 = ECPoint.Parse("04" +
                "b62ac4c8a352a892feceb18d7e2e3a62c8c1ecbaae5523d89d747b0219276e22" +
                "5be2556a137e0e806e4915762d816cdb43f572730d23bb1b1cba750011c4edc6", ECCurve.Secp256k1);

            // Public keys must be sorted, exactly like for standard CreateMultiSigRedeemScript.
            var keys = new List<(byte[], ECPoint)>
            {
                (privkey1, pubKey1),
                (privkey2, pubKey2),
                (privkey3, pubKey3),
                (privkey4, pubKey4),
            }.OrderBy(k => k.Item2).ToList();

            // Consider 4 users willing to sign 3/4 multisignature transaction with their Secp256k1 private keys.
            var m = 3;
            var n = keys.Count;

            // Must ensure the following conditions are met before verification script construction:
            Assert.IsTrue(n > 0);
            Assert.IsTrue(m <= n);
            Assert.AreEqual(n, keys.Select(k => k.Item2).Distinct().Count());

            // In fact, the following algorithm is implemented via NeoVM instructions:
            //
            // func Check(sigs []interop.Signature) bool {
            // 	if m != len(sigs) {
            // 		return false
            // 	}
            // 	var pubs []interop.PublicKey = []interop.PublicKey{...}
            // 	msg := append(convert.ToBytes(runtime.GetNetwork()), runtime.GetScriptContainer().Hash...)
            // 	var sigCnt = 0
            // 	var pubCnt = 0
            // 	for ; sigCnt < m && pubCnt < n; { // sigs must be sorted by pub
            // 		sigCnt += crypto.VerifyWithECDsa(msg, pubs[pubCnt], sigs[sigCnt], crypto.Secp256k1Keccak256)
            // 		pubCnt++
            // 	}
            // 	return sigCnt == m
            // }

            // vrf is a builder of M out of N multisig witness verification script corresponding to the public keys.
            using ScriptBuilder vrf = new();

            // Start the same way as regular multisig script.
            vrf.EmitPush(m); // push m.
            foreach (var tuple in keys)
            {
                vrf.EmitPush(tuple.Item2.EncodePoint(true)); // push public keys in compressed form.
            }
            vrf.EmitPush(n); // push n.

            // Initialize slots for local variables. Locals slot scheme:
            // LOC0 -> sigs
            // LOC1 -> pubs
            // LOC2 -> msg (ByteString)
            // LOC3 -> sigCnt (Integer)
            // LOC4 -> pubCnt (Integer)
            // LOC5 -> n
            // LOC6 -> m
            vrf.Emit(OpCode.INITSLOT, new ReadOnlySpan<byte>([7, 0])); // 7 locals, no args.

            // Store n.
            vrf.Emit(OpCode.STLOC5);

            // Pack public keys and store at LOC1.
            vrf.Emit(OpCode.LDLOC5, // load n.
                OpCode.PACK, OpCode.STLOC1); // pack pubs and store.

            // Store m.
            vrf.Emit(OpCode.STLOC6);

            // Check the number of signatures is m. Abort the execution if not.
            vrf.Emit(OpCode.DEPTH); // push the number of signatures onto stack.
            vrf.Emit(OpCode.LDLOC6); // load m.
            vrf.Emit(OpCode.JMPEQ, new ReadOnlySpan<byte>([0])); // here and below short jumps are sufficient. Offset will be filled later.
            var sigsLenCheckEndOffset = vrf.Length;
            vrf.Emit(OpCode.ABORT); // abort the execution if length of the signatures not equal to m.

            // Start the verification itself.
            var checkStartOffset = vrf.Length;

            // Pack signatures and store at LOC0.
            vrf.Emit(OpCode.LDLOC6); // load m.
            vrf.Emit(OpCode.PACK, OpCode.STLOC0);

            // Get message and store it at LOC2.
            // msg = [4-network-magic-bytes-LE, tx-hash-BE]
            vrf.EmitSysCall(ApplicationEngine.System_Runtime_GetNetwork); // push network magic (Integer stackitem), can have 0-5 bytes length serialized.
            // Convert network magic to 4-bytes-length LE byte array representation.
            vrf.EmitPush(0x100000000); // push 0x100000000.
            vrf.Emit(OpCode.ADD, // the result is some new number that is 5 bytes at least when serialized, but first 4 bytes are intact network value (LE).
                    OpCode.PUSH4, // cut the first 4 bytes out of a number that is at least 5 bytes long, 
                    OpCode.LEFT); // the result is 4-bytes-length LE network representation.
            // Retrieve executing transaction hash.
            vrf.EmitSysCall(ApplicationEngine.System_Runtime_GetScriptContainer); // push the script container (executing transaction, actually).
            vrf.Emit(OpCode.PUSH0, OpCode.PICKITEM); // pick 0-th transaction item (the transaction hash).
            // Concatenate network magic and transaction hash.
            vrf.Emit(OpCode.CAT); // this instruction will convert network magic to bytes using BigInteger rules of conversion.
            vrf.Emit(OpCode.STLOC2); // store msg as a local variable #2.

            // Initialize local variables: sigCnt, pubCnt.
            vrf.Emit(OpCode.PUSH0, OpCode.STLOC3, // initialize sigCnt.
            OpCode.PUSH0, OpCode.STLOC4); // initialize pubCnt.

            // Loop condition check.
            var loopStartOffset = vrf.Length;
            vrf.Emit(OpCode.LDLOC3); // load sigCnt.
            vrf.Emit(OpCode.LDLOC6); // load m.
            vrf.Emit(OpCode.GE,     // sigCnt >= m
            OpCode.LDLOC4); // load pubCnt
            vrf.Emit(OpCode.LDLOC5);      // load n.
            vrf.Emit(OpCode.GE, // pubCnt >= n
            OpCode.OR); // sigCnt >= m || pubCnt >= n
            vrf.Emit(OpCode.JMPIF, new ReadOnlySpan<byte>([0])); // jump to the end of the script if (sigCnt >= m || pubCnt >= n).
            var loopConditionOffset = vrf.Length;

            // Loop start. Prepare arguments and call CryptoLib's verifyWithECDsa.
            vrf.EmitPush((byte)NamedCurveHash.secp256k1Keccak256); // push Koblitz curve identifier and Keccak256 hasher.
            vrf.Emit(OpCode.LDLOC0,        // load signatures.
                OpCode.LDLOC3,             // load sigCnt.
                OpCode.PICKITEM,           // pick signature at index sigCnt.
                OpCode.LDLOC1,             // load pubs.
                OpCode.LDLOC4,             // load pubCnt.
                OpCode.PICKITEM,           // pick pub at index pubCnt.
                OpCode.LDLOC2,             // load msg.
                OpCode.PUSH4, OpCode.PACK); // pack 4 arguments for 'verifyWithECDsa' call.
            EmitAppCallNoArgs(vrf, CryptoLib.CryptoLib.Hash, "verifyWithECDsa", CallFlags.None); // emit the call to 'verifyWithECDsa' itself.

            // Update loop variables.
            vrf.Emit(OpCode.LDLOC3, OpCode.ADD, OpCode.STLOC3, // increment sigCnt if signature is valid.
            OpCode.LDLOC4, OpCode.INC, OpCode.STLOC4); // increment pubCnt.

            // End of the loop.
            vrf.Emit(OpCode.JMP, new ReadOnlySpan<byte>([0])); // jump to the start of cycle.
            var loopEndOffset = vrf.Length;
            // Return condition: the number of valid signatures should be equal to m.
            var progRetOffset = vrf.Length;
            vrf.Emit(OpCode.LDLOC3);  // load sigCnt.
            vrf.Emit(OpCode.LDLOC6);      // load m.
            vrf.Emit(OpCode.NUMEQUAL); // push m == sigCnt.

            var vrfScript = vrf.ToArray();

            // Set JMP* instructions offsets. "-1" is for short JMP parameter offset. JMP parameters
            // are relative offsets.
            vrfScript[sigsLenCheckEndOffset - 1] = (byte)(checkStartOffset - sigsLenCheckEndOffset + 2);
            vrfScript[loopEndOffset - 1] = (byte)(loopStartOffset - loopEndOffset + 2);
            vrfScript[loopConditionOffset - 1] = (byte)(progRetOffset - loopConditionOffset + 2);

            // Account is a hash of verification script.
            var acc = vrfScript.ToScriptHash();

            var tx = new Transaction
            {
                Attributes = [],
                NetworkFee = 1_0000_0000,
                Nonce = (uint)Environment.TickCount,
                Script = new byte[Transaction.MaxTransactionSize / 100],
                Signers = [new Signer { Account = acc }],
                SystemFee = 0,
                ValidUntilBlock = 10,
                Version = 0,
                Witnesses = []
            };
            // inv is a builder of witness invocation script corresponding to the public key.
            using ScriptBuilder inv = new();
            for (var i = 0; i < n; i++)
            {
                if (i == 1) // Skip one key since we need only 3 signatures.
                    continue;
                var signData = tx.GetSignData(TestProtocolSettings.Default.Network);
                var sig = Crypto.Sign(signData, keys[i].Item1, ECCurve.Secp256k1, HashAlgorithm.Keccak256);
                inv.EmitPush(sig);
            }

            tx.Witnesses =
            [
                new Witness { InvocationScript = inv.ToArray(), VerificationScript = vrfScript }
            ];

            Assert.AreEqual(VerifyResult.Succeed, tx.VerifyStateIndependent(TestProtocolSettings.Default));

            var snapshotCache = TestBlockchain.GetTestSnapshotCache();

            // Create fake balance to pay the fees.
            var engine = ApplicationEngine.Create(TriggerType.Application, null, snapshotCache,
                settings: TestProtocolSettings.Default, gas: long.MaxValue);
            _ = NativeContract.GAS.Mint(engine, acc, 5_0000_0000, false);

            // We should not use commit here cause once its committed, the value we get from the snapshot can be different
            // from the underline storage. Thought there isn't any issue triggered here, its wrong to use it this way.
            // We should either ignore the commit, or get a new snapshot of the store after the commit.
            // snapshot.Commit();

            // Check that witness verification passes.
            var txVrfContext = new TransactionVerificationContext();
            var conflicts = new List<Transaction>();
            Assert.AreEqual(VerifyResult.Succeed, tx.VerifyStateDependent(TestProtocolSettings.Default, snapshotCache, txVrfContext, conflicts));

            // The resulting witness verification cost for 3/4 multisig is 8389470  * 10e-8GAS. Cost depends on M/N.
            // The resulting witness Invocation script (198 bytes for 3 signatures):
            // NEO-VM 0 > loadbase64"
            // "DEDM23XByPvDK9XRAHRhfGH7/Mp5jdaci3/GpTZ3D9SZx2Zw89tAaOtmQSIutXbCxRQA1kSeUD4AteJGoNXFhFzIDECgeHoey0rY" +
            // "dlFyTVfDJSsuS+VwzC5OtYGCVR2V/MttmLXWA/FWZH/MjmU0obgQXa9zoBxqYQUUJKefivZFxVcTDEAZT6L6ZFybeXbm8+RlVNS7" +
            // "KshusT54d2ImQ6vFvxETphhJOwcQ0yNL6qJKsrLAKAnzicY4az3ct0G35mI17/gQ"
            // READY: loaded 198 instructions
            // NEO-VM 0 > ops
            // INDEX    OPCODE       PARAMETER
            // 0        PUSHDATA1    ccdb75c1c8fbc32bd5d10074617c61fbfcca798dd69c8b7fc6a536770fd499c76670f3db4068eb6641222eb576c2c51400d6449e503e00b5e246a0d5c5845cc8    <<
            // 66       PUSHDATA1    a0787a1ecb4ad87651724d57c3252b2e4be570cc2e4eb58182551d95fccb6d98b5d603f156647fcc8e6534a1b8105daf73a01c6a61051424a79f8af645c55713
            // 132      PUSHDATA1    194fa2fa645c9b7976e6f3e46554d4bb2ac86eb13e7877622643abc5bf1113a618493b0710d3234beaa24ab2b2c02809f389c6386b3ddcb741b7e66235eff810
            //
            //
            // Resulting witness verification script (266 bytes for 3/4 multisig):
            // NEO-VM 0 > loadbase64:
            // "EwwhAwSGRoaDwRISWXj/6HYkWyAGv+c5rKhTm2czUHkmLLJ6DCEDDSb8KtOxquIPBAtfgzgGcPjvXCsqySG6O915/QrwUlEMIQN7" +
            // "TnKuhUtqCVWz4C2SZRq3+mQak2Bmd2rUOPlbtnSiaQwhArYqxMijUqiS/s6xjX4uOmLIwey6rlUj2J10ewIZJ24iFFcHAHVtwHF2" +
            // "Q24oAzhuwHBBxfug4AMAAAAAAQAAAJ4UjUEtUQgwEM6LchBzEHRrbrhsbbiSJEIAGGhrzmlszmoUwB8MD3ZlcmlmeVdpdGhFQ0R" +
            // "zYQwUG/V1qxGJaIQTYQo1oSiGzeC2bHJBYn1bUmuec2ycdCK5a26z"
            // READY: loaded 264 instructions
            // NEO-VM 0 > ops
            // INDEX    OPCODE       PARAMETER
            // 0        PUSH3            <<
            // 1        PUSHDATA1    030486468683c112125978ffe876245b2006bfe739aca8539b67335079262cb27a
            // 36       PUSHDATA1    030d26fc2ad3b1aae20f040b5f83380670f8ef5c2b2ac921ba3bdd79fd0af05251
            // 71       PUSHDATA1    037b4e72ae854b6a0955b3e02d92651ab7fa641a936066776ad438f95bb674a269
            // 106      PUSHDATA1    02b62ac4c8a352a892feceb18d7e2e3a62c8c1ecbaae5523d89d747b0219276e22
            // 141      PUSH4
            // 142      INITSLOT     7 local, 0 arg
            // 145      STLOC5
            // 146      LDLOC5
            // 147      PACK
            // 148      STLOC1
            // 149      STLOC6
            // 150      DEPTH
            // 151      LDLOC6
            // 152      JMPEQ        155 (3/03)
            // 154      ABORT
            // 155      LDLOC6
            // 156      PACK
            // 157      STLOC0
            // 158      SYSCALL      System.Runtime.GetNetwork (c5fba0e0)
            // 163      PUSHINT64    4294967296 (0000000001000000)
            // 172      ADD
            // 173      PUSH4
            // 174      LEFT
            // 175      SYSCALL      System.Runtime.GetScriptContainer (2d510830)
            // 180      PUSH0
            // 181      PICKITEM
            // 182      CAT
            // 183      STLOC2
            // 184      PUSH0
            // 185      STLOC3
            // 186      PUSH0
            // 187      STLOC4
            // 188      LDLOC3
            // 189      LDLOC6
            // 190      GE
            // 191      LDLOC4
            // 192      LDLOC5
            // 193      GE
            // 194      OR
            // 195      JMPIF        261 (66/42)
            // 197      PUSHINT8     122 (7a)
            // 199      LDLOC0
            // 200      LDLOC3
            // 201      PICKITEM
            // 202      LDLOC1
            // 203      LDLOC4
            // 204      PICKITEM
            // 205      LDLOC2
            // 206      PUSH4
            // 207      PACK
            // 208      PUSH0
            // 209      PUSHDATA1    766572696679576974684543447361 ("verifyWithECDsa")
            // 226      PUSHDATA1    1bf575ab1189688413610a35a12886cde0b66c72 ("NNToUmdQBe5n8o53BTzjTFAnSEcpouyy3B", "0x726cb6e0cd8628a1350a611384688911ab75f51b")
            // 248      SYSCALL      System.Contract.Call (627d5b52)
            // 253      LDLOC3
            // 254      ADD
            // 255      STLOC3
            // 256      LDLOC4
            // 257      INC
            // 258      STLOC4
            // 259      JMP          188 (-71/b9)
            // 261      LDLOC3
            // 262      LDLOC6
            // 263      NUMEQUAL
        }

        // EmitAppCallNoArgs is a helper method that emits all parameters of System.Contract.Call interop
        // except the method arguments.
        private static ScriptBuilder EmitAppCallNoArgs(ScriptBuilder builder, UInt160 contractHash, string method, CallFlags f)
        {
            builder.EmitPush((byte)f);
            builder.EmitPush(method);
            builder.EmitPush(contractHash);
            builder.EmitSysCall(ApplicationEngine.System_Contract_Call);
            return builder;
        }

        [TestMethod]
        public void TestVerifyWithECDsa()
        {
            byte[] privR1 = "6e63fda41e9e3aba9bb5696d58a75731f044a9bdc48fe546da571543b2fa460e".HexToBytes();
            ECPoint pubR1 = ECPoint.Parse("04" +
                "cae768e1cf58d50260cab808da8d6d83d5d3ab91eac41cdce577ce5862d73641" +
                "3643bdecd6d21c3b66f122ab080f9219204b10aa8bbceb86c1896974768648f3", ECCurve.Secp256r1);

            byte[] privK1 = "0b5fb3a050385196b327be7d86cbce6e40a04c8832445af83ad19c82103b3ed9".HexToBytes();
            ECPoint pubK1 = ECPoint.Parse("04" +
                "b6363b353c3ee1620c5af58594458aa00abf43a6d134d7c4cb2d901dc0f474fd" +
                "74c94740bd7169aa0b1ef7bc657e824b1d7f4283c547e7ec18c8576acf84418a", ECCurve.Secp256k1);

            byte[] message = Encoding.Default.GetBytes("HelloWorld");

            // secp256r1 + SHA256
            byte[] signature = Crypto.Sign(message, privR1, ECCurve.Secp256r1, HashAlgorithm.SHA256);
            Assert.IsTrue(Crypto.VerifySignature(message, signature, pubR1)); // SHA256 hash is used by default.
            Assert.IsTrue(CallVerifyWithECDsa(message, pubR1, signature, NamedCurveHash.secp256r1SHA256));

            // secp256r1 + Keccak256
            signature = Crypto.Sign(message, privR1, ECCurve.Secp256r1, HashAlgorithm.Keccak256);
            Assert.IsTrue(Crypto.VerifySignature(message, signature, pubR1, HashAlgorithm.Keccak256));
            Assert.IsTrue(CallVerifyWithECDsa(message, pubR1, signature, NamedCurveHash.secp256r1Keccak256));

            // secp256k1 + SHA256
            signature = Crypto.Sign(message, privK1, ECCurve.Secp256k1, HashAlgorithm.SHA256);
            Assert.IsTrue(Crypto.VerifySignature(message, signature, pubK1)); // SHA256 hash is used by default.
            Assert.IsTrue(CallVerifyWithECDsa(message, pubK1, signature, NamedCurveHash.secp256k1SHA256));

            // secp256k1 + Keccak256
            signature = Crypto.Sign(message, privK1, ECCurve.Secp256k1, HashAlgorithm.Keccak256);
            Assert.IsTrue(Crypto.VerifySignature(message, signature, pubK1, HashAlgorithm.Keccak256));
            Assert.IsTrue(CallVerifyWithECDsa(message, pubK1, signature, NamedCurveHash.secp256k1Keccak256));
        }

        private bool CallVerifyWithECDsa(byte[] message, ECPoint pub, byte[] signature, NamedCurveHash curveHash)
        {
            var snapshot = TestBlockchain.GetTestSnapshotCache();
            using (ScriptBuilder script = new())
            {
                script.EmitPush((int)curveHash);
                script.EmitPush(signature);
                script.EmitPush(pub.EncodePoint(true));
                script.EmitPush(message);
                script.EmitPush(4);
                script.Emit(OpCode.PACK);
                script.EmitPush(CallFlags.All);
                script.EmitPush("verifyWithECDsa");
                script.EmitPush(NativeContract.CryptoLib.Hash);
                script.EmitSysCall(ApplicationEngine.System_Contract_Call);

                using var engine = ApplicationEngine.Create(TriggerType.Application, null, snapshot,
                    settings: TestProtocolSettings.Default);
                engine.LoadScript(script.ToArray());
                Assert.AreEqual(VMState.HALT, engine.Execute());
                return engine.ResultStack.Pop().GetBoolean();
            }
        }

        [TestMethod]
        public void TestVerifyWithEd25519()
        {
            // byte[] privateKey = "9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60".HexToBytes();
            byte[] publicKey = "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a".HexToBytes();
            byte[] message = Array.Empty<byte>();
            byte[] signature = ("e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e06522490155" +
                                "5fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b").HexToBytes();

            // Verify using Ed25519 directly
            Assert.IsTrue(Ed25519.Verify(publicKey, message, signature));

            // Verify using CryptoLib.VerifyWithEd25519
            Assert.IsTrue(CallVerifyWithEd25519(message, publicKey, signature));

            // Test with a different message
            byte[] differentMessage = Encoding.UTF8.GetBytes("Different message");
            Assert.IsFalse(CallVerifyWithEd25519(differentMessage, publicKey, signature));

            // Test with an invalid signature
            byte[] invalidSignature = new byte[signature.Length];
            Array.Copy(signature, invalidSignature, signature.Length);
            invalidSignature[0] ^= 0x01; // Flip one bit
            Assert.IsFalse(CallVerifyWithEd25519(message, publicKey, invalidSignature));

            // Test with an invalid public key
            byte[] invalidPublicKey = new byte[publicKey.Length];
            Array.Copy(publicKey, invalidPublicKey, publicKey.Length);
            invalidPublicKey[0] ^= 0x01; // Flip one bit
            Assert.IsFalse(CallVerifyWithEd25519(message, invalidPublicKey, signature));
        }

        private bool CallVerifyWithEd25519(byte[] message, byte[] publicKey, byte[] signature)
        {
            var snapshot = TestBlockchain.GetTestSnapshotCache();
            using (ScriptBuilder script = new())
            {
                script.EmitPush(signature);
                script.EmitPush(publicKey);
                script.EmitPush(message);
                script.EmitPush(3);
                script.Emit(OpCode.PACK);
                script.EmitPush(CallFlags.All);
                script.EmitPush("verifyWithEd25519");
                script.EmitPush(NativeContract.CryptoLib.Hash);
                script.EmitSysCall(ApplicationEngine.System_Contract_Call);

                using var engine = ApplicationEngine.Create(TriggerType.Application, null, snapshot,
                    settings: TestProtocolSettings.Default);
                engine.LoadScript(script.ToArray());
                Assert.AreEqual(VMState.HALT, engine.Execute());
                return engine.ResultStack.Pop().GetBoolean();
            }
        }
    }
}
