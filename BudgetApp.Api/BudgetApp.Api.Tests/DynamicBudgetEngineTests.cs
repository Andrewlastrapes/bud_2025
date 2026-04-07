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

    // ─── Pay Cycle Calculation ────────────────────────────────────────────────

    public class PayCycleCalculationTests
    {
        private readonly DynamicBudgetEngine _engine = new();

        [Fact]
        public void CalculatePreviousPaycheckDate_NextIsSecondDay_ReturnsSameMonthFirstDay()
        {
            var next = new DateTime(2026, 4, 15);
            var prev = _engine.CalculatePreviousPaycheckDate(1, 15, next);

            Assert.Equal(new DateTime(2026, 4, 1), prev);
        }

        [Fact]
        public void CalculatePreviousPaycheckDate_NextIsFirstDay_ReturnsPriorMonthSecondDay()
        {
            var next = new DateTime(2026, 4, 1);
            var prev = _engine.CalculatePreviousPaycheckDate(1, 15, next);

            Assert.Equal(new DateTime(2026, 3, 15), prev);
        }

        [Fact]
        public void CalculatePreviousPaycheckDate_ClampsToShortMonth()
        {
            var next = new DateTime(2026, 2, 1);
            var prev = _engine.CalculatePreviousPaycheckDate(1, 31, next);

            Assert.Equal(new DateTime(2026, 1, 31), prev);
        }

        [Fact]
        public void CalculatePreviousPaycheckDate_Day31InShortMonth_ClampsToDayInMonth()
        {
            var next = new DateTime(2026, 4, 15);
            var prev = _engine.CalculatePreviousPaycheckDate(15, 31, next);

            Assert.Equal(new DateTime(2026, 3, 31), prev);
        }
    }

    // ─── Base Budget Calculation (paycheck - fixedCosts only) ─────────────────
    //
    // This is displayed on the Debt screen BEFORE the user makes debt decisions.
    // baseRemaining = paycheck - fixedCosts
    // Debt and savings are NOT subtracted at this stage.

    public class BaseBudgetCalculationTests
    {
        private readonly DynamicBudgetEngine _engine = new();

        private BaseBudgetRequest MakeBaseRequest(
            decimal paycheckAmount = 2500m,
            decimal totalFixedBills = 800m)
        {
            return new BaseBudgetRequest
            {
                PaycheckAmount   = paycheckAmount,
                Today            = new DateTime(2026, 4, 7),
                NextPaycheckDate = new DateTime(2026, 4, 15),
                TotalFixedBills  = totalFixedBills
            };
        }

        [Fact]
        public void BaseRemaining_IsPaycheckMinusFixedCosts()
        {
            var result = _engine.CalculateBaseBudget(MakeBaseRequest());

            // 2500 - 800 = 1700
            Assert.Equal(1700m, result.BaseRemaining);
        }

        [Fact]
        public void BaseRemaining_WithNoFixedCosts_IsFullPaycheck()
        {
            var result = _engine.CalculateBaseBudget(MakeBaseRequest(totalFixedBills: 0m));

            Assert.Equal(2500m, result.BaseRemaining);
        }

        [Fact]
        public void BaseRemaining_CanBeNegative_WhenFixedCostsExceedPaycheck()
        {
            var result = _engine.CalculateBaseBudget(MakeBaseRequest(
                paycheckAmount: 500m, totalFixedBills: 800m));

            Assert.Equal(-300m, result.BaseRemaining);
        }

        [Fact]
        public void BaseRemaining_TaskExample_April7_April15()
        {
            // paycheck $2500, fixed costs $1250 → base = $1250
            var result = _engine.CalculateBaseBudget(MakeBaseRequest(
                paycheckAmount: 2500m, totalFixedBills: 1250m));

            Assert.Equal(1250m, result.BaseRemaining);
        }

        [Fact]
        public void Result_PassesThroughPaycheckAndFixedCosts()
        {
            var result = _engine.CalculateBaseBudget(MakeBaseRequest(
                paycheckAmount: 2500m, totalFixedBills: 800m));

            Assert.Equal(2500m, result.PaycheckAmount);
            Assert.Equal(800m,  result.FixedCostsRemaining);
        }

        [Fact]
        public void BaseRemaining_DoesNotIncludeDebtOrSavings()
        {
            // No matter what debt/savings might be, base is only paycheck - fixed
            var result = _engine.CalculateBaseBudget(MakeBaseRequest(
                paycheckAmount: 2500m, totalFixedBills: 800m));

            // 1700 — debt/savings are not subtracted
            Assert.Equal(1700m, result.BaseRemaining);
        }

        [Fact]
        public void AllMonetaryValues_AreRoundedToTwoDecimalPlaces()
        {
            var req = new BaseBudgetRequest
            {
                PaycheckAmount   = 2333.33m,
                Today            = new DateTime(2026, 4, 7),
                NextPaycheckDate = new DateTime(2026, 4, 15),
                TotalFixedBills  = 333.33m
            };

            var result = _engine.CalculateBaseBudget(req);

            static void AssertMaxTwoDecimals(decimal value, string name)
            {
                Assert.True(value == Math.Round(value, 2),
                    $"{name} has more than 2 decimal places: {value}");
            }

            AssertMaxTwoDecimals(result.PaycheckAmount,      nameof(result.PaycheckAmount));
            AssertMaxTwoDecimals(result.FixedCostsRemaining, nameof(result.FixedCostsRemaining));
            AssertMaxTwoDecimals(result.BaseRemaining,       nameof(result.BaseRemaining));
        }
    }

    // ─── Full Budget Calculation (paycheck - fixedCosts - debt - savings) ──────
    //
    // Core formula: remainingToSpend = paycheck - fixedBills - debt - savings
    // There is NO time-based proration.

    public class BudgetCalculationEngineTests
    {
        private readonly DynamicBudgetEngine _engine = new();

        // Shared scenario: $2500 paycheck, fixed bills = $500, savings = $200, debt = $150.
        // baseRemaining = 2500 - 500 = 2000
        // remainingToSpend = 2000 - 150 - 200 = 1650
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

        // ── Core formula ──────────────────────────────────────────────────────

        [Fact]
        public void RemainingToSpend_IsPaycheckMinusAllObligations()
        {
            var result = _engine.CalculateDynamicBudget(MakeStandardRequest());

            // 2500 - 500 - 200 - 150 = 1650
            Assert.Equal(1650m, result.RemainingToSpend);
        }

        [Fact]
        public void BaseRemaining_IsPaycheckMinusFixedCosts_OnlyNoDebtNoSavings()
        {
            var result = _engine.CalculateDynamicBudget(MakeStandardRequest());

            // 2500 - 500 = 2000 (debt and savings NOT included in baseRemaining)
            Assert.Equal(2000m, result.BaseRemaining);
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
            var req = MakeStandardRequest(paycheckAmount: 500m);
            var result = _engine.CalculateDynamicBudget(req);

            // 500 - 500 - 200 - 150 = -350
            Assert.Equal(-350m, result.RemainingToSpend);
        }

        // ── No proration: same result regardless of day in cycle ──────────────

        [Fact]
        public void RemainingToSpend_IsSameAtStartAndEndOfPayCycle()
        {
            var reqEarly = new BudgetCalculationRequest
            {
                PaycheckAmount      = 2500m,
                Today               = new DateTime(2026, 4, 1),
                NextPaycheckDate    = new DateTime(2026, 4, 15),
                TotalFixedBills     = 500m,
                SavingsContribution = 200m,
                DebtPerPaycheck     = 150m
            };

            var reqLate = new BudgetCalculationRequest
            {
                PaycheckAmount      = 2500m,
                Today               = new DateTime(2026, 4, 14),
                NextPaycheckDate    = new DateTime(2026, 4, 15),
                TotalFixedBills     = 500m,
                SavingsContribution = 200m,
                DebtPerPaycheck     = 150m
            };

            var earlyResult = _engine.CalculateDynamicBudget(reqEarly);
            var lateResult  = _engine.CalculateDynamicBudget(reqLate);

            Assert.Equal(1650m, earlyResult.RemainingToSpend);
            Assert.Equal(1650m, lateResult.RemainingToSpend);
        }

        // ── Task example: April 7, next paycheck April 15 ────────────────────

        [Fact]
        public void RemainingToSpend_TaskExample_April7_April15()
        {
            // paycheck $2500, fixed $1250, savings $200, debt $300 → remaining $750
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

            Assert.Equal(1250m, result.BaseRemaining);   // 2500 - 1250
            Assert.Equal(750m,  result.RemainingToSpend); // 1250 - 300 - 200
        }

        // ── Field pass-through ────────────────────────────────────────────────

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

            Assert.Equal(300m,  result.SavingsContribution);
            Assert.Equal(2200m, result.RemainingToSpend); // 2500 - 300
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
            AssertMaxTwoDecimals(result.BaseRemaining,       nameof(result.BaseRemaining));
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
            Assert.Equal(result1.BaseRemaining,    result2.BaseRemaining);
        }
    }
}