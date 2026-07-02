using System;

namespace LedgerX.Core.Entities
{
    public class Transaction
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid HoldingId { get; set; }
        public Guid UserId { get; set; }
        
        // Buy, Sell, Dividend, InterestCredit, Deposit, Withdrawal
        public string TransactionType { get; set; } = string.Empty;
        
        // Net transaction value
        public decimal Amount { get; set; }
        
        // Quantity or units changed (optional)
        public decimal? QuantityOrUnits { get; set; }
        
        // Price or NAV at transaction time (optional)
        public decimal? PriceOrNAV { get; set; }
        
        public DateTime TransactionDate { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigational properties
        public Holding? Holding { get; set; }
        public User? User { get; set; }
    }
}
