using System;
using System.Collections.Generic;

namespace LedgerX.Core.Entities
{
    public class Job
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        
        // MarketData, Analytics, Alert
        public string JobType { get; set; } = string.Empty;
        
        // JSON parameters
        public string Payload { get; set; } = string.Empty;

        // Pending, Processing, Completed, Failed, DeadLetter
        public string Status { get; set; } = "Pending";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigational properties
        public ICollection<JobExecution> JobExecutions { get; set; } = new List<JobExecution>();
    }
}
