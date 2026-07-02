using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using LedgerX.Core.Entities;
using LedgerX.Infrastructure.Services;

namespace LedgerX.Infrastructure.Data
{
    public static class DbSeeder
    {
        public static async Task SeedAsync(LedgerXDbContext context)
        {
            // Auto-apply migrations
            await context.Database.MigrateAsync();

            if (await context.Users.AnyAsync())
            {
                return; // DB already seeded
            }

            // 1. Seed Users
            var adminUser = new User
            {
                Username = "admin",
                Email = "admin@ledgerx.com",
                PasswordHash = PasswordHasher.HashPassword("Admin@123"),
                Role = "Admin"
            };

            var regularUser = new User
            {
                Username = "user",
                Email = "user@ledgerx.com",
                PasswordHash = PasswordHasher.HashPassword("User@123"),
                Role = "User"
            };

            context.Users.AddRange(adminUser, regularUser);
            await context.SaveChangesAsync();

            // 2. Seed Price History (useful for charting initial mock holdings)
            var prices = new[]
            {
                new { Symbol = "RELIANCE.NS", Type = "Stock", Price = 2450.00m },
                new { Symbol = "TCS.NS", Type = "Stock", Price = 3850.00m },
                new { Symbol = "NIFTYBEES.NS", Type = "ETF", Price = 260.00m },
                new { Symbol = "PARAGPARIKH", Type = "MutualFund", Price = 65.50m }
            };

            var now = DateTime.UtcNow;
            for (int i = 30; i >= 0; i--)
            {
                var date = now.AddDays(-i);
                foreach (var p in prices)
                {
                    // Add minor drift so price history forms a chart
                    decimal randomDrift = 1.0m + ((decimal)(new Random(p.Symbol.GetHashCode() + i).NextDouble() * 6.0 - 3.0) / 100.0m);
                    context.PriceHistory.Add(new PriceHistory
                    {
                        SymbolOrName = p.Symbol,
                        AssetType = p.Type,
                        Price = Math.Round(p.Price * randomDrift, 4),
                        PriceDate = new DateTime(date.Year, date.Month, date.Day, 16, 0, 0, DateTimeKind.Utc),
                        CreatedAt = now
                    });
                }
            }
            await context.SaveChangesAsync();

            // 3. Seed Holdings for "user"
            var relianceHolding = new Holding
            {
                UserId = regularUser.Id,
                AssetType = "Stock",
                SymbolOrName = "RELIANCE.NS",
                QuantityOrUnits = 15m, // Integer quantity for Indian Stock
                BuyPriceOrNAV = 2300.00m,
                UpdatedAt = now
            };

            var niftybeesHolding = new Holding
            {
                UserId = regularUser.Id,
                AssetType = "ETF",
                SymbolOrName = "NIFTYBEES.NS",
                QuantityOrUnits = 100m, // Integer quantity for Indian ETF
                BuyPriceOrNAV = 240.00m,
                UpdatedAt = now
            };

            var mutualFundHolding = new Holding
            {
                UserId = regularUser.Id,
                AssetType = "MutualFund",
                SymbolOrName = "PARAGPARIKH",
                QuantityOrUnits = 250.75m, // Fractional units allowed for Mutual Funds
                BuyPriceOrNAV = 50.00m,
                UpdatedAt = now
            };

            var fdHolding = new Holding
            {
                UserId = regularUser.Id,
                AssetType = "FixedDeposit",
                SymbolOrName = "State Bank of India",
                Principal = 500000m, // ₹5,00,000 principal
                InterestRate = 7.10m,
                StartDate = now.AddDays(-60),
                MaturityDate = now.AddDays(120),
                UpdatedAt = now
            };

            var savingsHolding = new Holding
            {
                UserId = regularUser.Id,
                AssetType = "SavingsAccount",
                SymbolOrName = "HDFC Bank Savings",
                Balance = 125000m, // ₹1,25,000 balance
                UpdatedAt = now
            };

            context.Holdings.AddRange(relianceHolding, niftybeesHolding, mutualFundHolding, fdHolding, savingsHolding);
            await context.SaveChangesAsync();

            // 4. Seed Transaction Records for Audit Log
            // RELIANCE.NS Buy 1: 10 shares @ ₹2200 on day -30
            context.Transactions.Add(new Transaction
            {
                HoldingId = relianceHolding.Id,
                UserId = regularUser.Id,
                TransactionType = "Buy",
                Amount = 22000m,
                QuantityOrUnits = 10m,
                PriceOrNAV = 2200m,
                TransactionDate = now.AddDays(-30)
            });

            // RELIANCE.NS Buy 2: 5 shares @ ₹2500 on day -15
            context.Transactions.Add(new Transaction
            {
                HoldingId = relianceHolding.Id,
                UserId = regularUser.Id,
                TransactionType = "Buy",
                Amount = 12500m,
                QuantityOrUnits = 5m,
                PriceOrNAV = 2500m,
                TransactionDate = now.AddDays(-15)
            });

            // NIFTYBEES.NS Buy: 100 shares @ ₹240 on day -60
            context.Transactions.Add(new Transaction
            {
                HoldingId = niftybeesHolding.Id,
                UserId = regularUser.Id,
                TransactionType = "Buy",
                Amount = 24000m,
                QuantityOrUnits = 100m,
                PriceOrNAV = 240m,
                TransactionDate = now.AddDays(-60)
            });

            // PARAGPARIKH Buy: 250.75 units @ ₹50 on day -45
            context.Transactions.Add(new Transaction
            {
                HoldingId = mutualFundHolding.Id,
                UserId = regularUser.Id,
                TransactionType = "Buy",
                Amount = 12537.50m,
                QuantityOrUnits = 250.75m,
                PriceOrNAV = 50m,
                TransactionDate = now.AddDays(-45)
            });

            // FD Credit Principal: ₹500000 on day -60
            context.Transactions.Add(new Transaction
            {
                HoldingId = fdHolding.Id,
                UserId = regularUser.Id,
                TransactionType = "Deposit",
                Amount = 500000m,
                TransactionDate = now.AddDays(-60)
            });

            // Savings Account: Deposit ₹125000 on day -15
            context.Transactions.Add(new Transaction
            {
                HoldingId = savingsHolding.Id,
                UserId = regularUser.Id,
                TransactionType = "Deposit",
                Amount = 125000m,
                TransactionDate = now.AddDays(-15)
            });

            // 5. Seed Alert Configurations
            context.Alerts.Add(new Alert
            {
                UserId = regularUser.Id,
                HoldingId = relianceHolding.Id,
                AlertType = "Price",
                ThresholdValue = 2600.00m,
                CurrentValue = 2450.00m,
                Message = "RELIANCE.NS Price exceeds ₹2600"
            });

            context.Alerts.Add(new Alert
            {
                UserId = regularUser.Id,
                AlertType = "PortfolioConcentration",
                ThresholdValue = 40.00m,
                CurrentValue = 0.00m,
                Message = "Single holding exceeds 40% of net worth"
            });

            await context.SaveChangesAsync();
        }
    }
}
