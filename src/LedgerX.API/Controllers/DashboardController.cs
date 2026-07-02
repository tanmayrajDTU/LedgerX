using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LedgerX.Core.Entities;
using LedgerX.Core.Interfaces;
using LedgerX.Core.Models;
using LedgerX.Infrastructure.Data;
using LedgerX.Infrastructure.Messaging;

namespace LedgerX.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class DashboardController : ControllerBase
    {
        private readonly LedgerXDbContext _dbContext;
        private readonly ICacheService _cacheService;
        private readonly IMessageBroker _messageBroker;
        private readonly IJobTracker _jobTracker;

        public DashboardController(
            LedgerXDbContext dbContext,
            ICacheService cacheService,
            IMessageBroker messageBroker,
            IJobTracker jobTracker)
        {
            _dbContext = dbContext;
            _cacheService = cacheService;
            _messageBroker = messageBroker;
            _jobTracker = jobTracker;
        }

        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary()
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
            string cacheKey = $"dashboard:summary:{userId}";

            // 1. Try to read from Redis cache
            var cachedSummary = await _cacheService.GetAsync<string>(cacheKey);
            if (!string.IsNullOrEmpty(cachedSummary))
            {
                return Content(cachedSummary, "application/json");
            }

            // 2. Fetch latest snapshot from DB
            var snapshot = await _dbContext.AnalyticsSnapshots
                .Where(s => s.UserId == userId)
                .OrderByDescending(s => s.SnapshotDate)
                .FirstOrDefaultAsync();

            // 3. Fallback: If no snapshot exists, we can queue a job and return an initial empty state
            if (snapshot == null)
            {
                // Trigger an immediate background job to calculate analytics
                var payload = new AnalyticsJobPayload { UserId = userId, RetryCount = 0 };
                var jobId = await _jobTracker.CreateJobAsync("Analytics", JsonSerializer.Serialize(payload));
                payload.JobId = jobId;
                await _messageBroker.PublishJobAsync("job.analytics", payload);

                // Queue alert job as well
                var alertPayload = new AlertJobPayload { UserId = userId, RetryCount = 0 };
                var alertJobId = await _jobTracker.CreateJobAsync("Alert", JsonSerializer.Serialize(alertPayload));
                alertPayload.JobId = alertJobId;
                await _messageBroker.PublishJobAsync("job.alert", alertPayload);

                // Return a temporary blank model
                var emptyModel = new
                {
                    IsCalculating = true,
                    NetWorth = 0m,
                    InvestedAmount = 0m,
                    CurrentValue = 0m,
                    TotalGainLoss = 0m,
                    Cagr = 0m,
                    AbsoluteReturn = 0m,
                    Volatility = 0m,
                    ConcentrationRisk = 0m,
                    DiversificationScore = 100m,
                    PortfolioGrowth = new List<object>(),
                    AssetAllocation = new List<object>(),
                    SectorAllocation = new List<object>(),
                    TopGainers = new List<object>(),
                    TopLosers = new List<object>(),
                    UpcomingMaturities = new List<object>(),
                    ActiveAlertsCount = 0
                };
                return Ok(emptyModel);
            }

            // 4. Calculate live widgets (Top Gainers/Losers, FD Maturities, Active Alerts)
            var holdings = await _dbContext.Holdings
                .Where(h => h.UserId == userId)
                .ToListAsync();

            var gainersList = new List<object>();
            var losersList = new List<object>();
            var fdMaturities = new List<object>();

            foreach (var h in holdings)
            {
                if (h.AssetType == "Stock" || h.AssetType == "ETF" || h.AssetType == "MutualFund")
                {
                    var price = await _cacheService.GetAsync<decimal>($"prices:latest:{h.SymbolOrName}");
                    if (price == 0)
                    {
                        price = await _dbContext.PriceHistory
                            .Where(p => p.SymbolOrName == h.SymbolOrName)
                            .OrderByDescending(p => p.PriceDate)
                            .Select(p => p.Price)
                            .FirstOrDefaultAsync();
                    }

                    decimal currentPrice = price > 0 ? price : (h.BuyPriceOrNAV ?? 0);
                    decimal buyPrice = h.BuyPriceOrNAV ?? 0;
                    
                    if (buyPrice > 0)
                    {
                        decimal gainLossPct = ((currentPrice - buyPrice) / buyPrice) * 100m;
                        var gainLossAmt = (h.QuantityOrUnits ?? 0) * (currentPrice - buyPrice);

                        var widgetItem = new
                        {
                            h.SymbolOrName,
                            h.AssetType,
                            GainLossPercentage = Math.Round(gainLossPct, 2),
                            GainLossAmount = Math.Round(gainLossAmt, 2),
                            CurrentValue = Math.Round((h.QuantityOrUnits ?? 0) * currentPrice, 2)
                        };

                        if (gainLossPct > 0)
                            gainersList.Add(widgetItem);
                        else if (gainLossPct < 0)
                            losersList.Add(widgetItem);
                    }
                }
                else if (h.AssetType == "FixedDeposit" && h.MaturityDate.HasValue)
                {
                    var daysToMaturity = (h.MaturityDate.Value.Date - DateTime.UtcNow.Date).Days;
                    if (daysToMaturity >= 0 && daysToMaturity <= 30) // FD maturing within 30 days
                    {
                        fdMaturities.Add(new
                        {
                            Bank = h.SymbolOrName,
                            Principal = h.Principal,
                            h.MaturityDate,
                            DaysRemaining = daysToMaturity
                        });
                    }
                }
            }

            // Order gainers and losers
            var topGainers = gainersList.OrderByDescending(g => ((dynamic)g).GainLossPercentage).Take(3).ToList();
            var topLosers = losersList.OrderBy(l => ((dynamic)l).GainLossPercentage).Take(3).ToList();

            var activeAlertsCount = await _dbContext.Alerts
                .Where(a => a.UserId == userId && a.IsTriggered)
                .CountAsync();

            // Construct final response combining snapshot + live widgets
            var response = new
            {
                IsCalculating = false,
                snapshot.NetWorth,
                snapshot.InvestedAmount,
                snapshot.CurrentValue,
                snapshot.TotalGainLoss,
                Cagr = snapshot.Cagr * 100m,
                AbsoluteReturn = snapshot.AbsoluteReturn * 100m,
                Volatility = snapshot.Volatility * 100m,
                ConcentrationRisk = snapshot.ConcentrationRisk * 100m,
                snapshot.DiversificationScore,
                PortfolioGrowth = JsonSerializer.Deserialize<List<object>>(snapshot.PortfolioGrowthJson),
                AssetAllocation = JsonSerializer.Deserialize<List<object>>(snapshot.AssetAllocationJson),
                SectorAllocation = JsonSerializer.Deserialize<List<object>>(snapshot.SectorAllocationJson),
                TopGainers = topGainers,
                TopLosers = topLosers,
                UpcomingMaturities = fdMaturities,
                ActiveAlertsCount = activeAlertsCount
            };

            var responseJson = JsonSerializer.Serialize(response);
            
            // Cache the calculated summary for 1 hour
            await _cacheService.SetAsync(cacheKey, responseJson, TimeSpan.FromHours(1));

            return Content(responseJson, "application/json");
        }

        [HttpPost("refresh")]
        public async Task<IActionResult> RefreshDashboard()
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);

            // Invalidate Redis caches
            await _cacheService.RemoveAsync($"dashboard:summary:{userId}");
            await _cacheService.RemoveAsync($"holdings:list:{userId}");

            // Queue recalculation jobs immediately
            var symbols = await _dbContext.Holdings
                .Where(h => h.UserId == userId && (h.AssetType == "Stock" || h.AssetType == "ETF" || h.AssetType == "MutualFund"))
                .Select(h => new { h.SymbolOrName, h.AssetType })
                .Distinct()
                .ToListAsync();

            foreach (var sym in symbols)
            {
                var payload = new MarketDataJobPayload
                {
                    Symbol = sym.SymbolOrName,
                    AssetType = sym.AssetType,
                    RetryCount = 0,
                    InjectFault = false
                };
                var jobId = await _jobTracker.CreateJobAsync("MarketData", JsonSerializer.Serialize(payload));
                payload.JobId = jobId;
                await _messageBroker.PublishJobAsync("job.marketdata", payload);
            }

            // Queue Analytics Job
            var analPayload = new AnalyticsJobPayload { UserId = userId, RetryCount = 0 };
            var analJobId = await _jobTracker.CreateJobAsync("Analytics", JsonSerializer.Serialize(analPayload));
            analPayload.JobId = analJobId;
            await _messageBroker.PublishJobAsync("job.analytics", analPayload);

            // Queue Alert Job
            var alertPayload = new AlertJobPayload { UserId = userId, RetryCount = 0 };
            var alertJobId = await _jobTracker.CreateJobAsync("Alert", JsonSerializer.Serialize(alertPayload));
            alertPayload.JobId = alertJobId;
            await _messageBroker.PublishJobAsync("job.alert", alertPayload);

            return Ok(new { Message = "Dashboard refresh scheduled. Background jobs have been dispatched to RabbitMQ." });
        }
    }
}
