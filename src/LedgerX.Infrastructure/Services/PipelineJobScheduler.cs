using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using LedgerX.Core.Interfaces;
using LedgerX.Core.Models;
using LedgerX.Infrastructure.Data;

namespace LedgerX.Infrastructure.Services
{
    public class PipelineJobScheduler
    {
        private readonly LedgerXDbContext _dbContext;
        private readonly IMessageBroker _messageBroker;
        private readonly IJobTracker _jobTracker;
        private readonly ILogger<PipelineJobScheduler> _logger;

        public PipelineJobScheduler(
            LedgerXDbContext dbContext,
            IMessageBroker messageBroker,
            IJobTracker jobTracker,
            ILogger<PipelineJobScheduler> logger)
        {
            _dbContext = dbContext;
            _messageBroker = messageBroker;
            _jobTracker = jobTracker;
            _logger = logger;
        }

        public async Task TriggerPipelineJobsAsync(bool injectFault = false)
        {
            _logger.LogInformation("PipelineJobScheduler triggering pipeline. InjectFault={Fault}", injectFault);

            // 1. Gather all active unique Stock, ETF, and Mutual Fund symbols
            var symbols = await _dbContext.Holdings
                .Where(h => h.AssetType == "Stock" || h.AssetType == "ETF" || h.AssetType == "MutualFund")
                .Select(h => new { h.SymbolOrName, h.AssetType })
                .Distinct()
                .ToListAsync();

            _logger.LogInformation("Scheduler found {Count} active instruments to update.", symbols.Count);

            // 2. Generate Market Data Jobs
            foreach (var sym in symbols)
            {
                var payload = new MarketDataJobPayload
                {
                    Symbol = sym.SymbolOrName,
                    AssetType = sym.AssetType,
                    RetryCount = 0,
                    InjectFault = injectFault // Inject faults if toggled
                };

                var payloadJson = JsonSerializer.Serialize(payload);
                var jobId = await _jobTracker.CreateJobAsync("MarketData", payloadJson);
                payload.JobId = jobId;

                await _messageBroker.PublishJobAsync("job.marketdata", payload);
            }

            // 3. Gather all active Users
            var users = await _dbContext.Users.Select(u => u.Id).ToListAsync();

            // 4. Generate Analytics and Alert Jobs for each User
            foreach (var userId in users)
            {
                // Generate Analytics Job
                var analPayload = new AnalyticsJobPayload
                {
                    UserId = userId,
                    RetryCount = 0
                };

                var analJson = JsonSerializer.Serialize(analPayload);
                var analJobId = await _jobTracker.CreateJobAsync("Analytics", analJson);
                analPayload.JobId = analJobId;

                await _messageBroker.PublishJobAsync("job.analytics", analPayload);

                // Generate Alert Job
                var alertPayload = new AlertJobPayload
                {
                    UserId = userId,
                    RetryCount = 0
                };

                var alertJson = JsonSerializer.Serialize(alertPayload);
                var alertJobId = await _jobTracker.CreateJobAsync("Alert", alertJson);
                alertPayload.JobId = alertJobId;

                await _messageBroker.PublishJobAsync("job.alert", alertPayload);
            }

            _logger.LogInformation("PipelineJobScheduler successfully generated and published all pipeline jobs.");
        }
    }
}
