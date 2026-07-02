using System;

namespace LedgerX.Core.Entities
{
    public class PriceHistory
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string SymbolOrName { get; set; } = string.Empty;
        public string AssetType { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public DateTime PriceDate { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
