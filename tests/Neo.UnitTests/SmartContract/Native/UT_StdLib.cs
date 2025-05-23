// Copyright (C) 2015-2025 The Neo Project.
//
// UT_StdLib.cs file belongs to the neo project and is free
// software distributed under the MIT software license, see the
// accompanying file LICENSE in the main directory of the
// repository or http://www.opensource.org/licenses/mit-license.php
// for more details.
//
// Redistribution and use in source and binary forms with or without
// modifications are permitted.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.Extensions;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using Neo.VM;
using Neo.VM.Types;
using System;
using System.Collections.Generic;
using System.Numerics;
using Array = System.Array;

namespace Neo.UnitTests.SmartContract.Native
{
    [TestClass]
    public class UT_StdLib
    {
        [TestMethod]
        public void TestBinary()
        {
            var data = Array.Empty<byte>();

            CollectionAssert.AreEqual(data, StdLib.Base64Decode(StdLib.Base64Encode(data)));
            CollectionAssert.AreEqual(data, StdLib.Base58Decode(StdLib.Base58Encode(data)));

            data = new byte[] { 1, 2, 3 };

            CollectionAssert.AreEqual(data, StdLib.Base64Decode(StdLib.Base64Encode(data)));
            CollectionAssert.AreEqual(data, StdLib.Base58Decode(StdLib.Base58Encode(data)));
            Assert.AreEqual("AQIDBA==", StdLib.Base64Encode(new byte[] { 1, 2, 3, 4 }));
            Assert.AreEqual("2VfUX", StdLib.Base58Encode(new byte[] { 1, 2, 3, 4 }));
        }

        [TestMethod]
        public void TestItoaAtoi()
        {
            Assert.AreEqual("1", StdLib.Itoa(BigInteger.One, 10));
            Assert.AreEqual("1", StdLib.Itoa(BigInteger.One, 16));
            Assert.AreEqual("-1", StdLib.Itoa(BigInteger.MinusOne, 10));
            Assert.AreEqual("f", StdLib.Itoa(BigInteger.MinusOne, 16));
            Assert.AreEqual("3b9aca00", StdLib.Itoa(1_000_000_000, 16));
            Assert.AreEqual(-1, StdLib.Atoi("-1", 10));
            Assert.AreEqual(1, StdLib.Atoi("+1", 10));
            Assert.AreEqual(-1, StdLib.Atoi("ff", 16));
            Assert.AreEqual(-1, StdLib.Atoi("FF", 16));
            Assert.ThrowsExactly<FormatException>(() => _ = StdLib.Atoi("a", 10));
            Assert.ThrowsExactly<FormatException>(() => _ = StdLib.Atoi("g", 16));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = StdLib.Atoi("a", 11));

            Assert.AreEqual(BigInteger.One, StdLib.Atoi(StdLib.Itoa(BigInteger.One, 10)));
            Assert.AreEqual(BigInteger.MinusOne, StdLib.Atoi(StdLib.Itoa(BigInteger.MinusOne, 10)));
        }

        [TestMethod]
        public void MemoryCompare()
        {
            var snapshotCache = TestBlockchain.GetTestSnapshotCache();

            using (var script = new ScriptBuilder())
            {
                script.EmitDynamicCall(NativeContract.StdLib.Hash, "memoryCompare", "abc", "c");
                script.EmitDynamicCall(NativeContract.StdLib.Hash, "memoryCompare", "abc", "d");
                script.EmitDynamicCall(NativeContract.StdLib.Hash, "memoryCompare", "abc", "abc");
                script.EmitDynamicCall(NativeContract.StdLib.Hash, "memoryCompare", "abc", "abcd");

                using var engine = ApplicationEngine.Create(TriggerType.Application, null, snapshotCache, settings: TestProtocolSettings.Default);
                engine.LoadScript(script.ToArray());

                Assert.AreEqual(engine.Execute(), VMState.HALT);
                Assert.AreEqual(4, engine.ResultStack.Count);

                Assert.AreEqual(-1, engine.ResultStack.Pop<Integer>().GetInteger());
                Assert.AreEqual(0, engine.ResultStack.Pop<Integer>().GetInteger());
                Assert.AreEqual(-1, engine.ResultStack.Pop<Integer>().GetInteger());
                Assert.AreEqual(-1, engine.ResultStack.Pop<Integer>().GetInteger());
            }
        }

        [TestMethod]
        public void CheckDecodeEncode()
        {
            var snapshotCache = TestBlockchain.GetTestSnapshotCache();

            using (ScriptBuilder script = new())
            {
                script.EmitDynamicCall(NativeContract.StdLib.Hash, "base58CheckEncode", new byte[] { 1, 2, 3 });

                using var engine = ApplicationEngine.Create(TriggerType.Application, null, snapshotCache, settings: TestProtocolSettings.Default);
                engine.LoadScript(script.ToArray());

                Assert.AreEqual(engine.Execute(), VMState.HALT);
                Assert.AreEqual(1, engine.ResultStack.Count);

                Assert.AreEqual("3DUz7ncyT", engine.ResultStack.Pop<ByteString>().GetString());
            }

            using (ScriptBuilder script = new())
            {
                script.EmitDynamicCall(NativeContract.StdLib.Hash, "base58CheckDecode", "3DUz7ncyT");

                using var engine = ApplicationEngine.Create(TriggerType.Application, null, snapshotCache, settings: TestProtocolSettings.Default);
                engine.LoadScript(script.ToArray());

                Assert.AreEqual(engine.Execute(), VMState.HALT);
                Assert.AreEqual(1, engine.ResultStack.Count);

                CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, engine.ResultStack.Pop<ByteString>().GetSpan().ToArray());
            }

            // Error

            using (ScriptBuilder script = new())
            {
                script.EmitDynamicCall(NativeContract.StdLib.Hash, "base58CheckDecode", "AA");

                using var engine = ApplicationEngine.Create(TriggerType.Application, null, snapshotCache, settings: TestProtocolSettings.Default);
                engine.LoadScript(script.ToArray());

                Assert.AreEqual(engine.Execute(), VMState.FAULT);
            }

            using (ScriptBuilder script = new())
            {
                script.EmitDynamicCall(NativeContract.StdLib.Hash, "base58CheckDecode", null);

                using var engine = ApplicationEngine.Create(TriggerType.Application, null, snapshotCache, settings: TestProtocolSettings.Default);
                engine.LoadScript(script.ToArray());

                Assert.AreEqual(engine.Execute(), VMState.FAULT);
            }
        }

        [TestMethod]
        public void MemorySearch()
        {
            var snapshotCache = TestBlockchain.GetTestSnapshotCache();

            using (var script = new ScriptBuilder())
            {
                script.EmitDynamicCall(NativeContract.StdLib.Hash, "memorySearch", "abc", "c", 0);
                script.EmitDynamicCall(NativeContract.StdLib.Hash, "memorySearch", "abc", "c", 1);
                script.EmitDynamicCall(NativeContract.StdLib.Hash, "memorySearch", "abc", "c", 2);
                script.EmitDynamicCall(NativeContract.StdLib.Hash, "memorySearch", "abc", "c", 3);
                script.EmitDynamicCall(NativeContract.StdLib.Hash, "memorySearch", "abc", "d", 0);

                using var engine = ApplicationEngine.Create(TriggerType.Application, null, snapshotCache, settings: TestProtocolSettings.Default);
                engine.LoadScript(script.ToArray());

                Assert.AreEqual(engine.Execute(), VMState.HALT);
                Assert.AreEqual(5, engine.ResultStack.Count);

                Assert.AreEqual(-1, engine.ResultStack.Pop<Integer>().GetInteger());
                Assert.AreEqual(-1, engine.ResultStack.Pop<Integer>().GetInteger());
                Assert.AreEqual(2, engine.ResultStack.Pop<Integer>().GetInteger());
                Assert.AreEqual(2, engine.ResultStack.Pop<Integer>().GetInteger());
            }

            using (var script = new ScriptBuilder())
            {
                script.EmitDynamicCall(NativeContract.StdLib.Hash, "memorySearch", "abc", "c", 0, false);
                script.EmitDynamicCall(NativeContract.StdLib.Hash, "memorySearch", "abc", "c", 1, false);
                script.EmitDynamicCall(NativeContract.StdLib.Hash, "memorySearch", "abc", "c", 2, false);
                script.EmitDynamicCall(NativeContract.StdLib.Hash, "memorySearch", "abc", "c", 3, false);
                script.EmitDynamicCall(NativeContract.StdLib.Hash, "memorySearch", "abc", "d", 0, false);

                using var engine = ApplicationEngine.Create(TriggerType.Application, null, snapshotCache, settings: TestProtocolSettings.Default);
                engine.LoadScript(script.ToArray());

                Assert.AreEqual(engine.Execute(), VMState.HALT);
                Assert.AreEqual(5, engine.ResultStack.Count);

                Assert.AreEqual(-1, engine.ResultStack.Pop<Integer>().GetInteger());
                Assert.AreEqual(-1, engine.ResultStack.Pop<Integer>().GetInteger());
                Assert.AreEqual(2, engine.ResultStack.Pop<Integer>().GetInteger());
                Assert.AreEqual(2, engine.ResultStack.Pop<Integer>().GetInteger());
            }

            using (var script = new ScriptBuilder())
            {
                script.EmitDynamicCall(NativeContract.StdLib.Hash, "memorySearch", "abc", "c", 0, true);
                script.EmitDynamicCall(NativeContract.StdLib.Hash, "memorySearch", "abc", "c", 1, true);
                script.EmitDynamicCall(NativeContract.StdLib.Hash, "memorySearch", "abc", "c", 2, true);
                script.EmitDynamicCall(NativeContract.StdLib.Hash, "memorySearch", "abc", "c", 3, true);
                script.EmitDynamicCall(NativeContract.StdLib.Hash, "memorySearch", "abc", "d", 0, true);

                using var engine = ApplicationEngine.Create(TriggerType.Application, null, snapshotCache, settings: TestProtocolSettings.Default);
                engine.LoadScript(script.ToArray());

                Assert.AreEqual(engine.Execute(), VMState.HALT);
                Assert.AreEqual(5, engine.ResultStack.Count);

                Assert.AreEqual(-1, engine.ResultStack.Pop<Integer>().GetInteger());
                Assert.AreEqual(2, engine.ResultStack.Pop<Integer>().GetInteger());
                Assert.AreEqual(-1, engine.ResultStack.Pop<Integer>().GetInteger());
                Assert.AreEqual(-1, engine.ResultStack.Pop<Integer>().GetInteger());
            }
        }

        [TestMethod]
        public void StringSplit()
        {
            var snapshotCache = TestBlockchain.GetTestSnapshotCache();

            using var script = new ScriptBuilder();
            script.EmitDynamicCall(NativeContract.StdLib.Hash, "stringSplit", "a,b", ",");

            using var engine = ApplicationEngine.Create(TriggerType.Application, null, snapshotCache, settings: TestProtocolSettings.Default);
            engine.LoadScript(script.ToArray());

            Assert.AreEqual(engine.Execute(), VMState.HALT);
            Assert.AreEqual(1, engine.ResultStack.Count);

            var arr = engine.ResultStack.Pop<VM.Types.Array>();
            Assert.AreEqual(2, arr.Count);
            Assert.AreEqual("a", arr[0].GetString());
            Assert.AreEqual("b", arr[1].GetString());
        }

        [TestMethod]
        public void StringElementLength()
        {
            var snapshotCache = TestBlockchain.GetTestSnapshotCache();

            using var script = new ScriptBuilder();
            script.EmitDynamicCall(NativeContract.StdLib.Hash, "strLen", "🦆");
            script.EmitDynamicCall(NativeContract.StdLib.Hash, "strLen", "ã");
            script.EmitDynamicCall(NativeContract.StdLib.Hash, "strLen", "a");

            using var engine = ApplicationEngine.Create(TriggerType.Application, null, snapshotCache, settings: TestProtocolSettings.Default);
            engine.LoadScript(script.ToArray());

            Assert.AreEqual(engine.Execute(), VMState.HALT);
            Assert.AreEqual(3, engine.ResultStack.Count);
            Assert.AreEqual(1, engine.ResultStack.Pop().GetInteger());
            Assert.AreEqual(1, engine.ResultStack.Pop().GetInteger());
            Assert.AreEqual(1, engine.ResultStack.Pop().GetInteger());
        }

        [TestMethod]
        public void TestInvalidUtf8Sequence()
        {
            // Simulating invalid UTF-8 byte (0xff) decoded as a UTF-16 char
            const char badChar = (char)0xff;
            var badStr = badChar.ToString();
            var snapshotCache = TestBlockchain.GetTestSnapshotCache();

            using var script = new ScriptBuilder();
            script.EmitDynamicCall(NativeContract.StdLib.Hash, "strLen", badStr);
            script.EmitDynamicCall(NativeContract.StdLib.Hash, "strLen", badStr + "ab");

            using var engine = ApplicationEngine.Create(TriggerType.Application, null, snapshotCache, settings: TestProtocolSettings.Default);
            engine.LoadScript(script.ToArray());

            Assert.AreEqual(engine.Execute(), VMState.HALT);
            Assert.AreEqual(2, engine.ResultStack.Count);
            Assert.AreEqual(3, engine.ResultStack.Pop().GetInteger());
            Assert.AreEqual(1, engine.ResultStack.Pop().GetInteger());
        }

        [TestMethod]
        public void Json_Deserialize()
        {
            var snapshotCache = TestBlockchain.GetTestSnapshotCache();

            // Good

            using (var script = new ScriptBuilder())
            {
                script.EmitDynamicCall(NativeContract.StdLib.Hash, "jsonDeserialize", "123");
                script.EmitDynamicCall(NativeContract.StdLib.Hash, "jsonDeserialize", "null");

                using var engine = ApplicationEngine.Create(TriggerType.Application, null, snapshotCache, settings: TestProtocolSettings.Default);
                engine.LoadScript(script.ToArray());

                Assert.AreEqual(engine.Execute(), VMState.HALT);
                Assert.AreEqual(2, engine.ResultStack.Count);

                engine.ResultStack.Pop<Null>();
                Assert.IsTrue(engine.ResultStack.Pop().GetInteger() == 123);
            }

            // Error 1 - Wrong Json

            using (ScriptBuilder script = new())
            {
                script.EmitDynamicCall(NativeContract.StdLib.Hash, "jsonDeserialize", "***");

                using var engine = ApplicationEngine.Create(TriggerType.Application, null, snapshotCache, settings: TestProtocolSettings.Default);
                engine.LoadScript(script.ToArray());

                Assert.AreEqual(engine.Execute(), VMState.FAULT);
                Assert.AreEqual(0, engine.ResultStack.Count);
            }

            // Error 2 - No decimals

            using (var script = new ScriptBuilder())
            {
                script.EmitDynamicCall(NativeContract.StdLib.Hash, "jsonDeserialize", "123.45");

                using var engine = ApplicationEngine.Create(TriggerType.Application, null, snapshotCache, settings: TestProtocolSettings.Default);
                engine.LoadScript(script.ToArray());

                Assert.AreEqual(engine.Execute(), VMState.FAULT);
                Assert.AreEqual(0, engine.ResultStack.Count);
            }
        }

        [TestMethod]
        public void Json_Serialize()
        {
            var snapshotCache = TestBlockchain.GetTestSnapshotCache();

            // Good

            using (var script = new ScriptBuilder())
            {
                script.EmitDynamicCall(NativeContract.StdLib.Hash, "jsonSerialize", 5);
                script.EmitDynamicCall(NativeContract.StdLib.Hash, "jsonSerialize", true);
                script.EmitDynamicCall(NativeContract.StdLib.Hash, "jsonSerialize", "test");
                script.EmitDynamicCall(NativeContract.StdLib.Hash, "jsonSerialize", new object[] { null });
                script.EmitDynamicCall(NativeContract.StdLib.Hash, "jsonSerialize", new ContractParameter(ContractParameterType.Map)
                {
                    Value = new List<KeyValuePair<ContractParameter, ContractParameter>>() {
                        { new KeyValuePair<ContractParameter, ContractParameter>(
                            new ContractParameter(ContractParameterType.String){ Value="key" },
                            new ContractParameter(ContractParameterType.String){ Value= "value" })
                        }
                    }
                });

                using var engine = ApplicationEngine.Create(TriggerType.Application, null, snapshotCache, settings: TestProtocolSettings.Default);
                engine.LoadScript(script.ToArray());

                Assert.AreEqual(engine.Execute(), VMState.HALT);
                Assert.AreEqual(5, engine.ResultStack.Count);

                Assert.AreEqual("{\"key\":\"value\"}", engine.ResultStack.Pop<ByteString>().GetString());
                Assert.AreEqual("null", engine.ResultStack.Pop<ByteString>().GetString());
                Assert.AreEqual("\"test\"", engine.ResultStack.Pop<ByteString>().GetString());
                Assert.AreEqual("true", engine.ResultStack.Pop<ByteString>().GetString());
                Assert.AreEqual("5", engine.ResultStack.Pop<ByteString>().GetString());
            }

            // Error

            using (var script = new ScriptBuilder())
            {
                script.EmitDynamicCall(NativeContract.StdLib.Hash, "jsonSerialize");

                using var engine = ApplicationEngine.Create(TriggerType.Application, null, snapshotCache, settings: TestProtocolSettings.Default);
                engine.LoadScript(script.ToArray());

                Assert.AreEqual(engine.Execute(), VMState.FAULT);
                Assert.AreEqual(0, engine.ResultStack.Count);
            }
        }

        [TestMethod]
        public void TestRuntime_Serialize()
        {
            var snapshotCache = TestBlockchain.GetTestSnapshotCache();

            // Good

            using ScriptBuilder script = new();
            script.EmitDynamicCall(NativeContract.StdLib.Hash, "serialize", 100);
            script.EmitDynamicCall(NativeContract.StdLib.Hash, "serialize", "test");

            using var engine = ApplicationEngine.Create(TriggerType.Application, null, snapshotCache, settings: TestProtocolSettings.Default);
            engine.LoadScript(script.ToArray());

            Assert.AreEqual(engine.Execute(), VMState.HALT);
            Assert.AreEqual(2, engine.ResultStack.Count);

            Assert.AreEqual(engine.ResultStack.Pop<ByteString>().GetSpan().ToHexString(), "280474657374");
            Assert.AreEqual(engine.ResultStack.Pop<ByteString>().GetSpan().ToHexString(), "210164");
        }

        [TestMethod]
        public void TestRuntime_Deserialize()
        {
            var snapshotCache = TestBlockchain.GetTestSnapshotCache();

            // Good

            using ScriptBuilder script = new();
            script.EmitDynamicCall(NativeContract.StdLib.Hash, "deserialize", "280474657374".HexToBytes());
            script.EmitDynamicCall(NativeContract.StdLib.Hash, "deserialize", "210164".HexToBytes());

            using var engine = ApplicationEngine.Create(TriggerType.Application, null, snapshotCache, settings: TestProtocolSettings.Default);
            engine.LoadScript(script.ToArray());

            Assert.AreEqual(engine.Execute(), VMState.HALT);
            Assert.AreEqual(2, engine.ResultStack.Count);

            Assert.AreEqual(engine.ResultStack.Pop<Integer>().GetInteger(), 100);
            Assert.AreEqual(engine.ResultStack.Pop<ByteString>().GetString(), "test");
        }

        [TestMethod]
        public void TestBase64Url()
        {
            var snapshotCache = TestBlockchain.GetTestSnapshotCache();
            using (var script = new ScriptBuilder())
            {
                // Test encoding
                script.EmitDynamicCall(NativeContract.StdLib.Hash, "base64UrlEncode", "Subject=test@example.com&Issuer=https://example.com");
                script.EmitDynamicCall(NativeContract.StdLib.Hash, "base64UrlDecode", "U3ViamVjdD10ZXN0QGV4YW1wbGUuY29tJklzc3Vlcj1odHRwczovL2V4YW1wbGUuY29t");

                using var engine = ApplicationEngine.Create(TriggerType.Application, null, snapshotCache, settings: TestProtocolSettings.Default);
                engine.LoadScript(script.ToArray());

                Assert.AreEqual(engine.Execute(), VMState.HALT);
                Assert.AreEqual(2, engine.ResultStack.Count);
                Assert.AreEqual("Subject=test@example.com&Issuer=https://example.com", engine.ResultStack.Pop<ByteString>());
                Assert.AreEqual("U3ViamVjdD10ZXN0QGV4YW1wbGUuY29tJklzc3Vlcj1odHRwczovL2V4YW1wbGUuY29t", engine.ResultStack.Pop<ByteString>().GetString());
            }
        }
    }
}
