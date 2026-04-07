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
            // Pay days: 15 and 31. Next is April 15 → previous is April 31 clamped to 30.
            var next = new DateTime(2026, 4, 15);
            var prev = _engine.CalculatePreviousPaycheckDate(15, 31, next);

            // days[0]=15, days[1]=31, nextDay=15 == days[0] → prev is days[1] in prior month (March)
            // DaysInMonth(March) = 31, so 31 is valid.
            Assert.Equal(new DateTime(2026, 3, 31), prev);
        }
    }

    // ─── New: Budget Calculation Engine (6-step) ───────────────────────────────

    public class BudgetCalculationEngineTests
    {
        private readonly DynamicBudgetEngine _engine = new();

        // Shared scenario: $2500 paycheck, 16-day pay cycle, 14 days remaining.
        // Fixed bills = $500, savings = $200, debt = $150.
        // totalRecurring = 850, effectivePaycheck = 1650
        // prorateFactor = 14/16 = 0.875, spendable = 1650 * 0.875 = 1443.75
        private BudgetCalculationRequest MakeStandardRequest(
            decimal paycheckAmount = 2500m,
            decimal totalFixedBills = 500m,
            decimal savingsContribution = 200m,
            decimal debtPerPaycheck = 150m,
            int payCycleDays = 16,
            int daysUntilNext = 14)
        {
            var today = new DateTime(2026, 4, 1);
            var nextPaycheck = today.AddDays(daysUntilNext);
            var prevPaycheck = nextPaycheck.AddDays(-payCycleDays);

            return new BudgetCalculationRequest
            {
                PaycheckAmount       = paycheckAmount,
                Today                = today,
                PreviousPaycheckDate = prevPaycheck,
                NextPaycheckDate     = nextPaycheck,
                TotalFixedBills      = totalFixedBills,
                SavingsContribution  = savingsContribution,
                DebtPerPaycheck      = debtPerPaycheck
            };
        }

        // ── Step 3: totalRecurringCosts ───────────────────────────────────────

        [Fact]
        public void TotalRecurringCosts_IsSumOfBillsSavingsDebt()
        {
            var result = _engine.CalculateDynamicBudget(MakeStandardRequest());

            // 500 + 200 + 150 = 850
            Assert.Equal(850m, result.TotalRecurringCosts);
        }

        [Fact]
        public void TotalRecurringCosts_WhenNoDebt_IsBillsPlusSavings()
        {
            var req = MakeStandardRequest(debtPerPaycheck: 0m);
            var result = _engine.CalculateDynamicBudget(req);

            // 500 + 200 + 0 = 700
            Assert.Equal(700m, result.TotalRecurringCosts);
        }

        [Fact]
        public void SavingsAlwaysIncluded_EvenWithNoBillsOrDebt()
        {
            var req = MakeStandardRequest(totalFixedBills: 0m, debtPerPaycheck: 0m, savingsContribution: 300m);
            var result = _engine.CalculateDynamicBudget(req);

            Assert.Equal(300m, result.TotalRecurringCosts);
            Assert.Equal(300m, result.SavingsContribution);
        }

        // ── Step 4: effectivePaycheck ─────────────────────────────────────────

        [Fact]
        public void EffectivePaycheck_IsPaycheckMinusTotalRecurring()
        {
            var result = _engine.CalculateDynamicBudget(MakeStandardRequest());

            // 2500 - 850 = 1650
            Assert.Equal(1650m, result.EffectivePaycheck);
        }

        [Fact]
        public void EffectivePaycheck_CanBeNegative_WhenCostsExceedPaycheck()
        {
            var req = MakeStandardRequest(paycheckAmount: 500m, totalFixedBills: 600m);
            var result = _engine.CalculateDynamicBudget(req);

            // 500 - (600+200+150) = 500 - 950 = -450
            Assert.Equal(-450m, result.EffectivePaycheck);
        }

        // ── Step 5: prorateFactor & dynamicSpendableAmount ───────────────────

        [Fact]
        public void ProrateFactor_IsRatioOfDaysRemainingToCycleDays()
        {
            // 14 days remaining out of 16-day cycle = 0.875
            var result = _engine.CalculateDynamicBudget(MakeStandardRequest(payCycleDays: 16, daysUntilNext: 14));

            Assert.Equal(0.875m, result.ProrateFactor);
        }

        [Fact]
        public void DynamicSpendableAmount_IsEffectivePaycheckTimesProrateFactor()
        {
            // effectivePaycheck = 1650, prorateFactor = 0.875 → 1443.75
            var result = _engine.CalculateDynamicBudget(MakeStandardRequest(payCycleDays: 16, daysUntilNext: 14));

            Assert.Equal(1443.75m, result.DynamicSpendableAmount);
        }

        [Fact]
        public void DynamicSpendableAmount_AtStartOfCycle_IsNearFullEffectivePaycheck()
        {
            // 15 days remaining out of 15-day cycle = prorateFactor 1.0
            var req = MakeStandardRequest(payCycleDays: 15, daysUntilNext: 15);
            var result = _engine.CalculateDynamicBudget(req);

            Assert.Equal(1m, result.ProrateFactor);
            Assert.Equal(1650m, result.DynamicSpendableAmount);
        }

        [Fact]
        public void DynamicSpendableAmount_NearEndOfCycle_IsSmall()
        {
            // 1 day remaining out of 15-day cycle
            var req = MakeStandardRequest(payCycleDays: 15, daysUntilNext: 1);
            var result = _engine.CalculateDynamicBudget(req);

            // prorateFactor = round(1/15, 4) = 0.0667
            // spendable = 1650 * 0.0667 = 110.055 → rounds to 110.06
            Assert.Equal(0.0667m, result.ProrateFactor);
            Assert.Equal(110.06m, result.DynamicSpendableAmount);
        }

        [Fact]
        public void DynamicSpendableAmount_StandardBiMonthly_MatchesKnownValues()
        {
            // Bi-monthly (1st and 15th) — today is April 7, next paycheck April 15.
            // payCycleDays = 14 (April 1 → April 15)
            // daysUntilNext = 8 (April 7 → April 15)
            // effectivePaycheck = 2500 - 850 = 1650
            // prorateFactor = round(8/14, 4) = 0.5714
            // spendable = 1650 * 0.5714 = 942.81

            var today = new DateTime(2026, 4, 7);
            var nextPaycheck = new DateTime(2026, 4, 15);
            var prevPaycheck = new DateTime(2026, 4, 1);

            var req = new BudgetCalculationRequest
            {
                PaycheckAmount       = 2500m,
                Today                = today,
                PreviousPaycheckDate = prevPaycheck,
                NextPaycheckDate     = nextPaycheck,
                TotalFixedBills      = 500m,
                SavingsContribution  = 200m,
                DebtPerPaycheck      = 150m
            };

            var result = _engine.CalculateDynamicBudget(req);

            Assert.Equal(850m, result.TotalRecurringCosts);
            Assert.Equal(1650m, result.EffectivePaycheck);
            Assert.Equal(0.5714m, result.ProrateFactor);
            Assert.Equal(942.81m, result.DynamicSpendableAmount);
        }

        // ── Step 6: structured result fields ─────────────────────────────────

        [Fact]
        public void Result_PassesThroughInputFields()
        {
            var req = MakeStandardRequest();
            var result = _engine.CalculateDynamicBudget(req);

            Assert.Equal(2500m, result.PaycheckAmount);
            Assert.Equal(150m, result.DebtPerPaycheck);
            Assert.Equal(200m, result.SavingsContribution);
        }

        [Fact]
        public void Explanation_ContainsDynamicSpendableAmount()
        {
            var result = _engine.CalculateDynamicBudget(MakeStandardRequest());

            Assert.Contains(result.DynamicSpendableAmount.ToString("0.00"), result.Explanation);
        }

        [Fact]
        public void Explanation_ContainsPaycheckAmount()
        {
            var result = _engine.CalculateDynamicBudget(MakeStandardRequest());

            Assert.Contains("$2500.00 paycheck", result.Explanation);
        }

        [Fact]
        public void Explanation_ContainsDebtLine_WhenDebtIsNonZero()
        {
            var result = _engine.CalculateDynamicBudget(MakeStandardRequest(debtPerPaycheck: 150m));

            Assert.Contains("toward debt", result.Explanation);
            Assert.Contains("$150.00", result.Explanation);
        }

        [Fact]
        public void Explanation_OmitsDebtLine_WhenDebtIsZero()
        {
            var result = _engine.CalculateDynamicBudget(MakeStandardRequest(debtPerPaycheck: 0m));

            Assert.DoesNotContain("toward debt", result.Explanation);
        }

        [Fact]
        public void Explanation_ContainsSavingsLine_WhenSavingsIsNonZero()
        {
            var result = _engine.CalculateDynamicBudget(MakeStandardRequest(savingsContribution: 200m));

            Assert.Contains("in savings", result.Explanation);
            Assert.Contains("$200.00", result.Explanation);
        }

        [Fact]
        public void Explanation_ContainsBillsLine_WhenBillsIsNonZero()
        {
            var result = _engine.CalculateDynamicBudget(MakeStandardRequest(totalFixedBills: 500m));

            Assert.Contains("in upcoming bills", result.Explanation);
            Assert.Contains("$500.00", result.Explanation);
        }

        // ── Rounding & precision ──────────────────────────────────────────────

        [Fact]
        public void AllMonetaryValues_AreRoundedToTwoDecimalPlaces()
        {
            // Use values that would produce many decimal places without rounding
            var today = new DateTime(2026, 4, 7);
            var req = new BudgetCalculationRequest
            {
                PaycheckAmount       = 2333.33m,
                Today                = today,
                PreviousPaycheckDate = new DateTime(2026, 4, 1),
                NextPaycheckDate     = new DateTime(2026, 4, 15),
                TotalFixedBills      = 333.33m,
                SavingsContribution  = 111.11m,
                DebtPerPaycheck      = 77.77m
            };

            var result = _engine.CalculateDynamicBudget(req);

            // Assert no more than 2 decimal places on each monetary field
            static void AssertMaxTwoDecimals(decimal value, string fieldName)
            {
                decimal rounded = Math.Round(value, 2);
                Assert.True(value == rounded,
                    $"{fieldName} has more than 2 decimal places: {value}");
            }

            AssertMaxTwoDecimals(result.PaycheckAmount,          nameof(result.PaycheckAmount));
            AssertMaxTwoDecimals(result.TotalRecurringCosts,     nameof(result.TotalRecurringCosts));
            AssertMaxTwoDecimals(result.EffectivePaycheck,       nameof(result.EffectivePaycheck));
            AssertMaxTwoDecimals(result.DynamicSpendableAmount,  nameof(result.DynamicSpendableAmount));
            AssertMaxTwoDecimals(result.DebtPerPaycheck,         nameof(result.DebtPerPaycheck));
            AssertMaxTwoDecimals(result.SavingsContribution,     nameof(result.SavingsContribution));
        }

        [Fact]
        public void DynamicSpendableAmount_IsReproducible_GivenSameInputs()
        {
            var req = MakeStandardRequest();

            var result1 = _engine.CalculateDynamicBudget(req);
            var result2 = _engine.CalculateDynamicBudget(req);

            Assert.Equal(result1.DynamicSpendableAmount, result2.DynamicSpendableAmount);
            Assert.Equal(result1.ProrateFactor, result2.ProrateFactor);
        }
    }
}