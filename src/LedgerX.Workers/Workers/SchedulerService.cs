using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using LedgerX.Core.Interfaces;
using LedgerX.Infrastructure.Services;

namespace LedgerX.Workers.Workers
{
    public class SchedulerService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<SchedulerService> _logger;
        private readonly TimeSpan _period = TimeSpan.FromMinutes(5); // Run scheduler every 5 minutes

        public SchedulerService(IServiceProvider serviceProvider, ILogger<SchedulerService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("SchedulerService hosted service starting...");

            // Delay startup execution slightly to let migrations/seeding run first
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Scheduler running: Generating nightly pipeline jobs...");
                    await TriggerPipelineJobsAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred in SchedulerService during job generation.");
                }

                await Task.Delay(_period, stoppingToken);
            }
        }

        public async Task TriggerPipelineJobsAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var scheduler = scope.ServiceProvider.GetRequiredService<PipelineJobScheduler>();
            var cacheService = scope.ServiceProvider.GetRequiredService<ICacheService>();

            // Check config in Redis
            bool injectFault = await cacheService.GetAsync<bool>("config:marketdata:inject-fault");

            await scheduler.TriggerPipelineJobsAsync(injectFault);
        }
    }
}
