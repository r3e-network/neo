// Copyright (C) 2015-2025 The Neo Project.
//
// Nep17NativeContractExtensions.cs file belongs to the neo project and is free
// software distributed under the MIT software license, see the
// accompanying file LICENSE in the main directory of the
// repository or http://www.opensource.org/licenses/mit-license.php
// for more details.
//
// Redistribution and use in source and binary forms with or without
// modifications are permitted.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.Extensions;
using Neo.IO;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using Neo.VM;
using Neo.VM.Types;
using System.IO;
using System.Numerics;
using Array = System.Array;
using Boolean = Neo.VM.Types.Boolean;

namespace Neo.UnitTests.Extensions
{
    public static class Nep17NativeContractExtensions
    {
        internal class ManualWitness : IVerifiable
        {
            private readonly UInt160[] _hashForVerify;

            public int Size => 0;

            public Witness[] Witnesses { get; set; }

            public ManualWitness(params UInt160[] hashForVerify)
            {
                _hashForVerify = hashForVerify ?? Array.Empty<UInt160>();
            }

            public void Deserialize(ref MemoryReader reader) { }

            public void DeserializeUnsigned(ref MemoryReader reader) { }

            public UInt160[] GetScriptHashesForVerifying(DataCache snapshot) => _hashForVerify;

            public void Serialize(BinaryWriter writer) { }

            public void SerializeUnsigned(BinaryWriter writer) { }
        }

        public static bool Transfer(this NativeContract contract, DataCache snapshot, byte[] from, byte[] to, BigInteger amount, bool signFrom, Block persistingBlock)
        {
            using var engine = ApplicationEngine.Create(TriggerType.Application,
                new ManualWitness(signFrom ? new UInt160(from) : null), snapshot, persistingBlock, settings: TestProtocolSettings.Default);

            using var script = new ScriptBuilder();
            script.EmitDynamicCall(contract.Hash, "transfer", from, to, amount, null);
            engine.LoadScript(script.ToArray());

            if (engine.Execute() == VMState.FAULT)
            {
                throw engine.FaultException;
            }

            var result = engine.ResultStack.Pop();
            Assert.IsInstanceOfType(result, typeof(Boolean));

            return result.GetBoolean();
        }

        public static BigInteger TotalSupply(this NativeContract contract, DataCache snapshot)
        {
            using var engine = ApplicationEngine.Create(TriggerType.Application, null, snapshot, settings: TestProtocolSettings.Default);

            using var script = new ScriptBuilder();
            script.EmitDynamicCall(contract.Hash, "totalSupply");
            engine.LoadScript(script.ToArray());

            Assert.AreEqual(VMState.HALT, engine.Execute());

            var result = engine.ResultStack.Pop();
            Assert.IsInstanceOfType(result, typeof(Integer));

            return result.GetInteger();
        }

        public static BigInteger BalanceOf(this NativeContract contract, DataCache snapshot, byte[] account)
        {
            using var engine = ApplicationEngine.Create(TriggerType.Application, null, snapshot, settings: TestProtocolSettings.Default);

            using var script = new ScriptBuilder();
            script.EmitDynamicCall(contract.Hash, "balanceOf", account);
            engine.LoadScript(script.ToArray());

            Assert.AreEqual(VMState.HALT, engine.Execute());

            var result = engine.ResultStack.Pop();
            Assert.IsInstanceOfType(result, typeof(Integer));

            return result.GetInteger();
        }

        public static BigInteger Decimals(this NativeContract contract, DataCache snapshot)
        {
            using var engine = ApplicationEngine.Create(TriggerType.Application, null, snapshot, settings: TestProtocolSettings.Default);

            using var script = new ScriptBuilder();
            script.EmitDynamicCall(contract.Hash, "decimals");
            engine.LoadScript(script.ToArray());

            Assert.AreEqual(VMState.HALT, engine.Execute());

            var result = engine.ResultStack.Pop();
            Assert.IsInstanceOfType(result, typeof(Integer));

            return result.GetInteger();
        }

        public static string Symbol(this NativeContract contract, DataCache snapshot)
        {
            using var engine = ApplicationEngine.Create(TriggerType.Application, null, snapshot, settings: TestProtocolSettings.Default);

            using var script = new ScriptBuilder();
            script.EmitDynamicCall(contract.Hash, "symbol");
            engine.LoadScript(script.ToArray());

            Assert.AreEqual(VMState.HALT, engine.Execute());

            var result = engine.ResultStack.Pop();
            Assert.IsInstanceOfType(result, typeof(ByteString));

            return result.GetString();
        }
    }
}
