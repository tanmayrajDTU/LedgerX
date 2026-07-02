using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using LedgerX.Core.Entities;
using LedgerX.Core.Interfaces;
using LedgerX.Infrastructure.Data;
using LedgerX.Core.Models;

namespace LedgerX.Workers.Workers
{
    public class AlertWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<AlertWorker> _logger;
        private readonly IConfiguration _configuration;
        private IConnection? _connection;
        private IModel? _channel;
        private const string QueueName = "ledgerx.alert.queue";
        private const string ExchangeName = "ledgerx.jobs.exchange";

        public AlertWorker(
            IServiceProvider serviceProvider,
            ILogger<AlertWorker> logger,
            IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("AlertWorker background service starting...");

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
                    _logger.LogInformation("AlertWorker successfully connected to RabbitMQ.");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("AlertWorker failed to connect to RabbitMQ. Retrying in 5 seconds...");
                    await Task.Delay(5000, stoppingToken);
                }
            }

            if (_channel == null) return;

            // Ensure queue and DLQ declarations match RabbitMQMessageBroker topology
            _channel.ExchangeDeclare(ExchangeName, ExchangeType.Direct, durable: true);
            _channel.ExchangeDeclare("ledgerx.deadletter.exchange", ExchangeType.Direct, durable: true);
            
            _channel.QueueDeclare("ledgerx.alert.dlq", durable: true, exclusive: false, autoDelete: false, arguments: null);
            _channel.QueueBind("ledgerx.alert.dlq", "ledgerx.deadletter.exchange", "deadletter.alert", null);

            var args = new System.Collections.Generic.Dictionary<string, object>
            {
                { "x-dead-letter-exchange", "ledgerx.deadletter.exchange" },
                { "x-dead-letter-routing-key", "deadletter.alert" }
            };
            _channel.QueueDeclare(QueueName, durable: true, exclusive: false, autoDelete: false, arguments: args);
            _channel.QueueBind(QueueName, ExchangeName, "job.alert", null);

            var consumer = new EventingBasicConsumer(_channel);
            consumer.Received += async (model, ea) =>
            {
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);
                AlertJobPayload? payload = null;

                try
                {
                    payload = JsonSerializer.Deserialize<AlertJobPayload>(message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to deserialize alert job payload: {Msg}", message);
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
                var dbContext = scope.ServiceProvider.GetRequiredService<LedgerXDbContext>();

                var executionId = await jobTracker.StartExecutionAsync(payload.JobId, nameof(AlertWorker), payload.RetryCount);

                try
                {
                    _logger.LogInformation("Processing AlertJob. ID: {JobId}, User: {UserId}", payload.JobId, payload.UserId);

                    // 1. Fetch User and latest analytics snapshot
                    var user = await dbContext.Users
                        .Include(u => u.Holdings)
                        .FirstOrDefaultAsync(u => u.Id == payload.UserId);

                    if (user == null)
                    {
                        throw new Exception($"User with ID {payload.UserId} not found.");
                    }

                    var latestSnapshot = await dbContext.AnalyticsSnapshots
                        .Where(s => s.UserId == user.Id)
                        .OrderByDescending(s => s.SnapshotDate)
                        .FirstOrDefaultAsync();

                    // 2. Fetch User configured Alerts (both triggered and untriggered)
                    var alertsList = await dbContext.Alerts
                        .Where(a => a.UserId == user.Id)
                        .ToListAsync();

                    foreach (var alert in alertsList)
                    {
                        bool shouldTrigger = false;
                        decimal currentCalculatedValue = 0;
                        string alertMessage = alert.Message;

                        switch (alert.AlertType)
                        {
                            case "Price":
                                if (alert.HoldingId.HasValue)
                                {
                                    var holding = user.Holdings.FirstOrDefault(h => h.Id == alert.HoldingId.Value);
                                    if (holding != null && !string.IsNullOrEmpty(holding.SymbolOrName))
                                    {
                                        var latestPrice = await dbContext.PriceHistory
                                            .Where(p => p.SymbolOrName == holding.SymbolOrName)
                                            .OrderByDescending(p => p.PriceDate)
                                            .Select(p => p.Price)
                                            .FirstOrDefaultAsync();

                                        currentCalculatedValue = latestPrice;
                                        if (latestPrice > alert.ThresholdValue)
                                        {
                                            shouldTrigger = true;
                                            alertMessage = $"Alert! {holding.SymbolOrName} price is ${latestPrice:F2}, which exceeds your threshold of ${alert.ThresholdValue:F2}.";
                                        }
                                    }
                                }
                                break;

                            case "AllocationDrift":
                                if (latestSnapshot != null)
                                {
                                    // Parse AssetAllocationJson to find Equity (Stock + ETF)
                                    using var doc = JsonDocument.Parse(latestSnapshot.AssetAllocationJson);
                                    decimal equityPct = 0;
                                    foreach (var item in doc.RootElement.EnumerateArray())
                                    {
                                        var type = item.GetProperty("AssetType").GetString();
                                        if (type == "Stock" || type == "ETF")
                                        {
                                            equityPct += item.GetProperty("Percentage").GetDecimal();
                                        }
                                    }

                                    currentCalculatedValue = equityPct;
                                    if (equityPct > alert.ThresholdValue)
                                    {
                                        shouldTrigger = true;
                                        alertMessage = $"Alert! Equity allocation is {equityPct:F2}%, exceeding drift limit of {alert.ThresholdValue:F2}%.";
                                    }
                                }
                                break;

                            case "FDMaturity":
                                // Check all FD holdings
                                var fdHoldings = user.Holdings.Where(h => h.AssetType == "FixedDeposit" && h.MaturityDate.HasValue).ToList();
                                foreach (var fd in fdHoldings)
                                {
                                    var daysToMaturity = (fd.MaturityDate!.Value.Date - DateTime.UtcNow.Date).Days;
                                    currentCalculatedValue = daysToMaturity;

                                    // If maturity is within 15 days (and in the future or today)
                                    if (daysToMaturity >= 0 && daysToMaturity <= 15)
                                    {
                                        shouldTrigger = true;
                                        alertMessage = $"Alert! Fixed Deposit with {fd.SymbolOrName} of ${fd.Principal:F2} matures in {daysToMaturity} days on {fd.MaturityDate:yyyy-MM-dd}.";
                                        alert.HoldingId = fd.Id;
                                        break; // Trigger first matching FD for simplicity
                                    }
                                }
                                break;

                            case "PortfolioConcentration":
                                if (latestSnapshot != null)
                                {
                                    currentCalculatedValue = latestSnapshot.LargestHoldingPercentage * 100m; // Convert to percent
                                    if (currentCalculatedValue > alert.ThresholdValue)
                                    {
                                        shouldTrigger = true;
                                        alertMessage = $"Alert! Single asset concentration is {currentCalculatedValue:F2}%, which exceeds security threshold of {alert.ThresholdValue:F2}%.";
                                    }
                                }
                                break;
                        }

                        if (shouldTrigger)
                        {
                            alert.IsTriggered = true;
                            alert.TriggeredAt = DateTime.UtcNow;
                            alert.CurrentValue = currentCalculatedValue;
                            alert.Message = alertMessage;
                        }
                        else
                        {
                            // Reset if condition is no longer met (optional, but keep history by only setting false if it wasn't triggered today)
                            // For simplicity, if it's resolved, mark it untriggered so it can fire again.
                            alert.CurrentValue = currentCalculatedValue;
                        }
                    }

                    await dbContext.SaveChangesAsync();

                    // 3. Complete Job
                    await jobTracker.CompleteExecutionAsync(executionId, payload.JobId);

                    _logger.LogInformation("Completed AlertJob. ID: {JobId}", payload.JobId);
                    _channel.BasicAck(ea.DeliveryTag, false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process AlertJob. ID: {JobId}", payload.JobId);

                    if (payload.RetryCount < 3)
                    {
                        await jobTracker.FailExecutionAsync(executionId, payload.JobId, ex.Message, moveToDeadLetter: false);
                        _channel.BasicAck(ea.DeliveryTag, false);

                        var backoffDelay = TimeSpan.FromSeconds(Math.Pow(2, payload.RetryCount + 1));
                        _logger.LogWarning("Re-queueing AlertJob with backoff. Delaying {Delay} seconds...", backoffDelay.TotalSeconds);

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
                                    routingKey: "job.alert",
                                    basicProperties: properties,
                                    body: bodyBytes
                                );
                            }
                        });
                    }
                    else
                    {
                        await jobTracker.FailExecutionAsync(executionId, payload.JobId, $"Retries exhausted. Error: {ex.Message}", moveToDeadLetter: true);
                        _logger.LogError("AlertJob {JobId} exhausted retries. Rejecting to DLQ.", payload.JobId);
                        _channel.BasicReject(ea.DeliveryTag, false);
                    }
                }
            };

            _channel.BasicConsume(queue: QueueName, autoAck: false, consumer: consumer);

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
