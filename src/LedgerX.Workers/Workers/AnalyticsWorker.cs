using System;
using System.Collections.Generic;
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
    public class AnalyticsWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<AnalyticsWorker> _logger;
        private readonly IConfiguration _configuration;
        private IConnection? _connection;
        private IModel? _channel;
        private const string QueueName = "ledgerx.analytics.queue";
        private const string ExchangeName = "ledgerx.jobs.exchange";

        public AnalyticsWorker(
            IServiceProvider serviceProvider,
            ILogger<AnalyticsWorker> logger,
            IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("AnalyticsWorker background service starting...");

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
                    _logger.LogInformation("AnalyticsWorker successfully connected to RabbitMQ.");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("AnalyticsWorker failed to connect to RabbitMQ. Retrying in 5 seconds...");
                    await Task.Delay(5000, stoppingToken);
                }
            }

            if (_channel == null) return;

            // Ensure queue and DLQ declarations match RabbitMQMessageBroker topology
            _channel.ExchangeDeclare(ExchangeName, ExchangeType.Direct, durable: true);
            _channel.ExchangeDeclare("ledgerx.deadletter.exchange", ExchangeType.Direct, durable: true);
            
            _channel.QueueDeclare("ledgerx.analytics.dlq", durable: true, exclusive: false, autoDelete: false, arguments: null);
            _channel.QueueBind("ledgerx.analytics.dlq", "ledgerx.deadletter.exchange", "deadletter.analytics", null);

            var args = new System.Collections.Generic.Dictionary<string, object>
            {
                { "x-dead-letter-exchange", "ledgerx.deadletter.exchange" },
                { "x-dead-letter-routing-key", "deadletter.analytics" }
            };
            _channel.QueueDeclare(QueueName, durable: true, exclusive: false, autoDelete: false, arguments: args);
            _channel.QueueBind(QueueName, ExchangeName, "job.analytics", null);

            var consumer = new EventingBasicConsumer(_channel);
            consumer.Received += async (model, ea) =>
            {
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);
                AnalyticsJobPayload? payload = null;

                try
                {
                    payload = JsonSerializer.Deserialize<AnalyticsJobPayload>(message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to deserialize analytics job payload: {Msg}", message);
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
                var cacheService = scope.ServiceProvider.GetRequiredService<ICacheService>();

                var executionId = await jobTracker.StartExecutionAsync(payload.JobId, nameof(AnalyticsWorker), payload.RetryCount);

                try
                {
                    _logger.LogInformation("Processing AnalyticsJob. ID: {JobId}, User: {UserId}", payload.JobId, payload.UserId);

                    // 1. Fetch User data
                    var user = await dbContext.Users
                        .Include(u => u.Holdings)
                        .ThenInclude(h => h.Transactions)
                        .FirstOrDefaultAsync(u => u.Id == payload.UserId);

                    if (user == null)
                    {
                        throw new Exception($"User with ID {payload.UserId} not found.");
                    }

                    // 2. Fetch Latest Prices from DB/Cache
                    var holdingsList = user.Holdings.ToList();
                    var latestPrices = new Dictionary<string, decimal>();

                    foreach (var h in holdingsList)
                    {
                        if (string.IsNullOrEmpty(h.SymbolOrName)) continue;
                        
                        if (h.AssetType == "Stock" || h.AssetType == "ETF" || h.AssetType == "MutualFund")
                        {
                            var price = await dbContext.PriceHistory
                                .Where(p => p.SymbolOrName == h.SymbolOrName)
                                .OrderByDescending(p => p.PriceDate)
                                .Select(p => p.Price)
                                .FirstOrDefaultAsync();

                            latestPrices[h.SymbolOrName] = price > 0 ? price : (h.BuyPriceOrNAV ?? 0);
                        }
                    }

                    // 3. Compute Aggregates
                    decimal netWorth = 0;
                    decimal investedAmount = 0;
                    var assetValues = new Dictionary<string, decimal>();
                    var sectorValues = new Dictionary<string, decimal>();

                    // Map mock sectors for symbols
                    var sectorMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "AAPL", "Technology" },
                        { "MSFT", "Technology" },
                        { "VOO", "Index Funds" },
                        { "FXAIX", "Mutual Funds" }
                    };

                    foreach (var h in holdingsList)
                    {
                        decimal holdingCurrentVal = 0;
                        decimal holdingInvestedVal = 0;

                        if (h.AssetType == "Stock" || h.AssetType == "ETF" || h.AssetType == "MutualFund")
                        {
                            var qty = h.QuantityOrUnits ?? 0;
                            var buyPrice = h.BuyPriceOrNAV ?? 0;
                            var currentPrice = latestPrices.TryGetValue(h.SymbolOrName, out var p) ? p : buyPrice;

                            holdingCurrentVal = qty * currentPrice;
                            holdingInvestedVal = qty * buyPrice;

                            var sector = sectorMap.TryGetValue(h.SymbolOrName, out var sec) ? sec : "Other";
                            sectorValues[sector] = sectorValues.GetValueOrDefault(sector) + holdingCurrentVal;
                        }
                        else if (h.AssetType == "FixedDeposit")
                        {
                            holdingInvestedVal = h.Principal ?? 0;
                            
                            // Calculate accrued interest
                            decimal interest = 0;
                            if (h.StartDate.HasValue && h.InterestRate.HasValue)
                            {
                                int daysAccrued = Math.Max(0, (DateTime.UtcNow - h.StartDate.Value).Days);
                                interest = holdingInvestedVal * (h.InterestRate.Value / 100m) * (daysAccrued / 365.0m);
                            }
                            
                            holdingCurrentVal = holdingInvestedVal + interest;
                            sectorValues["Cash/Fixed Income"] = sectorValues.GetValueOrDefault("Cash/Fixed Income") + holdingCurrentVal;
                        }
                        else if (h.AssetType == "SavingsAccount")
                        {
                            holdingCurrentVal = h.Balance ?? 0;
                            holdingInvestedVal = holdingCurrentVal;
                            sectorValues["Cash/Fixed Income"] = sectorValues.GetValueOrDefault("Cash/Fixed Income") + holdingCurrentVal;
                        }

                        netWorth += holdingCurrentVal;
                        investedAmount += holdingInvestedVal;
                        assetValues[h.AssetType] = assetValues.GetValueOrDefault(h.AssetType) + holdingCurrentVal;
                    }

                    decimal totalGainLoss = netWorth - investedAmount;
                    decimal absoluteReturn = investedAmount > 0 ? (totalGainLoss / investedAmount) : 0;

                    // 4. Calculate CAGR
                    decimal cagr = 0;
                    var oldestTransactionDate = await dbContext.Transactions
                        .Where(t => t.UserId == user.Id)
                        .OrderBy(t => t.TransactionDate)
                        .Select(t => t.TransactionDate)
                        .FirstOrDefaultAsync();

                    if (oldestTransactionDate != default && investedAmount > 0 && netWorth > 0)
                    {
                        double days = (DateTime.UtcNow - oldestTransactionDate).TotalDays;
                        double years = days / 365.0;
                        if (years > 0.05) // Need at least ~2 weeks of history for CAGR
                        {
                            try
                            {
                                double ratio = (double)(netWorth / investedAmount);
                                cagr = (decimal)Math.Pow(ratio, 1.0 / years) - 1m;
                            }
                            catch
                            {
                                cagr = 0;
                            }
                        }
                        else
                        {
                            cagr = absoluteReturn; // Fallback to absolute return if holding period is very short
                        }
                    }

                    // 5. Portfolio Growth History (30 Days)
                    var growthPoints = new List<object>();
                    for (int i = 30; i >= 0; i--)
                    {
                        var day = DateTime.UtcNow.AddDays(-i).Date;
                        // For a real app, reconstruct holding quantities on "day" from transactions
                        // and multiply by the close price on "day" from PriceHistory.
                        // Here, we'll simulate daily value based on seed price history to ensure clean metrics.
                        decimal dayNetWorth = 0;
                        foreach (var h in holdingsList)
                        {
                            if (h.AssetType == "Stock" || h.AssetType == "ETF" || h.AssetType == "MutualFund")
                            {
                                // Check if user bought it before this day
                                var qtyOnDay = h.Transactions
                                    .Where(t => t.TransactionDate.Date <= day && (t.TransactionType == "Buy" || t.TransactionType == "Deposit"))
                                    .Sum(t => t.QuantityOrUnits ?? 0)
                                    - h.Transactions
                                    .Where(t => t.TransactionDate.Date <= day && (t.TransactionType == "Sell" || t.TransactionType == "Withdrawal"))
                                    .Sum(t => t.QuantityOrUnits ?? 0);

                                var dayPrice = await dbContext.PriceHistory
                                    .Where(p => p.SymbolOrName == h.SymbolOrName && p.PriceDate.Date <= day)
                                    .OrderByDescending(p => p.PriceDate)
                                    .Select(p => p.Price)
                                    .FirstOrDefaultAsync();

                                if (dayPrice == 0)
                                {
                                    dayPrice = h.BuyPriceOrNAV ?? 0;
                                }

                                dayNetWorth += qtyOnDay * dayPrice;
                            }
                            else if (h.AssetType == "FixedDeposit" && h.StartDate.HasValue && h.StartDate.Value.Date <= day)
                            {
                                decimal principal = h.Principal ?? 0;
                                int daysAccrued = (day - h.StartDate.Value.Date).Days;
                                decimal interest = principal * ((h.InterestRate ?? 0) / 100m) * (Math.Max(0, daysAccrued) / 365.0m);
                                dayNetWorth += principal + interest;
                            }
                            else if (h.AssetType == "SavingsAccount")
                            {
                                // Get sum of deposits - withdrawals before this day
                                var balOnDay = h.Transactions
                                    .Where(t => t.TransactionDate.Date <= day && (t.TransactionType == "Deposit" || t.TransactionType == "InterestCredit"))
                                    .Sum(t => t.Amount)
                                    - h.Transactions
                                    .Where(t => t.TransactionDate.Date <= day && (t.TransactionType == "Withdrawal" || t.TransactionType == "Sell"))
                                    .Sum(t => t.Amount);

                                dayNetWorth += balOnDay;
                            }
                        }

                        if (dayNetWorth == 0) dayNetWorth = netWorth; // Fallback to current

                        growthPoints.Add(new { Date = day.ToString("yyyy-MM-dd"), Value = Math.Round(dayNetWorth, 2) });
                    }

                    // 6. Volatility Calculation (standard deviation of daily growth returns)
                    decimal volatility = 0.05m; // Default simulated volatility (5%)
                    if (growthPoints.Count > 1)
                    {
                        var values = growthPoints.Select(g => (decimal)((dynamic)g).Value).ToList();
                        var returns = new List<double>();
                        for (int i = 1; i < values.Count; i++)
                        {
                            if (values[i - 1] > 0)
                            {
                                returns.Add((double)((values[i] - values[i - 1]) / values[i - 1]));
                            }
                        }
                        if (returns.Any())
                        {
                            double avgReturn = returns.Average();
                            double sumOfSquares = returns.Select(r => Math.Pow(r - avgReturn, 2)).Sum();
                            double dailyStdDev = Math.Sqrt(sumOfSquares / returns.Count);
                            // Annualize the daily volatility
                            volatility = (decimal)(dailyStdDev * Math.Sqrt(252));
                        }
                    }

                    // 7. Concentration Risk & Largest Holding %
                    decimal largestHoldingPercentage = 0;
                    decimal concentrationRisk = 0; // HHI Index: sum of squared weights (0 to 1)

                    if (netWorth > 0)
                    {
                        var holdingValues = new List<decimal>();
                        foreach (var h in holdingsList)
                        {
                            decimal currentVal = 0;
                            if (h.AssetType == "Stock" || h.AssetType == "ETF" || h.AssetType == "MutualFund")
                            {
                                var qty = h.QuantityOrUnits ?? 0;
                                var currentPrice = latestPrices.TryGetValue(h.SymbolOrName, out var p) ? p : (h.BuyPriceOrNAV ?? 0);
                                currentVal = qty * currentPrice;
                            }
                            else if (h.AssetType == "FixedDeposit")
                            {
                                currentVal = h.Principal ?? 0;
                            }
                            else if (h.AssetType == "SavingsAccount")
                            {
                                currentVal = h.Balance ?? 0;
                            }
                            holdingValues.Add(currentVal);
                        }

                        if (holdingValues.Any())
                        {
                            var weights = holdingValues.Select(v => v / netWorth).ToList();
                            largestHoldingPercentage = weights.Max();
                            concentrationRisk = weights.Select(w => w * w).Sum();
                        }
                    }

                    // 8. Portfolio Diversification Score (0 to 100)
                    // HHI = 1 (complete concentration) -> Score = 0
                    // HHI = 0.1 (well diversified) -> Score = 100
                    decimal diversificationScore = 100m;
                    if (concentrationRisk > 0)
                    {
                        // Map HHI 0.1 to 100, HHI 1.0 to 0
                        diversificationScore = Math.Max(0, Math.Min(100, (1m - concentrationRisk) * 111.1m));
                    }

                    // 9. Format JSON fields
                    var assetAllocation = assetValues.Select(kvp => new 
                    { 
                        AssetType = kvp.Key, 
                        Value = Math.Round(kvp.Value, 2), 
                        Percentage = netWorth > 0 ? Math.Round((kvp.Value / netWorth) * 100, 2) : 0 
                    }).ToList();

                    var sectorAllocation = sectorValues.Select(kvp => new 
                    { 
                        Sector = kvp.Key, 
                        Value = Math.Round(kvp.Value, 2), 
                        Percentage = netWorth > 0 ? Math.Round((kvp.Value / netWorth) * 100, 2) : 0 
                    }).ToList();

                    // 10. Save Snapshot
                    var snapshot = new AnalyticsSnapshot
                    {
                        UserId = payload.UserId,
                        SnapshotDate = DateTime.UtcNow,
                        NetWorth = Math.Round(netWorth, 2),
                        InvestedAmount = Math.Round(investedAmount, 2),
                        CurrentValue = Math.Round(netWorth, 2),
                        TotalGainLoss = Math.Round(totalGainLoss, 2),
                        PortfolioGrowthJson = JsonSerializer.Serialize(growthPoints),
                        AssetAllocationJson = JsonSerializer.Serialize(assetAllocation),
                        SectorAllocationJson = JsonSerializer.Serialize(sectorAllocation),
                        Cagr = Math.Round(cagr, 4),
                        AbsoluteReturn = Math.Round(absoluteReturn, 4),
                        Volatility = Math.Round(volatility, 4),
                        ConcentrationRisk = Math.Round(concentrationRisk, 4),
                        LargestHoldingPercentage = Math.Round(largestHoldingPercentage, 4),
                        DiversificationScore = Math.Round(diversificationScore, 2)
                    };

                    dbContext.AnalyticsSnapshots.Add(snapshot);
                    await dbContext.SaveChangesAsync();

                    // 11. Invalidate Redis cache for user dashboard
                    await cacheService.RemoveAsync($"dashboard:summary:{payload.UserId}");
                    await cacheService.RemoveAsync($"holdings:list:{payload.UserId}");

                    // 12. Complete Job
                    await jobTracker.CompleteExecutionAsync(executionId, payload.JobId);

                    _logger.LogInformation("Completed AnalyticsJob. ID: {JobId}, NetWorth: {NW}", payload.JobId, netWorth);
                    _channel.BasicAck(ea.DeliveryTag, false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process AnalyticsJob. ID: {JobId}", payload.JobId);

                    if (payload.RetryCount < 3)
                    {
                        await jobTracker.FailExecutionAsync(executionId, payload.JobId, ex.Message, moveToDeadLetter: false);
                        _channel.BasicAck(ea.DeliveryTag, false);

                        var backoffDelay = TimeSpan.FromSeconds(Math.Pow(2, payload.RetryCount + 1));
                        _logger.LogWarning("Re-queueing AnalyticsJob with backoff. Delaying {Delay} seconds...", backoffDelay.TotalSeconds);

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
                                    routingKey: "job.analytics",
                                    basicProperties: properties,
                                    body: bodyBytes
                                );
                            }
                        });
                    }
                    else
                    {
                        await jobTracker.FailExecutionAsync(executionId, payload.JobId, $"Retries exhausted. Error: {ex.Message}", moveToDeadLetter: true);
                        _logger.LogError("AnalyticsJob {JobId} exhausted retries. Rejecting to DLQ.", payload.JobId);
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
