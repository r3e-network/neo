// Copyright (C) 2015-2025 The Neo Project.
//
// TransactionVerificationContext.cs file belongs to the neo project and is free
// software distributed under the MIT software license, see the
// accompanying file LICENSE in the main directory of the
// repository or http://www.opensource.org/licenses/mit-license.php
// for more details.
//
// Redistribution and use in source and binary forms with or without
// modifications are permitted.

using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.SmartContract.Native;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Neo.Ledger
{
    /// <summary>
    /// The context used to verify the transaction.
    /// </summary>
    public class TransactionVerificationContext
    {
        /// <summary>
        /// Store all verified unsorted transactions' senders' fee currently in the memory pool.
        /// </summary>
        private readonly Dictionary<UInt160, BigInteger> _senderFee = [];

        /// <summary>
        /// Store oracle responses
        /// </summary>
        private readonly Dictionary<ulong, UInt256> _oracleResponses = [];

        /// <summary>
        /// Adds a verified <see cref="Transaction"/> to the context.
        /// </summary>
        /// <param name="tx">The verified <see cref="Transaction"/>.</param>
        public void AddTransaction(Transaction tx)
        {
            var oracle = tx.GetAttribute<OracleResponse>();
            if (oracle != null) _oracleResponses.Add(oracle.Id, tx.Hash);

            if (_senderFee.TryGetValue(tx.Sender, out var value))
                _senderFee[tx.Sender] = value + tx.SystemFee + tx.NetworkFee;
            else
                _senderFee.Add(tx.Sender, tx.SystemFee + tx.NetworkFee);
        }

        /// <summary>
        /// Determine whether the specified <see cref="Transaction"/> conflicts with other transactions.
        /// </summary>
        /// <param name="tx">The specified <see cref="Transaction"/>.</param>
        /// <param name="conflictingTxs">The list of <see cref="Transaction"/> that conflicts with the specified one and are to be removed from the pool.</param>
        /// <param name="snapshot">The snapshot used to verify the <see cref="Transaction"/>.</param>
        /// <returns><see langword="true"/> if the <see cref="Transaction"/> passes the check; otherwise, <see langword="false"/>.</returns>
        public bool CheckTransaction(Transaction tx, IEnumerable<Transaction> conflictingTxs, DataCache snapshot)
        {
            var balance = NativeContract.GAS.BalanceOf(snapshot, tx.Sender);
            _senderFee.TryGetValue(tx.Sender, out var totalSenderFeeFromPool);

            var expectedFee = tx.SystemFee + tx.NetworkFee + totalSenderFeeFromPool;
            foreach (var conflictTx in conflictingTxs.Where(c => c.Sender.Equals(tx.Sender)))
                expectedFee -= conflictTx.NetworkFee + conflictTx.SystemFee;
            if (balance < expectedFee) return false;

            var oracle = tx.GetAttribute<OracleResponse>();
            if (oracle != null && _oracleResponses.ContainsKey(oracle.Id))
                return false;

            return true;
        }

        /// <summary>
        /// Removes a <see cref="Transaction"/> from the context.
        /// </summary>
        /// <param name="tx">The <see cref="Transaction"/> to be removed.</param>
        public void RemoveTransaction(Transaction tx)
        {
            if ((_senderFee[tx.Sender] -= tx.SystemFee + tx.NetworkFee) == 0)
                _senderFee.Remove(tx.Sender);

            var oracle = tx.GetAttribute<OracleResponse>();
            if (oracle != null)
                _oracleResponses.Remove(oracle.Id);
        }
    }
}
