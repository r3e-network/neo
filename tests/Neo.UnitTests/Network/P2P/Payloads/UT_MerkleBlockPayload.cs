// Copyright (C) 2015-2025 The Neo Project.
//
// UT_MerkleBlockPayload.cs file belongs to the neo project and is free
// software distributed under the MIT software license, see the
// accompanying file LICENSE in the main directory of the
// repository or http://www.opensource.org/licenses/mit-license.php
// for more details.
//
// Redistribution and use in source and binary forms with or without
// modifications are permitted.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.Extensions;
using Neo.Network.P2P.Payloads;
using System;
using System.Collections;

namespace Neo.UnitTests.Network.P2P.Payloads
{
    [TestClass]
    public class UT_MerkleBlockPayload
    {
        private NeoSystem _system;

        [TestInitialize]
        public void TestSetup()
        {
            _system = TestBlockchain.GetSystem();
        }

        [TestMethod]
        public void Size_Get()
        {
            var test = MerkleBlockPayload.Create(_system.GenesisBlock, new BitArray(1024, false));
            Assert.AreEqual(247, test.Size); // 239 + nonce

            test = MerkleBlockPayload.Create(_system.GenesisBlock, new BitArray(0, false));
            Assert.AreEqual(119, test.Size); // 111 + nonce
        }

        [TestMethod]
        public void DeserializeAndSerialize()
        {
            var test = MerkleBlockPayload.Create(_system.GenesisBlock, new BitArray(2, false));
            var clone = test.ToArray().AsSerializable<MerkleBlockPayload>();

            Assert.AreEqual(test.TxCount, clone.TxCount);
            Assert.AreEqual(test.Hashes.Length, clone.Hashes.Length);
            Assert.AreEqual(test.Flags.Length, clone.Flags.Length);
            CollectionAssert.AreEqual(test.Hashes, clone.Hashes);
            Assert.IsTrue(test.Flags.Span.SequenceEqual(clone.Flags.Span));
        }
    }
}
