// File: Data/DepositContext.cs
using System;

namespace BudgetApp.Api.Data
{
    public class DepositContext
    {

        public decimal Amount { get; set; }


        public DateTime Date { get; set; }


        public string? MerchantName { get; set; }


        public int PayDay1 { get; set; }


        public int PayDay2 { get; set; }


        public decimal ExpectedPaycheckAmount { get; set; }
    }
}
