using System;
using System.Collections.Generic;

namespace LedgerX.Core.Entities
{
    public class Holding
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        
        // Stock, ETF, MutualFund, FixedDeposit, SavingsAccount
        public string AssetType { get; set; } = string.Empty; 
        
        // Symbol for Stocks/ETFs, Scheme Name for Mutual Funds, Bank Name for FD & Savings
        public string SymbolOrName { get; set; } = string.Empty;

        // Stock/ETF quantity or Mutual Fund units
        public decimal? QuantityOrUnits { get; set; }

        // Stock/ETF buy price or Mutual Fund Buy NAV
        public decimal? BuyPriceOrNAV { get; set; }

        // Fixed Deposit principal amount
        public decimal? Principal { get; set; }

        // Fixed Deposit interest rate (e.g. 7.5 for 7.5%)
        public decimal? InterestRate { get; set; }

        // Fixed Deposit start date
        public DateTime? StartDate { get; set; }

        // Fixed Deposit maturity date
        public DateTime? MaturityDate { get; set; }

        // Savings Account current balance
        public decimal? Balance { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigational properties
        public User? User { get; set; }
        public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
        public ICollection<Alert> Alerts { get; set; } = new List<Alert>();
    }
}
