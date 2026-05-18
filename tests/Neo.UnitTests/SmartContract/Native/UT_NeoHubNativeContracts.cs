// Copyright (C) 2015-2026 The Neo Project.
//
// NeoHub is intentionally maintained as deployable contracts and plugins, not
// as L1 native contracts in the r3e/neo-n3-core branch.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.SmartContract.Native;
using System;
using System.IO;
using System.Linq;

namespace Neo.UnitTests.SmartContract.Native
{
    [TestClass]
    public class UT_NeoHubNativeContracts
    {
        [TestMethod]
        public void NeoHub_IsNotRegisteredAsL1NativeContracts()
        {
            var neoHubContracts = NativeContract.Contracts
                .Where(c => c.Name.StartsWith("NeoHub", StringComparison.Ordinal))
                .Select(c => c.Name)
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.AreEqual(0, neoHubContracts.Length,
                "NeoHub must be deployed through contracts/NeoHub.* and optional plugins, not registered as L1 native contracts: "
                + string.Join(", ", neoHubContracts));
        }

        [TestMethod]
        public void NeoHub_SourceDoesNotLiveUnderNativeContracts()
        {
            var root = FindRepositoryRoot();
            var nativeNeoHubPath = Path.Combine(root, "src", "Neo", "SmartContract", "Native", "NeoHub");

            Assert.IsFalse(Directory.Exists(nativeNeoHubPath),
                "NeoHub business contracts belong in r3e-network/neo-n4 contracts/NeoHub.* as deployed contracts, not under Neo core Native/NeoHub.");
        }

        private static string FindRepositoryRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "neo.sln")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Could not find repository root containing neo.sln.");
        }
    }
}
