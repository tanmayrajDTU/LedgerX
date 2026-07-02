using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LedgerX.Core.Entities;
using LedgerX.Core.Interfaces;
using LedgerX.Infrastructure.Data;
using LedgerX.Infrastructure.Services;
using Microsoft.Extensions.Configuration;

namespace LedgerX.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class HoldingsController : ControllerBase
    {
        private readonly LedgerXDbContext _dbContext;
        private readonly TransactionService _transactionService;
        private readonly ICacheService _cacheService;
        private readonly IMarketDataProvider _marketDataProvider;
        private readonly IConfiguration _configuration;

        public HoldingsController(
            LedgerXDbContext dbContext,
            TransactionService transactionService,
            ICacheService cacheService,
            IMarketDataProvider marketDataProvider,
            IConfiguration configuration)
        {
            _dbContext = dbContext;
            _transactionService = transactionService;
            _cacheService = cacheService;
            _marketDataProvider = marketDataProvider;
            _configuration = configuration;
        }

        public class HoldingDto
        {
            public string AssetType { get; set; } = string.Empty; // Stock, ETF, MutualFund, FixedDeposit, SavingsAccount
            public string SymbolOrName { get; set; } = string.Empty;
            public decimal? QuantityOrUnits { get; set; }
            public decimal? BuyPriceOrNAV { get; set; }
            public decimal? Principal { get; set; }
            public decimal? InterestRate { get; set; }
            public DateTime? StartDate { get; set; }
            public DateTime? MaturityDate { get; set; }
            public decimal? Balance { get; set; }
        }

        [HttpGet]
        public async Task<IActionResult> GetHoldings()
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
            
            // Check cache first
            var cachedHoldings = await _cacheService.GetAsync<object>($"holdings:list:{userId}");
            if (cachedHoldings != null)
            {
                return Ok(cachedHoldings);
            }

            var holdings = await _dbContext.Holdings
                .Where(h => h.UserId == userId)
                .ToListAsync();

            var holdingsWithLivePrices = await Task.WhenAll(holdings.Select(async h =>
            {
                decimal latestPrice = 0;
                decimal currentValue = 0;
                decimal totalGainLoss = 0;

                if (h.AssetType == "Stock" || h.AssetType == "ETF" || h.AssetType == "MutualFund")
                {
                    // Check Redis cache for latest price
                    var price = await _cacheService.GetAsync<decimal>($"prices:latest:{h.SymbolOrName}");
                    if (price == 0)
                    {
                        // Fallback to database
                        price = await _dbContext.PriceHistory
                            .Where(p => p.SymbolOrName == h.SymbolOrName)
                            .OrderByDescending(p => p.PriceDate)
                            .Select(p => p.Price)
                            .FirstOrDefaultAsync();
                    }

                    latestPrice = price > 0 ? price : (h.BuyPriceOrNAV ?? 0);
                    currentValue = (h.QuantityOrUnits ?? 0) * latestPrice;
                    totalGainLoss = currentValue - ((h.QuantityOrUnits ?? 0) * (h.BuyPriceOrNAV ?? 0));
                }
                else if (h.AssetType == "FixedDeposit")
                {
                    decimal principal = h.Principal ?? 0;
                    decimal interest = 0;
                    if (h.StartDate.HasValue && h.InterestRate.HasValue)
                    {
                        int days = Math.Max(0, (DateTime.UtcNow - h.StartDate.Value).Days);
                        interest = principal * (h.InterestRate.Value / 100m) * (days / 365.0m);
                    }
                    latestPrice = 1.0m;
                    currentValue = principal + interest;
                    totalGainLoss = interest;
                }
                else if (h.AssetType == "SavingsAccount")
                {
                    latestPrice = 1.0m;
                    currentValue = h.Balance ?? 0;
                    totalGainLoss = 0;
                }

                return new
                {
                    h.Id,
                    h.AssetType,
                    h.SymbolOrName,
                    h.QuantityOrUnits,
                    h.BuyPriceOrNAV,
                    h.Principal,
                    h.InterestRate,
                    h.StartDate,
                    h.MaturityDate,
                    h.Balance,
                    LatestPrice = latestPrice,
                    CurrentValue = currentValue,
                    TotalGainLoss = totalGainLoss,
                    h.CreatedAt,
                    h.UpdatedAt
                };
            }));

            // Save to cache
            await _cacheService.SetAsync($"holdings:list:{userId}", holdingsWithLivePrices, TimeSpan.FromHours(1));

            return Ok(holdingsWithLivePrices);
        }

        [HttpPost]
        public async Task<IActionResult> CreateHolding([FromBody] HoldingDto dto)
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);

            if (string.IsNullOrWhiteSpace(dto.AssetType) || string.IsNullOrWhiteSpace(dto.SymbolOrName))
            {
                return BadRequest(new { Message = "AssetType and SymbolOrName are required." });
            }

            var holding = new Holding
            {
                UserId = userId,
                AssetType = dto.AssetType,
                SymbolOrName = dto.SymbolOrName,
                QuantityOrUnits = 0, // Recalculated upon transaction creation
                BuyPriceOrNAV = 0,    // Recalculated upon transaction creation
                Principal = dto.Principal,
                InterestRate = dto.InterestRate,
                StartDate = dto.StartDate,
                MaturityDate = dto.MaturityDate,
                Balance = 0,          // Recalculated upon transaction creation
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _dbContext.Holdings.Add(holding);
            await _dbContext.SaveChangesAsync(); // Generate holding ID

            // Generate initial transaction for audit trail
            string txType = dto.AssetType switch
            {
                "Stock" => "Buy",
                "ETF" => "Buy",
                "MutualFund" => "Buy",
                _ => "Deposit"
            };

            decimal amount = dto.AssetType switch
            {
                "FixedDeposit" => dto.Principal ?? 0,
                "SavingsAccount" => dto.Balance ?? 0,
                _ => (dto.QuantityOrUnits ?? 0) * (dto.BuyPriceOrNAV ?? 0)
            };

            await _transactionService.ProcessTransactionAsync(
                userId,
                holding.Id,
                txType,
                amount,
                dto.QuantityOrUnits,
                dto.BuyPriceOrNAV,
                DateTime.UtcNow
            );

            // Fetch and seed initial latest price (either live or simulated based on configuration)
            try
            {
                bool? liveDataOverride = await _cacheService.GetAsync<bool>("config:marketdata:use-live");
                bool useLiveData = liveDataOverride ?? _configuration.GetValue<bool>("MarketData:UseLiveData", false);
                
                decimal latestPrice = await _marketDataProvider.GetLatestPriceAsync(holding.SymbolOrName, holding.AssetType, useLiveData, false);
                
                // Save to database price history if it doesn't already exist for today
                var today = DateTime.UtcNow.Date;
                var alreadyExists = await _dbContext.PriceHistory.AnyAsync(p => p.SymbolOrName == holding.SymbolOrName && p.PriceDate.Date == today);
                if (!alreadyExists)
                {
                    _dbContext.PriceHistory.Add(new PriceHistory
                    {
                        SymbolOrName = holding.SymbolOrName,
                        AssetType = holding.AssetType,
                        Price = latestPrice,
                        PriceDate = DateTime.UtcNow
                    });
                    await _dbContext.SaveChangesAsync();
                }

                // Cache in Redis
                await _cacheService.SetAsync($"prices:latest:{holding.SymbolOrName}", latestPrice, TimeSpan.FromHours(24));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to seed initial price for {holding.SymbolOrName}: {ex.Message}");
            }

            // Re-fetch final holding
            var finalHolding = await _dbContext.Holdings.FindAsync(holding.Id);
            return CreatedAtAction(nameof(GetHoldings), new { id = holding.Id }, finalHolding);
        }
    }
}
