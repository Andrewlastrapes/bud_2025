using System;
using BudgetApp.Api.Data;
using BudgetApp.Api.Services;
using Xunit;

namespace BudgetApp.Api.Tests
{
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
}
