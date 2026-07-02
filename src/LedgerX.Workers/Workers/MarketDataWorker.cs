using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Microsoft.EntityFrameworkCore;
using LedgerX.Core.Entities;
using LedgerX.Core.Interfaces;
using LedgerX.Infrastructure.Data;
using LedgerX.Core.Models;

namespace LedgerX.Workers.Workers
{
    public class MarketDataWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<MarketDataWorker> _logger;
        private readonly IConfiguration _configuration;
        private IConnection? _connection;
        private IModel? _channel;
        private const string QueueName = "ledgerx.marketdata.queue";
        private const string ExchangeName = "ledgerx.jobs.exchange";

        public MarketDataWorker(
            IServiceProvider serviceProvider,
            ILogger<MarketDataWorker> logger,
            IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("MarketDataWorker background service starting...");

            // Robust startup connection retry
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var factory = new ConnectionFactory
                    {
                        Uri = new Uri(_configuration.GetConnectionString("RabbitMQ") ?? "amqp://guest:guest@localhost:5672/")
                    };
                    _connection = factory.CreateConnection();
                    _channel = _connection.CreateModel();
                    _logger.LogInformation("MarketDataWorker successfully connected to RabbitMQ.");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("MarketDataWorker failed to connect to RabbitMQ. Retrying in 5 seconds... Error: {Msg}", ex.Message);
                    await Task.Delay(5000, stoppingToken);
                }
            }

            if (_channel == null) return;

            // Ensure queue and DLQ declarations match RabbitMQMessageBroker topology
            _channel.ExchangeDeclare(ExchangeName, ExchangeType.Direct, durable: true);
            _channel.ExchangeDeclare("ledgerx.deadletter.exchange", ExchangeType.Direct, durable: true);
            
            _channel.QueueDeclare("ledgerx.marketdata.dlq", durable: true, exclusive: false, autoDelete: false, arguments: null);
            _channel.QueueBind("ledgerx.marketdata.dlq", "ledgerx.deadletter.exchange", "deadletter.marketdata", null);

            var args = new System.Collections.Generic.Dictionary<string, object>
            {
                { "x-dead-letter-exchange", "ledgerx.deadletter.exchange" },
                { "x-dead-letter-routing-key", "deadletter.marketdata" }
            };
            _channel.QueueDeclare(QueueName, durable: true, exclusive: false, autoDelete: false, arguments: args);
            _channel.QueueBind(QueueName, ExchangeName, "job.marketdata", null);

            var consumer = new EventingBasicConsumer(_channel);
            consumer.Received += async (model, ea) =>
            {
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);
                MarketDataJobPayload? payload = null;

                try
                {
                    payload = JsonSerializer.Deserialize<MarketDataJobPayload>(message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to deserialize market data job payload: {Msg}", message);
                    _channel.BasicAck(ea.DeliveryTag, false);
                    return;
                }

                if (payload == null)
                {
                    _channel.BasicAck(ea.DeliveryTag, false);
                    return;
                }

                using var scope = _serviceProvider.CreateScope();
                var jobTracker = scope.ServiceProvider.GetRequiredService<IJobTracker>();
                var marketProvider = scope.ServiceProvider.GetRequiredService<IMarketDataProvider>();
                var cacheService = scope.ServiceProvider.GetRequiredService<ICacheService>();
                var dbContext = scope.ServiceProvider.GetRequiredService<LedgerXDbContext>();

                var executionId = await jobTracker.StartExecutionAsync(payload.JobId, nameof(MarketDataWorker), payload.RetryCount);

                try
                {
                    _logger.LogInformation("Processing MarketDataJob. ID: {JobId}, Symbol: {Symbol}, Attempt: {Attempt}", 
                        payload.JobId, payload.Symbol, payload.RetryCount + 1);

                    // 1. Fetch Latest Price (check Redis cache configuration overrides first)
                    bool? liveDataOverride = await cacheService.GetAsync<bool>("config:marketdata:use-live");
                    bool useLiveData = liveDataOverride ?? _configuration.GetValue<bool>("MarketData:UseLiveData", false);

                    bool? faultOverride = await cacheService.GetAsync<bool>("config:marketdata:inject-fault");
                    bool injectFault = payload.InjectFault || (faultOverride ?? false);
                    
                    // Call pluggable provider
                    decimal price = await marketProvider.GetLatestPriceAsync(payload.Symbol, payload.AssetType, useLiveData, injectFault);

                    // 2. Save to Price History (avoid duplicating symbol+date via simple check)
                    var today = DateTime.UtcNow.Date;
                    var alreadyExists = await dbContext.PriceHistory.AnyAsync(p => p.SymbolOrName == payload.Symbol && p.PriceDate.Date == today);
                    if (!alreadyExists)
                    {
                        dbContext.PriceHistory.Add(new PriceHistory
                        {
                            SymbolOrName = payload.Symbol,
                            AssetType = payload.AssetType,
                            Price = price,
                            PriceDate = DateTime.UtcNow
                        });
                        await dbContext.SaveChangesAsync();
                    }

                    // 3. Cache latest price in Redis
                    await cacheService.SetAsync($"prices:latest:{payload.Symbol}", price, TimeSpan.FromHours(24));

                    // 4. Complete job execution in DB
                    await jobTracker.CompleteExecutionAsync(executionId, payload.JobId);

                    _logger.LogInformation("Completed MarketDataJob. ID: {JobId}, Symbol: {Symbol}, Price: {Price}", 
                        payload.JobId, payload.Symbol, price);

                    _channel.BasicAck(ea.DeliveryTag, false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process MarketDataJob. ID: {JobId}, Symbol: {Symbol}", payload.JobId, payload.Symbol);

                    if (payload.RetryCount < 3)
                    {
                        // Record failure in DB for this execution run (not final deadletter yet)
                        await jobTracker.FailExecutionAsync(executionId, payload.JobId, ex.Message, moveToDeadLetter: false);

                        // Acknowledge the message (to remove from queue)
                        _channel.BasicAck(ea.DeliveryTag, false);

                        // Perform exponential backoff (e.g. 2, 4, 8 seconds) and republish
                        var backoffDelay = TimeSpan.FromSeconds(Math.Pow(2, payload.RetryCount + 1));
                        _logger.LogWarning("Re-queueing MarketDataJob with backoff. Delaying {Delay} seconds...", backoffDelay.TotalSeconds);
                        
                        // Fire-and-forget republish after delay (non-blocking)
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(backoffDelay);
                            payload.RetryCount++;
                            var json = JsonSerializer.Serialize(payload);
                            var bodyBytes = Encoding.UTF8.GetBytes(json);
                            
                            var properties = _channel.CreateBasicProperties();
                            properties.Persistent = true;

                            lock (_channel)
                            {
                                _channel.BasicPublish(
                                    exchange: ExchangeName,
                                    routingKey: "job.marketdata",
                                    basicProperties: properties,
                                    body: bodyBytes
                                );
                            }
                        });
                    }
                    else
                    {
                        // Exhausted retries. Write final fail state to DB as DeadLetter
                        await jobTracker.FailExecutionAsync(executionId, payload.JobId, $"Retries exhausted. Error: {ex.Message}", moveToDeadLetter: true);

                        _logger.LogError("MarketDataJob {JobId} exhausted all retries. Rejecting message to trigger Dead Letter Queue.", payload.JobId);
                        
                        // Reject message. basic.reject with requeue=false automatically sends to RabbitMQ DLQ
                        _channel.BasicReject(ea.DeliveryTag, false);
                    }
                }
            };

            _channel.BasicConsume(queue: QueueName, autoAck: false, consumer: consumer);

            // Keep worker running
            var tcs = new TaskCompletionSource<bool>();
            stoppingToken.Register(() => tcs.SetResult(true));
            await tcs.Task;
        }

        public override void Dispose()
        {
            _channel?.Dispose();
            _connection?.Dispose();
            base.Dispose();
        }
    }
}
