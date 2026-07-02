using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace LedgerX.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class SearchController : ControllerBase
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<SearchController> _logger;

        public SearchController(HttpClient httpClient, ILogger<SearchController> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
        }

        public class SearchResultDto
        {
            public string Symbol { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Exchange { get; set; } = string.Empty;
            public string AssetType { get; set; } = string.Empty;
        }

        [HttpGet]
        public async Task<IActionResult> Search([FromQuery] string q)
        {
            if (string.IsNullOrWhiteSpace(q))
            {
                return Ok(new List<SearchResultDto>());
            }

            try
            {
                var url = $"https://query2.finance.yahoo.com/v1/finance/search?q={Uri.EscapeDataString(q)}&quotesCount=10&newsCount=0";
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Yahoo Finance Search API returned non-success code: {Code}", response.StatusCode);
                    return StatusCode((int)response.StatusCode, new { Message = "Yahoo Finance Search failed." });
                }

                var content = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                var results = new List<SearchResultDto>();

                if (root.TryGetProperty("quotes", out var quotes) && quotes.ValueKind == JsonValueKind.Array)
                {
                    foreach (var quote in quotes.EnumerateArray())
                    {
                        if (!quote.TryGetProperty("symbol", out var symbolProp) || symbolProp.ValueKind != JsonValueKind.String)
                        {
                            continue;
                        }

                        var symbol = symbolProp.GetString() ?? string.Empty;

                        // Get name
                        var name = string.Empty;
                        if (quote.TryGetProperty("shortname", out var shortnameProp) && shortnameProp.ValueKind == JsonValueKind.String)
                        {
                            name = shortnameProp.GetString();
                        }
                        else if (quote.TryGetProperty("longname", out var longnameProp) && longnameProp.ValueKind == JsonValueKind.String)
                        {
                            name = longnameProp.GetString();
                        }

                        if (string.IsNullOrWhiteSpace(name))
                        {
                            name = symbol;
                        }

                        // Get exchange
                        var exchange = string.Empty;
                        if (quote.TryGetProperty("exchange", out var exchangeProp) && exchangeProp.ValueKind == JsonValueKind.String)
                        {
                            exchange = exchangeProp.GetString() ?? string.Empty;
                        }

                        // Get asset type mapping
                        var quoteType = string.Empty;
                        if (quote.TryGetProperty("quoteType", out var quoteTypeProp) && quoteTypeProp.ValueKind == JsonValueKind.String)
                        {
                            quoteType = quoteTypeProp.GetString() ?? string.Empty;
                        }

                        var assetType = quoteType.ToUpper() switch
                        {
                            "EQUITY" => "Stock",
                            "ETF" => "ETF",
                            "MUTUALFUND" => "MutualFund",
                            _ => string.Empty
                        };

                        // If not Stock/ETF/MutualFund, skip
                        if (string.IsNullOrEmpty(assetType))
                        {
                            continue;
                        }

                        results.Add(new SearchResultDto
                        {
                            Symbol = symbol,
                            Name = name,
                            Exchange = exchange,
                            AssetType = assetType
                        });
                    }
                }

                // Sort results: prioritize Indian markets (.NS or .BO)
                results.Sort((a, b) =>
                {
                    bool aIsIndian = a.Symbol.EndsWith(".NS", StringComparison.OrdinalIgnoreCase) || a.Symbol.EndsWith(".BO", StringComparison.OrdinalIgnoreCase);
                    bool bIsIndian = b.Symbol.EndsWith(".NS", StringComparison.OrdinalIgnoreCase) || b.Symbol.EndsWith(".BO", StringComparison.OrdinalIgnoreCase);

                    if (aIsIndian && !bIsIndian) return -1;
                    if (!aIsIndian && bIsIndian) return 1;
                    return string.Compare(a.Symbol, b.Symbol, StringComparison.OrdinalIgnoreCase);
                });

                return Ok(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while performing search query: {Query}", q);
                return StatusCode(500, new { Message = "Internal server error during search.", Details = ex.Message });
            }
        }
    }
}
