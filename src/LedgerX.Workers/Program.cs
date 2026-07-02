using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Prometheus;
using Serilog;
using StackExchange.Redis;
using LedgerX.Core.Interfaces;
using LedgerX.Infrastructure.Caching;
using LedgerX.Infrastructure.Data;
using LedgerX.Infrastructure.Messaging;
using LedgerX.Infrastructure.Services;
using LedgerX.Workers.Workers;

namespace LedgerX.Workers
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console()
                .CreateLogger();

            try
            {
                Log.Information("Starting LedgerX Workers host...");

                // Start Prometheus Standalone Metric Server on port 5001
                var metricServer = new MetricServer("localhost", 5001);
                metricServer.Start();
                Log.Information("Prometheus worker metrics server started on port 5001.");

                var builder = Host.CreateApplicationBuilder(args);

                builder.Services.AddLogging(loggingBuilder =>
                {
                    loggingBuilder.AddSerilog(Log.Logger);
                });

                // Configure Database (EF Core + Postgres)
                builder.Services.AddDbContext<LedgerXDbContext>(options =>
                    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

                // Configure Redis
                builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
                {
                    var connectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
                    try
                    {
                        return ConnectionMultiplexer.Connect(connectionString);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failed to connect to Redis at {Conn}", connectionString);
                        return null!;
                    }
                });

                // Register Infrastructure Services
                builder.Services.AddSingleton<ICacheService, RedisCacheService>();
                builder.Services.AddSingleton<IMessageBroker, RabbitMQMessageBroker>();
                builder.Services.AddHttpClient<IMarketDataProvider, MarketDataProvider>();
                builder.Services.AddScoped<IJobTracker, JobTracker>();
                builder.Services.AddScoped<PipelineJobScheduler>();

                // Register Background Workers & Schedulers
                builder.Services.AddHostedService<MarketDataWorker>();
                builder.Services.AddHostedService<AnalyticsWorker>();
                builder.Services.AddHostedService<AlertWorker>();
                builder.Services.AddHostedService<SchedulerService>();
                builder.Services.AddHostedService<SchedulerTriggerWorker>();

                var host = builder.Build();
                host.Run();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Workers host terminated unexpectedly");
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
}
