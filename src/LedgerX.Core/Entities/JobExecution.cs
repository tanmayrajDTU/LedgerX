using System;

namespace LedgerX.Core.Entities
{
    public class JobExecution
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid JobId { get; set; }
        public string WorkerName { get; set; } = string.Empty;
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
        public long? DurationMs { get; set; }
        public int RetryCount { get; set; }
        public string? ErrorMessage { get; set; }

        // Navigational properties
        public Job? Job { get; set; }
    }
}
