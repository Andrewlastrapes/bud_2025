using System;
using BudgetApp.Api.Data;
using BudgetApp.Api.Services;
using Xunit;

namespace BudgetApp.Api.Tests
{
    // ─── Existing: Deposit Classification ─────────────────────────────────────

    public class DynamicBudgetEngineTests
    {
        [Fact]
        public void ClassifyDeposit_ReturnsPaycheck_WhenTimingAndAmountMatch()
        {
            var engine = new DynamicBudgetEngine();
            var ctx = new DepositContext
            {
                Amount = 3000m,
                Date = new DateTime(2025, 02, 15),
                MerchantName = "ACME PAYROLL",
                PayDay1 = 1,
                PayDay2 = 15,
                ExpectedPaycheckAmount = 3100m  // within 15%
            };

            var kind = engine.ClassifyDeposit(ctx);

            Assert.Equal(TransactionSuggestedKind.Paycheck, kind);
        }

        [Fact]
        public void ClassifyDeposit_ReturnsWindfall_WhenAmountDoesNotMatch()
        {
            var engine = new DynamicBudgetEngine();
            var ctx = new DepositContext
            {
                Amount = 500m,
                Date = new DateTime(2025, 02, 16),
                MerchantName = "Random Refund",
                PayDay1 = 1,
                PayDay2 = 15,
                ExpectedPaycheckAmount = 3000m
            };

            var kind = engine.ClassifyDeposit(ctx);

            Assert.Equal(TransactionSuggestedKind.Windfall, kind);
        }

        [Fact]
        public void ClassifyDeposit_TreatsCloseButTooSmallAsWindfall()
        {
            var engine = new DynamicBudgetEngine();
            var ctx = new DepositContext
            {
                Amount = 2500m, // ~17% below 3000
                Date = new DateTime(2025, 02, 15),
                MerchantName = "ACME PAYROLL",
                PayDay1 = 1,
                PayDay2 = 15,
                ExpectedPaycheckAmount = 3000m
            };

            var kind = engine.ClassifyDeposit(ctx);

            Assert.Equal(TransactionSuggestedKind.Windfall, kind);
        }

        [Fact]
        public void ClassifyDeposit_TreatsFarFromPaydayCreditAsWindfall()
        {
            var engine = new DynamicBudgetEngine();
            var ctx = new DepositContext
            {
                Amount = 3000m,
                Date = new DateTime(2025, 02, 25), // clearly far from 1 and 15
                MerchantName = "ACME PAYROLL",
                PayDay1 = 1,
                PayDay2 = 15,
                ExpectedPaycheckAmount = 3000m
            };

            var kind = engine.ClassifyDeposit(ctx);

            Assert.Equal(TransactionSuggestedKind.Windfall, kind);
        }
    }

    // ─── New: Pay Cycle Calculation ────────────────────────────────────────────

    public class PayCycleCalculationTests
    {
        private readonly DynamicBudgetEngine _engine = new();

        [Fact]
        public void CalculatePreviousPaycheckDate_NextIsSecondDay_ReturnsSameMonthFirstDay()
        {
            // Pay days: 1 and 15. Next paycheck is the 15th → previous is the 1st same month.
            var next = new DateTime(2026, 4, 15);
            var prev = _engine.CalculatePreviousPaycheckDate(1, 15, next);

            Assert.Equal(new DateTime(2026, 4, 1), prev);
        }

        [Fact]
        public void CalculatePreviousPaycheckDate_NextIsFirstDay_ReturnsPriorMonthSecondDay()
        {
            // Pay days: 1 and 15. Next paycheck is the 1st → previous is the 15th of prior month.
            var next = new DateTime(2026, 4, 1);
            var prev = _engine.CalculatePreviousPaycheckDate(1, 15, next);

            Assert.Equal(new DateTime(2026, 3, 15), prev);
        }

        [Fact]
        public void CalculatePreviousPaycheckDate_ClampsToShortMonth()
        {
            // Pay days: 1 and 31. Next paycheck is Feb 1 → previous is Jan 31.
            var next = new DateTime(2026, 2, 1);
            var prev = _engine.CalculatePreviousPaycheckDate(1, 31, next);

            Assert.Equal(new DateTime(2026, 1, 31), prev);
        }

        [Fact]
        public void CalculatePreviousPaycheckDate_Day31InShortMonth_ClampsToDayInMonth()
        {
            // Pay days: 15 and 31. Next is April 15 → previous is March 31.
            var next = new DateTime(2026, 4, 15);
            var prev = _engine.CalculatePreviousPaycheckDate(15, 31, next);

            // days[0]=15, days[1]=31, nextDay=15 == days[0] → prev is days[1] in prior month (March)
            // DaysInMonth(March) = 31, so 31 is valid.
            Assert.Equal(new DateTime(2026, 3, 31), prev);
        }
    }

    // ─── Budget Calculation Engine (no proration) ─────────────────────────────
    //
    // Core formula: remainingToSpend = paycheckAmount - fixedBills - savings - debt
    //
    // There is NO time-based proration. The full paycheck minus obligations
    // is the answer to "how much can I spend before I get paid again?"

    public class BudgetCalculationEngineTests
    {
        private readonly DynamicBudgetEngine _engine = new();

        // Shared scenario: $2500 paycheck, fixed bills = $500, savings = $200, debt = $150.
        // remainingToSpend = 2500 - 500 - 200 - 150 = 1650
        private BudgetCalculationRequest MakeStandardRequest(
            decimal paycheckAmount = 2500m,
            decimal totalFixedBills = 500m,
            decimal savingsContribution = 200m,
            decimal debtPerPaycheck = 150m)
        {
            var today = new DateTime(2026, 4, 7);
            var nextPaycheck = new DateTime(2026, 4, 15);

            return new BudgetCalculationRequest
            {
                PaycheckAmount      = paycheckAmount,
                Today               = today,
                NextPaycheckDate    = nextPaycheck,
                TotalFixedBills     = totalFixedBills,
                SavingsContribution = savingsContribution,
                DebtPerPaycheck     = debtPerPaycheck
            };
        }

        // ── Core formula tests ────────────────────────────────────────────────

        [Fact]
        public void RemainingToSpend_IsPaycheckMinusAllObligations()
        {
            var result = _engine.CalculateDynamicBudget(MakeStandardRequest());

            // 2500 - 500 - 200 - 150 = 1650
            Assert.Equal(1650m, result.RemainingToSpend);
        }

        [Fact]
        public void RemainingToSpend_EqualsDynamicSpendableAmount_LegacyAlias()
        {
            var result = _engine.CalculateDynamicBudget(MakeStandardRequest());

            Assert.Equal(result.RemainingToSpend, result.DynamicSpendableAmount);
        }

        [Fact]
        public void RemainingToSpend_WhenNoDebt_IsPaycheckMinusFixedAndSavings()
        {
            var req = MakeStandardRequest(debtPerPaycheck: 0m);
            var result = _engine.CalculateDynamicBudget(req);

            // 2500 - 500 - 200 = 1800
            Assert.Equal(1800m, result.RemainingToSpend);
        }

        [Fact]
        public void RemainingToSpend_WhenNoObligations_IsFullPaycheck()
        {
            var req = MakeStandardRequest(totalFixedBills: 0m, savingsContribution: 0m, debtPerPaycheck: 0m);
            var result = _engine.CalculateDynamicBudget(req);

            Assert.Equal(2500m, result.RemainingToSpend);
        }

        [Fact]
        public void RemainingToSpend_CanBeNegative_WhenObligationsExceedPaycheck()
        {
            // 500 - 500 - 200 - 150 = -350
            var req = MakeStandardRequest(paycheckAmount: 500m);
            var result = _engine.CalculateDynamicBudget(req);

            Assert.Equal(-350m, result.RemainingToSpend);
        }

        // ── No proration: result is the SAME regardless of day in pay cycle ──

        [Fact]
        public void RemainingToSpend_IsSameAtStartAndEndOfPayCycle()
        {
            // Day of month should NOT matter — no proration applied
            var reqEarlyInCycle = new BudgetCalculationRequest
            {
                PaycheckAmount      = 2500m,
                Today               = new DateTime(2026, 4, 1),  // start of cycle
                NextPaycheckDate    = new DateTime(2026, 4, 15),
                TotalFixedBills     = 500m,
                SavingsContribution = 200m,
                DebtPerPaycheck     = 150m
            };

            var reqLateInCycle = new BudgetCalculationRequest
            {
                PaycheckAmount      = 2500m,
                Today               = new DateTime(2026, 4, 14), // one day before payday
                NextPaycheckDate    = new DateTime(2026, 4, 15),
                TotalFixedBills     = 500m,
                SavingsContribution = 200m,
                DebtPerPaycheck     = 150m
            };

            var earlyResult = _engine.CalculateDynamicBudget(reqEarlyInCycle);
            var lateResult  = _engine.CalculateDynamicBudget(reqLateInCycle);

            // Both should give the same answer — 2500 - 850 = 1650
            Assert.Equal(1650m, earlyResult.RemainingToSpend);
            Assert.Equal(1650m, lateResult.RemainingToSpend);
        }

        // ── Task example scenario: April 7, next paycheck April 15 ───────────

        [Fact]
        public void RemainingToSpend_TaskExample_April7_April15()
        {
            // Today: April 7. Next paycheck: April 15 ($2500)
            // Fixed costs: $1250, Savings: $200, Debt: $300
            // Expected: 2500 - 1250 - 200 - 300 = 750
            var req = new BudgetCalculationRequest
            {
                PaycheckAmount      = 2500m,
                Today               = new DateTime(2026, 4, 7),
                NextPaycheckDate    = new DateTime(2026, 4, 15),
                TotalFixedBills     = 1250m,
                SavingsContribution = 200m,
                DebtPerPaycheck     = 300m
            };

            var result = _engine.CalculateDynamicBudget(req);

            Assert.Equal(750m, result.RemainingToSpend);
        }

        // ── Individual field pass-through ─────────────────────────────────────

        [Fact]
        public void Result_PassesThroughInputFields()
        {
            var req = MakeStandardRequest();
            var result = _engine.CalculateDynamicBudget(req);

            Assert.Equal(2500m, result.PaycheckAmount);
            Assert.Equal(500m,  result.FixedCostsRemaining);
            Assert.Equal(150m,  result.DebtPerPaycheck);
            Assert.Equal(200m,  result.SavingsContribution);
        }

        [Fact]
        public void SavingsAlwaysIncluded_EvenWithNoBillsOrDebt()
        {
            var req = MakeStandardRequest(totalFixedBills: 0m, debtPerPaycheck: 0m, savingsContribution: 300m);
            var result = _engine.CalculateDynamicBudget(req);

            // 2500 - 0 - 300 - 0 = 2200
            Assert.Equal(300m,  result.SavingsContribution);
            Assert.Equal(2200m, result.RemainingToSpend);
        }

        // ── Explanation text ──────────────────────────────────────────────────

        [Fact]
        public void Explanation_ContainsRemainingToSpend()
        {
            var result = _engine.CalculateDynamicBudget(MakeStandardRequest());

            Assert.Contains(result.RemainingToSpend.ToString("0.00"), result.Explanation);
        }

        [Fact]
        public void Explanation_ContainsPaycheckAmount()
        {
            var result = _engine.CalculateDynamicBudget(MakeStandardRequest());

            Assert.Contains("2500.00", result.Explanation);
        }

        [Fact]
        public void Explanation_ContainsDebtLine_WhenDebtIsNonZero()
        {
            var result = _engine.CalculateDynamicBudget(MakeStandardRequest(debtPerPaycheck: 150m));

            Assert.Contains("Debt payoff", result.Explanation);
            Assert.Contains("150.00", result.Explanation);
        }

        [Fact]
        public void Explanation_OmitsDebtLine_WhenDebtIsZero()
        {
            var result = _engine.CalculateDynamicBudget(MakeStandardRequest(debtPerPaycheck: 0m));

            Assert.DoesNotContain("Debt payoff", result.Explanation);
        }

        [Fact]
        public void Explanation_ContainsSavingsLine_WhenSavingsIsNonZero()
        {
            var result = _engine.CalculateDynamicBudget(MakeStandardRequest(savingsContribution: 200m));

            Assert.Contains("Savings", result.Explanation);
            Assert.Contains("200.00", result.Explanation);
        }

        [Fact]
        public void Explanation_ContainsFixedCostsLine_WhenBillsIsNonZero()
        {
            var result = _engine.CalculateDynamicBudget(MakeStandardRequest(totalFixedBills: 500m));

            Assert.Contains("Fixed costs", result.Explanation);
            Assert.Contains("500.00", result.Explanation);
        }

        [Fact]
        public void Explanation_DoesNotContainProration()
        {
            var result = _engine.CalculateDynamicBudget(MakeStandardRequest());

            // Confirm no time-scaling language appears anywhere
            Assert.DoesNotContain("pay cycle", result.Explanation);
            Assert.DoesNotContain("prorate", result.Explanation);
            Assert.DoesNotContain("time remaining", result.Explanation);
        }

        // ── Rounding & precision ──────────────────────────────────────────────

        [Fact]
        public void AllMonetaryValues_AreRoundedToTwoDecimalPlaces()
        {
            var req = new BudgetCalculationRequest
            {
                PaycheckAmount      = 2333.33m,
                Today               = new DateTime(2026, 4, 7),
                NextPaycheckDate    = new DateTime(2026, 4, 15),
                TotalFixedBills     = 333.33m,
                SavingsContribution = 111.11m,
                DebtPerPaycheck     = 77.77m
            };

            var result = _engine.CalculateDynamicBudget(req);

            static void AssertMaxTwoDecimals(decimal value, string fieldName)
            {
                decimal rounded = Math.Round(value, 2);
                Assert.True(value == rounded,
                    $"{fieldName} has more than 2 decimal places: {value}");
            }

            AssertMaxTwoDecimals(result.PaycheckAmount,      nameof(result.PaycheckAmount));
            AssertMaxTwoDecimals(result.FixedCostsRemaining, nameof(result.FixedCostsRemaining));
            AssertMaxTwoDecimals(result.RemainingToSpend,    nameof(result.RemainingToSpend));
            AssertMaxTwoDecimals(result.DebtPerPaycheck,     nameof(result.DebtPerPaycheck));
            AssertMaxTwoDecimals(result.SavingsContribution, nameof(result.SavingsContribution));
        }

        [Fact]
        public void RemainingToSpend_IsReproducible_GivenSameInputs()
        {
            var req = MakeStandardRequest();

            var result1 = _engine.CalculateDynamicBudget(req);
            var result2 = _engine.CalculateDynamicBudget(req);

            Assert.Equal(result1.RemainingToSpend, result2.RemainingToSpend);
        }
    }
}