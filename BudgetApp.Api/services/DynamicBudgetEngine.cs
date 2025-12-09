// File: services/DynamicBudgetEngine.cs
using System;
using BudgetApp.Api.Data;

namespace BudgetApp.Api.Services
{
    public interface IDynamicBudgetEngine
    {
        TransactionSuggestedKind ClassifyDeposit(DepositContext ctx);
    }

    public class DynamicBudgetEngine : IDynamicBudgetEngine
    {
        // How many days around a payday we consider "pay window"
        private const int PayWindowDays = 5;

        // How close (percentage) to the expected paycheck amount we require
        private const decimal AmountTolerancePercent = 0.15m; // 15%

        public TransactionSuggestedKind ClassifyDeposit(DepositContext ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            // No expected paycheck configured → we can't confidently call anything a paycheck.
            if (ctx.ExpectedPaycheckAmount <= 0m)
                return TransactionSuggestedKind.Windfall;

            // Amount must be > 0 and reasonably close to expected.
            if (ctx.Amount <= 0m)
                return TransactionSuggestedKind.Windfall;

            var isAmountMatch = IsAmountWithinTolerance(
                ctx.Amount,
                ctx.ExpectedPaycheckAmount,
                AmountTolerancePercent
            );

            // Date must be reasonably close to one of the two configured pay days.
            var txDate = ctx.Date.Date;

            var inPayWindow =
                IsInPayWindow(txDate, ctx.PayDay1) ||
                IsInPayWindow(txDate, ctx.PayDay2);

            if (isAmountMatch && inPayWindow)
            {
                return TransactionSuggestedKind.Paycheck;
            }

            // Later we can add more nuanced cases (e.g., "Reimbursement", "Transfer", etc.)
            return TransactionSuggestedKind.Windfall;
        }

        private static bool IsAmountWithinTolerance(
            decimal actual,
            decimal expected,
            decimal tolerancePercent)
        {
            var diff = Math.Abs(actual - expected);
            var allowed = expected * tolerancePercent;
            return diff <= allowed;
        }

        private static bool IsInPayWindow(DateTime txDate, int payDay)
        {
            if (payDay < 1 || payDay > 31)
                return false;

            // Candidate payday in the same month as the transaction.
            var year = txDate.Year;
            var month = txDate.Month;

            var daysInMonth = DateTime.DaysInMonth(year, month);
            var day = payDay > daysInMonth ? daysInMonth : payDay;

            var candidate = new DateTime(year, month, day);

            var deltaDays = Math.Abs((candidate.Date - txDate.Date).TotalDays);

            return deltaDays <= PayWindowDays;
        }
    }
}
