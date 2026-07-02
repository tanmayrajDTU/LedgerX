using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using LedgerX.Core.Entities;
using LedgerX.Core.Interfaces;
using LedgerX.Infrastructure.Data;

namespace LedgerX.Infrastructure.Services
{
    public class TransactionService
    {
        private readonly LedgerXDbContext _dbContext;
        private readonly ICacheService _cacheService;

        public TransactionService(LedgerXDbContext dbContext, ICacheService cacheService)
        {
            _dbContext = dbContext;
            _cacheService = cacheService;
        }

        public async Task<Transaction> ProcessTransactionAsync(
            Guid userId,
            Guid holdingId,
            string transactionType, // Buy, Sell, Dividend, InterestCredit, Deposit, Withdrawal
            decimal amount,
            decimal? quantityOrUnits = null,
            decimal? priceOrNAV = null,
            DateTime? transactionDate = null)
        {
            var holding = await _dbContext.Holdings.FirstOrDefaultAsync(h => h.Id == holdingId && h.UserId == userId);
            if (holding == null)
            {
                throw new ArgumentException("Holding not found for the user.");
            }

            // Enforce integer-only quantities for Stocks and ETFs in the Indian market
            if (holding.AssetType.Equals("Stock", StringComparison.OrdinalIgnoreCase) ||
                holding.AssetType.Equals("ETF", StringComparison.OrdinalIgnoreCase))
            {
                if (quantityOrUnits.HasValue && quantityOrUnits.Value % 1 != 0)
                {
                    throw new ArgumentException("Quantity for Stocks and ETFs cannot be fractional in the Indian market.");
                }
            }

            var transaction = new Transaction
            {
                HoldingId = holdingId,
                UserId = userId,
                TransactionType = transactionType,
                Amount = amount,
                QuantityOrUnits = quantityOrUnits,
                PriceOrNAV = priceOrNAV,
                TransactionDate = transactionDate ?? DateTime.UtcNow
            };

            _dbContext.Transactions.Add(transaction);
            await _dbContext.SaveChangesAsync(); // Save transaction first to include in recalculation

            // Recalculate holding based on transaction history
            await RecalculateHoldingStateAsync(holding);

            // Invalidate Redis caches
            await _cacheService.RemoveAsync($"dashboard:summary:{userId}");
            await _cacheService.RemoveAsync($"holdings:list:{userId}");

            return transaction;
        }

        public async Task RecalculateHoldingStateAsync(Holding holding)
        {
            var transactions = await _dbContext.Transactions
                .Where(t => t.HoldingId == holding.Id)
                .OrderBy(t => t.TransactionDate)
                .ToListAsync();

            if (holding.AssetType.Equals("Stock", StringComparison.OrdinalIgnoreCase) ||
                holding.AssetType.Equals("ETF", StringComparison.OrdinalIgnoreCase) ||
                holding.AssetType.Equals("MutualFund", StringComparison.OrdinalIgnoreCase))
            {
                decimal totalQty = 0;
                decimal totalCost = 0;

                foreach (var tx in transactions)
                {
                    var qty = tx.QuantityOrUnits ?? 0;
                    var price = tx.PriceOrNAV ?? 0;

                    if (tx.TransactionType.Equals("Buy", StringComparison.OrdinalIgnoreCase) ||
                        tx.TransactionType.Equals("Deposit", StringComparison.OrdinalIgnoreCase))
                    {
                        totalQty += qty;
                        totalCost += qty * price;
                    }
                    else if (tx.TransactionType.Equals("Sell", StringComparison.OrdinalIgnoreCase) ||
                             tx.TransactionType.Equals("Withdrawal", StringComparison.OrdinalIgnoreCase))
                    {
                        // Weighted average buy price remains the same, but quantity reduces.
                        // If selling, we reduce quantity.
                        if (totalQty > 0)
                        {
                            var averageBuyPrice = totalCost / totalQty;
                            totalQty = Math.Max(0, totalQty - qty);
                            totalCost = totalQty * averageBuyPrice;
                        }
                    }
                }

                holding.QuantityOrUnits = totalQty;
                holding.BuyPriceOrNAV = totalQty > 0 ? Math.Round(totalCost / totalQty, 4) : 0;
            }
            else if (holding.AssetType.Equals("SavingsAccount", StringComparison.OrdinalIgnoreCase))
            {
                decimal balance = 0;

                foreach (var tx in transactions)
                {
                    if (tx.TransactionType.Equals("Deposit", StringComparison.OrdinalIgnoreCase) ||
                        tx.TransactionType.Equals("InterestCredit", StringComparison.OrdinalIgnoreCase) ||
                        tx.TransactionType.Equals("Dividend", StringComparison.OrdinalIgnoreCase))
                    {
                        balance += tx.Amount;
                    }
                    else if (tx.TransactionType.Equals("Withdrawal", StringComparison.OrdinalIgnoreCase) ||
                             tx.TransactionType.Equals("Sell", StringComparison.OrdinalIgnoreCase))
                    {
                        balance -= tx.Amount;
                    }
                }

                holding.Balance = balance;
            }
            else if (holding.AssetType.Equals("FixedDeposit", StringComparison.OrdinalIgnoreCase))
            {
                // For Fixed Deposits, principal is set on creation and doesn't change from daily transactions
                // unless interest is credited to the principal or there's a partial withdrawal.
                decimal principal = holding.Principal ?? 0;
                foreach (var tx in transactions)
                {
                    if (tx.TransactionType.Equals("Deposit", StringComparison.OrdinalIgnoreCase))
                    {
                        principal = tx.Amount;
                    }
                    else if (tx.TransactionType.Equals("Withdrawal", StringComparison.OrdinalIgnoreCase))
                    {
                        principal = Math.Max(0, principal - tx.Amount);
                    }
                }
                holding.Principal = principal;
            }

            holding.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
        }
    }
}
