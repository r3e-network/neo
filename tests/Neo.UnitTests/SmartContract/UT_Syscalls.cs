// Copyright (C) 2015-2025 The Neo Project.
//
// UT_Syscalls.cs file belongs to the neo project and is free
// software distributed under the MIT software license, see the
// accompanying file LICENSE in the main directory of the
// repository or http://www.opensource.org/licenses/mit-license.php
// for more details.
//
// Redistribution and use in source and binary forms with or without
// modifications are permitted.

using Akka.TestKit.MsTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.Cryptography.ECC;
using Neo.Extensions;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using Neo.UnitTests.Extensions;
using Neo.VM;
using Neo.VM.Types;
using System.Linq;
using Array = System.Array;

namespace Neo.UnitTests.SmartContract
{
    [TestClass]
    public partial class UT_Syscalls : TestKit
    {
        private DataCache _snapshotCache;

        [TestInitialize]
        public void TestSetup()
        {
            _snapshotCache = TestBlockchain.GetTestSnapshotCache();
        }

        [TestMethod]
        public void System_Blockchain_GetBlock()
        {
            var tx = new Transaction()
            {
                Script = new byte[] { 0x01 },
                Attributes = [],
                Signers = [],
                NetworkFee = 0x02,
                SystemFee = 0x03,
                Nonce = 0x04,
                ValidUntilBlock = 0x05,
                Version = 0x06,
                Witnesses = [new() { VerificationScript = new byte[] { 0x07 } }],
            };

            var block = new TrimmedBlock()
            {
                Header = new Header
                {
                    Index = 0,
                    Timestamp = 2,
                    Witness = Witness.Empty,
                    PrevHash = UInt256.Zero,
                    MerkleRoot = UInt256.Zero,
                    PrimaryIndex = 1,
                    NextConsensus = UInt160.Zero,
                },
                Hashes = [tx.Hash]
            };

            var snapshot = _snapshotCache.CloneCache();

            using ScriptBuilder script = new();
            script.EmitDynamicCall(NativeContract.Ledger.Hash, "getBlock", block.Hash.ToArray());

            // Without block

            var engine = ApplicationEngine.Create(TriggerType.Application, null, snapshot, settings: TestProtocolSettings.Default);
            engine.LoadScript(script.ToArray());

            Assert.AreEqual(engine.Execute(), VMState.HALT);
            Assert.AreEqual(1, engine.ResultStack.Count);
            Assert.IsTrue(engine.ResultStack.Peek().IsNull);

            // Not traceable block

            const byte Prefix_Transaction = 11;
            const byte Prefix_CurrentBlock = 12;

            TestUtils.BlocksAdd(snapshot, block.Hash, block);

            var height = snapshot[NativeContract.Ledger.CreateStorageKey(Prefix_CurrentBlock)].GetInteroperable<HashIndexState>();
            height.Index = block.Index + TestProtocolSettings.Default.MaxTraceableBlocks;

            snapshot.Add(NativeContract.Ledger.CreateStorageKey(Prefix_Transaction, tx.Hash), new StorageItem(new TransactionState
            {
                BlockIndex = block.Index,
                Transaction = tx
            }));

            engine = ApplicationEngine.Create(TriggerType.Application, null, snapshot, settings: TestProtocolSettings.Default);
            engine.LoadScript(script.ToArray());

            Assert.AreEqual(engine.Execute(), VMState.HALT);
            Assert.AreEqual(1, engine.ResultStack.Count);
            Assert.IsTrue(engine.ResultStack.Peek().IsNull);

            // With block

            height = snapshot[NativeContract.Ledger.CreateStorageKey(Prefix_CurrentBlock)].GetInteroperable<HashIndexState>();
            height.Index = block.Index;

            engine = ApplicationEngine.Create(TriggerType.Application, null, snapshot, settings: TestProtocolSettings.Default);
            engine.LoadScript(script.ToArray());

            Assert.AreEqual(engine.Execute(), VMState.HALT);
            Assert.AreEqual(1, engine.ResultStack.Count);

            var array = engine.ResultStack.Pop<VM.Types.Array>();
            Assert.AreEqual(block.Hash, new UInt256(array[0].GetSpan()));
        }

        [TestMethod]
        public void System_ExecutionEngine_GetScriptContainer()
        {
            var snapshot = _snapshotCache.CloneCache();
            using ScriptBuilder script = new();
            script.EmitSysCall(ApplicationEngine.System_Runtime_GetScriptContainer);

            // Without tx

            var engine = ApplicationEngine.Create(TriggerType.Application, null, snapshot);
            engine.LoadScript(script.ToArray());

            Assert.AreEqual(engine.Execute(), VMState.FAULT);
            Assert.AreEqual(0, engine.ResultStack.Count);

            // With tx

            var tx = new Transaction()
            {
                Script = new byte[] { 0x01 },
                Signers =
                [
                    new()
                    {
                        Account = UInt160.Zero,
                        Scopes = WitnessScope.None,
                        AllowedContracts = [],
                        AllowedGroups = [],
                        Rules = [],
                    }
                ],
                Attributes = [],
                NetworkFee = 0x02,
                SystemFee = 0x03,
                Nonce = 0x04,
                ValidUntilBlock = 0x05,
                Version = 0x06,
                Witnesses = [new() { VerificationScript = new byte[] { 0x07 } }],
            };

            engine = ApplicationEngine.Create(TriggerType.Application, tx, snapshot);
            engine.LoadScript(script.ToArray());

            Assert.AreEqual(engine.Execute(), VMState.HALT);
            Assert.AreEqual(1, engine.ResultStack.Count);

            var array = engine.ResultStack.Pop<VM.Types.Array>();
            Assert.AreEqual(tx.Hash, new UInt256(array[0].GetSpan()));
        }

        [TestMethod]
        public void System_Runtime_GasLeft()
        {
            var snapshot = _snapshotCache.CloneCache();

            using (var script = new ScriptBuilder())
            {
                script.Emit(OpCode.NOP);
                script.EmitSysCall(ApplicationEngine.System_Runtime_GasLeft);
                script.Emit(OpCode.NOP);
                script.EmitSysCall(ApplicationEngine.System_Runtime_GasLeft);
                script.Emit(OpCode.NOP);
                script.Emit(OpCode.NOP);
                script.Emit(OpCode.NOP);
                script.EmitSysCall(ApplicationEngine.System_Runtime_GasLeft);

                // Execute

                var engine = ApplicationEngine.Create(TriggerType.Application, null, snapshot, gas: 100_000_000);
                engine.LoadScript(script.ToArray());
                Assert.AreEqual(engine.Execute(), VMState.HALT);

                // Check the results

                CollectionAssert.AreEqual
                    (
                    engine.ResultStack.Select(u => (int)u.GetInteger()).ToArray(),
                    new int[] { 99_999_490, 99_998_980, 99_998_410 }
                    );
            }

            // Check test mode

            using (var script = new ScriptBuilder())
            {
                script.EmitSysCall(ApplicationEngine.System_Runtime_GasLeft);

                // Execute

                var engine = ApplicationEngine.Create(TriggerType.Application, null, snapshot);
                engine.LoadScript(script.ToArray());

                // Check the results

                Assert.AreEqual(engine.Execute(), VMState.HALT);
                Assert.AreEqual(1, engine.ResultStack.Count);
                Assert.IsInstanceOfType(engine.ResultStack.Peek(), typeof(Integer));
                Assert.AreEqual(1999999520, engine.ResultStack.Pop().GetInteger());
            }
        }

        [TestMethod]
        public void System_Runtime_GetInvocationCounter()
        {
            var snapshot = _snapshotCache.CloneCache();
            ContractState contractA, contractB, contractC;

            // Create dummy contracts

            using (var script = new ScriptBuilder())
            {
                script.EmitSysCall(ApplicationEngine.System_Runtime_GetInvocationCounter);

                contractA = TestUtils.GetContract(new byte[] { (byte)OpCode.DROP, (byte)OpCode.DROP }.Concat(script.ToArray()).ToArray());
                contractB = TestUtils.GetContract(new byte[] { (byte)OpCode.DROP, (byte)OpCode.DROP, (byte)OpCode.NOP }.Concat(script.ToArray()).ToArray());
                contractC = TestUtils.GetContract(new byte[] { (byte)OpCode.DROP, (byte)OpCode.DROP, (byte)OpCode.NOP, (byte)OpCode.NOP }.Concat(script.ToArray()).ToArray());
                contractA.Hash = contractA.Script.Span.ToScriptHash();
                contractB.Hash = contractB.Script.Span.ToScriptHash();
                contractC.Hash = contractC.Script.Span.ToScriptHash();

                // Init A,B,C contracts
                // First two drops is for drop method and arguments

                snapshot.DeleteContract(contractA.Hash);
                snapshot.DeleteContract(contractB.Hash);
                snapshot.DeleteContract(contractC.Hash);
                contractA.Manifest = TestUtils.CreateManifest("dummyMain", ContractParameterType.Any, ContractParameterType.String, ContractParameterType.Integer);
                contractB.Manifest = TestUtils.CreateManifest("dummyMain", ContractParameterType.Any, ContractParameterType.String, ContractParameterType.Integer);
                contractC.Manifest = TestUtils.CreateManifest("dummyMain", ContractParameterType.Any, ContractParameterType.String, ContractParameterType.Integer);
                snapshot.AddContract(contractA.Hash, contractA);
                snapshot.AddContract(contractB.Hash, contractB);
                snapshot.AddContract(contractC.Hash, contractC);
            }

            // Call A,B,B,C

            using (var script = new ScriptBuilder())
            {
                script.EmitDynamicCall(contractA.Hash, "dummyMain", "0", 1);
                script.EmitDynamicCall(contractB.Hash, "dummyMain", "0", 1);
                script.EmitDynamicCall(contractB.Hash, "dummyMain", "0", 1);
                script.EmitDynamicCall(contractC.Hash, "dummyMain", "0", 1);

                // Execute

                var engine = ApplicationEngine.Create(TriggerType.Application, null, snapshot, null, ProtocolSettings.Default);
                engine.LoadScript(script.ToArray());
                Assert.AreEqual(VMState.HALT, engine.Execute());

                // Check the results

                CollectionAssert.AreEqual(
                    engine.ResultStack.Select(u => (int)u.GetInteger()).ToArray(),
                    new int[] { 1 /* A */, 1 /* B */, 2 /* B */, 1  /* C */});
            }
        }
    }
}
