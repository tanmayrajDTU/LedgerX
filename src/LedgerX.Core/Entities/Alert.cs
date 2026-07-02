using System;

namespace LedgerX.Core.Entities
{
    public class Alert
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public Guid? HoldingId { get; set; }
        
        // Price, AllocationDrift, FDMaturity, PortfolioConcentration
        public string AlertType { get; set; } = string.Empty;
        
        public decimal ThresholdValue { get; set; }
        public decimal CurrentValue { get; set; }
        public string Message { get; set; } = string.Empty;
        public bool IsTriggered { get; set; } = false;
        public DateTime? TriggeredAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigational properties
        public User? User { get; set; }
        public Holding? Holding { get; set; }
    }
}
