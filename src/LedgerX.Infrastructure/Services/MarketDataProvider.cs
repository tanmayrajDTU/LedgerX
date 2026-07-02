using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using LedgerX.Core.Interfaces;

namespace LedgerX.Infrastructure.Services
{
    public class MarketDataProvider : IMarketDataProvider
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<MarketDataProvider> _logger;
        private readonly Random _random = new();

        public MarketDataProvider(HttpClient httpClient, ILogger<MarketDataProvider> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            // Add user agent to prevent Yahoo Finance from blocking requests
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
        }

        public async Task<decimal> GetLatestPriceAsync(string symbolOrName, string assetType, bool useLiveData, bool injectFault = false)
        {
            _logger.LogInformation("Fetch request: Symbol={Symbol}, Type={Type}, UseLive={Live}, InjectFault={Fault}", 
                symbolOrName, assetType, useLiveData, injectFault);

            // 1. Check for Fault Injection
            if (injectFault)
            {
                _logger.LogWarning("Fault Injection Triggered for {Symbol}", symbolOrName);
                throw new HttpRequestException($"Fault-injection triggered: simulated transient network failure for {symbolOrName}.");
            }

            // 2. Fetch Live Data if requested and the asset type is Stock or ETF (which Yahoo supports)
            if (useLiveData && (assetType.Equals("Stock", StringComparison.OrdinalIgnoreCase) || 
                                assetType.Equals("ETF", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    var cleanSymbol = symbolOrName.Trim().ToUpper();
                    var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{cleanSymbol}?interval=1d&range=1d";
                    
                    var response = await _httpClient.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(content);
                        var root = doc.RootElement;
                        
                        if (root.TryGetProperty("chart", out var chart) && 
                            chart.TryGetProperty("result", out var result) && 
                            result.GetArrayLength() > 0)
                        {
                            var meta = result[0].GetProperty("meta");
                            if (meta.TryGetProperty("regularMarketPrice", out var priceElement))
                            {
                                decimal price = priceElement.GetDecimal();
                                _logger.LogInformation("Fetched Live Price for {Symbol}: {Price}", symbolOrName, price);
                                return price;
                            }
                        }
                    }
                    _logger.LogWarning("Could not parse Yahoo Finance response for {Symbol}. Falling back to simulated price.", symbolOrName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to fetch live price for {Symbol}. Falling back to simulation.", symbolOrName);
                }
            }

            // 3. Fallback or Default to Simulation
            return GetSimulatedPrice(symbolOrName, assetType);
        }

        private decimal GetSimulatedPrice(string symbolOrName, string assetType)
        {
            // Seed a value based on the hashcode of the symbol to keep it relatively stable per asset
            int seed = symbolOrName.GetHashCode();
            var localRandom = new Random(seed);
            
            double basePrice = assetType.ToLower() switch
            {
                "stock" => localRandom.Next(50, 1500),
                "etf" => localRandom.Next(80, 400),
                "mutualfund" => localRandom.Next(10, 150),
                _ => 100.0
            };

            // Add minor random fluctuation (-2% to +2%)
            double fluctuation = (_random.NextDouble() * 4.0 - 2.0) / 100.0;
            decimal finalPrice = (decimal)(basePrice * (1.0 + fluctuation));
            
            return Math.Round(finalPrice, 4);
        }
    }
}
