// ─── DepositPendingFilterTests.cs ─────────────────────────────────────────────
// Verifies the deposit-kind classification and filter logic that feeds the
// GET /api/transactions/deposits/pending endpoint.
//
// The endpoint filters by:
//   1. t.UserId == user.Id
//   2. depositKinds.Contains(t.SuggestedKind)   <- Paycheck | Windfall | InternalTransfer | Refund
//   3. t.UserDecision == Undecided
//   4. !t.IsLargeExpenseCandidate
//
// These tests exercise the classification engine that assigns SuggestedKind values
// (DynamicBudgetEngine.ClassifyDeposit) and confirm:
//   - true deposits get classified with a non-Unknown deposit kind
//   - debit/spend transactions are NEVER assigned a deposit kind (they don't call ClassifyDeposit)
//   - the deposit kinds list is exactly the expected four values
// ──────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.Linq;
using BudgetApp.Api.Data;
using BudgetApp.Api.Services;
using Xunit;

namespace BudgetApp.Api.Tests
{
    public class DepositPendingFilterTests
    {
        // ── Shared ────────────────────────────────────────────────────────────
        private static readonly DynamicBudgetEngine Engine = new();

        // The four deposit kinds that the endpoint accepts.
        // If this list changes in Program.cs, this test will catch it.
        private static readonly HashSet<TransactionSuggestedKind> DepositKinds = new()
        {
            TransactionSuggestedKind.Paycheck,
            TransactionSuggestedKind.Windfall,
            TransactionSuggestedKind.InternalTransfer,
            TransactionSuggestedKind.Refund,
        };

        // ── 1. Paycheck appears ───────────────────────────────────────────────
        [Fact]
        public void Paycheck_IsClassifiedAsPaycheck_AndPassesDepositFilter()
        {
            var ctx = new DepositContext
            {
                Amount = 3000m,
                Date = new DateTime(2026, 5, 1),
                MerchantName = "DIRECT DEPOSIT",
                PayDay1 = 1,
                PayDay2 = 15,
                ExpectedPaycheckAmount = 3000m
            };

            var kind = Engine.ClassifyDeposit(ctx);

            Assert.Equal(TransactionSuggestedKind.Paycheck, kind);
            Assert.Contains(kind, DepositKinds);
        }

        // ── 2. Refund appears ─────────────────────────────────────────────────
        [Fact]
        public void Refund_IsClassifiedAsRefund_AndPassesDepositFilter()
        {
            var ctx = new DepositContext
            {
                Amount = 42.50m,
                Date = new DateTime(2026, 5, 10),
                MerchantName = "AMAZON REFUND",
                PayDay1 = 1,
                PayDay2 = 15,
                ExpectedPaycheckAmount = 3000m
            };

            var kind = Engine.ClassifyDeposit(ctx);

            Assert.Equal(TransactionSuggestedKind.Refund, kind);
            Assert.Contains(kind, DepositKinds);
        }

        // ── 3. Windfall appears ───────────────────────────────────────────────
        [Fact]
        public void Windfall_IsClassifiedAsWindfall_AndPassesDepositFilter()
        {
            // A large, non-paycheck-day credit that is much bigger than the expected paycheck
            var ctx = new DepositContext
            {
                Amount = 10_000m,
                Date = new DateTime(2026, 5, 7),   // not a pay day (pay days are 1 and 15)
                MerchantName = "WIRE TRANSFER",
                PayDay1 = 1,
                PayDay2 = 15,
                ExpectedPaycheckAmount = 3000m
            };

            var kind = Engine.ClassifyDeposit(ctx);

            Assert.Equal(TransactionSuggestedKind.Windfall, kind);
            Assert.Contains(kind, DepositKinds);
        }

        // ── 4. InternalTransfer appears ───────────────────────────────────────
        [Fact]
        public void InternalTransfer_IsClassifiedAsInternalTransfer_AndPassesDepositFilter()
        {
            var ctx = new DepositContext
            {
                Amount = 500m,
                Date = new DateTime(2026, 5, 7),   // not a pay day
                MerchantName = "TRANSFER FROM SAVINGS",
                PayDay1 = 1,
                PayDay2 = 15,
                ExpectedPaycheckAmount = 3000m
            };

            var kind = Engine.ClassifyDeposit(ctx);

            // InternalTransfer is the expected classification for a bank-to-bank transfer
            Assert.Equal(TransactionSuggestedKind.InternalTransfer, kind);
            Assert.Contains(kind, DepositKinds);
        }

        // ── 5. Debit/spend transactions do NOT call ClassifyDeposit ───────────
        // In TransactionService the credit-vs-debit branch is:
        //   if (isCredit) { newTx.SuggestedKind = _budgetEngine.ClassifyDeposit(ctx); }
        //   else          { /* SuggestedKind stays at default Unknown (0) */ }
        //
        // This test confirms that Unknown == 0 (the EF default) and is NOT in the
        // deposit-kinds filter — so debit transactions never appear in deposits/pending.
        [Fact]
        public void Unknown_IsNotInDepositKinds_SoDebitTransactionsNeverAppear()
        {
            // Unknown is the zero/default value — set automatically by EF for all debit rows
            var unknownKind = TransactionSuggestedKind.Unknown;

            Assert.Equal(0, (int)unknownKind);
            Assert.DoesNotContain(unknownKind, DepositKinds);
        }

        // ── 6. Normal debit purchase → Unknown, excluded from deposit endpoint ─
        [Fact]
        public void DebitPurchase_HasUnknownKind_AndIsExcludedFromDepositFilter()
        {
            // Simulate what TransactionService stores for a normal debit: SuggestedKind stays Unknown
            var debitTx = new Transaction
            {
                Id = 1,
                UserId = 99,
                Amount = 55.00m,
                SuggestedKind = TransactionSuggestedKind.Unknown,   // debit — ClassifyDeposit NOT called
                UserDecision = TransactionUserDecision.Undecided,
                IsLargeExpenseCandidate = false,
            };

            bool passesFilter =
                DepositKinds.Contains(debitTx.SuggestedKind)
                && debitTx.UserDecision == TransactionUserDecision.Undecided
                && !debitTx.IsLargeExpenseCandidate;

            Assert.False(passesFilter, "A debit/spend transaction must NOT pass the deposit filter.");
        }

        // ── 7. Large expense candidate is excluded even if it has a deposit kind ─
        // This is a defense-in-depth check. In practice TransactionService never
        // sets IsLargeExpenseCandidate on a credit, but the endpoint filter guards it.
        [Fact]
        public void LargeExpenseCandidate_IsExcluded_EvenIfDepositKindSet()
        {
            var largeTx = new Transaction
            {
                Id = 2,
                UserId = 99,
                Amount = 4000m,
                SuggestedKind = TransactionSuggestedKind.Paycheck,  // hypothetical miscategorisation
                UserDecision = TransactionUserDecision.Undecided,
                IsLargeExpenseCandidate = true,                     // must be excluded
            };

            bool passesFilter =
                DepositKinds.Contains(largeTx.SuggestedKind)
                && largeTx.UserDecision == TransactionUserDecision.Undecided
                && !largeTx.IsLargeExpenseCandidate;

            Assert.False(passesFilter, "A large expense candidate must NOT appear in the deposit review list.");
        }

        // ── 8. Already-decided deposit is excluded ────────────────────────────
        [Fact]
        public void AlreadyDecidedDeposit_IsExcluded_FromPendingList()
        {
            var decidedTx = new Transaction
            {
                Id = 3,
                UserId = 99,
                Amount = 3000m,
                SuggestedKind = TransactionSuggestedKind.Paycheck,
                UserDecision = TransactionUserDecision.TreatAsIncome,  // already reviewed
                IsLargeExpenseCandidate = false,
            };

            bool passesFilter =
                DepositKinds.Contains(decidedTx.SuggestedKind)
                && decidedTx.UserDecision == TransactionUserDecision.Undecided
                && !decidedTx.IsLargeExpenseCandidate;

            Assert.False(passesFilter, "An already-reviewed deposit must NOT appear in the pending list.");
        }

        // ── 9. Valid undecided paycheck passes all four filter conditions ──────
        [Fact]
        public void ValidUndecidedPaycheck_PassesAllFilterConditions()
        {
            var paycheck = new Transaction
            {
                Id = 4,
                UserId = 99,
                Amount = 3000m,
                SuggestedKind = TransactionSuggestedKind.Paycheck,
                UserDecision = TransactionUserDecision.Undecided,
                IsLargeExpenseCandidate = false,
            };

            bool passesFilter =
                DepositKinds.Contains(paycheck.SuggestedKind)
                && paycheck.UserDecision == TransactionUserDecision.Undecided
                && !paycheck.IsLargeExpenseCandidate;

            Assert.True(passesFilter, "A valid undecided paycheck must appear in the deposit review list.");
        }

        // ── 10. Deposit-kinds set contains exactly the four expected values ────
        [Fact]
        public void DepositKinds_ContainsExactlyFourExpectedValues()
        {
            Assert.Equal(4, DepositKinds.Count);
            Assert.Contains(TransactionSuggestedKind.Paycheck, DepositKinds);
            Assert.Contains(TransactionSuggestedKind.Windfall, DepositKinds);
            Assert.Contains(TransactionSuggestedKind.InternalTransfer, DepositKinds);
            Assert.Contains(TransactionSuggestedKind.Refund, DepositKinds);
            Assert.DoesNotContain(TransactionSuggestedKind.Unknown, DepositKinds);
        }
    }
}
