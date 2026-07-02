using System;

namespace LedgerX.Core.Entities
{
    public class AnalyticsSnapshot
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public DateTime SnapshotDate { get; set; } = DateTime.UtcNow;

        // Portfolio aggregates
        public decimal NetWorth { get; set; }
        public decimal InvestedAmount { get; set; }
        public decimal CurrentValue { get; set; }
        public decimal TotalGainLoss { get; set; }

        // JSON stored metrics
        public string PortfolioGrowthJson { get; set; } = "[]"; // Historical growth data points
        public string AssetAllocationJson { get; set; } = "[]"; // Pie chart data
        public string SectorAllocationJson { get; set; } = "[]"; // Sector breakdown

        // Advanced Metrics
        public decimal Cagr { get; set; }
        public decimal AbsoluteReturn { get; set; }
        public decimal Volatility { get; set; }
        public decimal ConcentrationRisk { get; set; }
        public decimal LargestHoldingPercentage { get; set; }
        public decimal DiversificationScore { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigational properties
        public User? User { get; set; }
    }
}
