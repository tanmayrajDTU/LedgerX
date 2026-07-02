using System;

namespace LedgerX.Core.Models
{
    public class MarketDataJobPayload
    {
        public Guid JobId { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public string AssetType { get; set; } = string.Empty;
        public int RetryCount { get; set; }
        public bool InjectFault { get; set; }
    }

    public class AnalyticsJobPayload
    {
        public Guid JobId { get; set; }
        public Guid UserId { get; set; }
        public int RetryCount { get; set; }
    }

    public class AlertJobPayload
    {
        public Guid JobId { get; set; }
        public Guid UserId { get; set; }
        public int RetryCount { get; set; }
    }
}
