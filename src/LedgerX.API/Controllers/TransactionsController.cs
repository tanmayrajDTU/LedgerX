using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LedgerX.Infrastructure.Data;
using LedgerX.Infrastructure.Services;

namespace LedgerX.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class TransactionsController : ControllerBase
    {
        private readonly LedgerXDbContext _dbContext;
        private readonly TransactionService _transactionService;

        public TransactionsController(LedgerXDbContext dbContext, TransactionService transactionService)
        {
            _dbContext = dbContext;
            _transactionService = transactionService;
        }

        public class TransactionDto
        {
            public Guid HoldingId { get; set; }
            public string TransactionType { get; set; } = string.Empty; // Buy, Sell, Dividend, InterestCredit, Deposit, Withdrawal
            public decimal Amount { get; set; }
            public decimal? QuantityOrUnits { get; set; }
            public decimal? PriceOrNAV { get; set; }
            public DateTime? TransactionDate { get; set; }
        }

        [HttpGet]
        public async Task<IActionResult> GetTransactions()
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
            
            var transactions = await _dbContext.Transactions
                .Include(t => t.Holding)
                .Where(t => t.UserId == userId)
                .OrderByDescending(t => t.TransactionDate)
                .Select(t => new
                {
                    t.Id,
                    t.HoldingId,
                    HoldingName = t.Holding != null ? t.Holding.SymbolOrName : "N/A",
                    HoldingAssetType = t.Holding != null ? t.Holding.AssetType : "N/A",
                    t.TransactionType,
                    t.Amount,
                    t.QuantityOrUnits,
                    t.PriceOrNAV,
                    t.TransactionDate,
                    t.CreatedAt
                })
                .ToListAsync();

            return Ok(transactions);
        }

        [HttpPost]
        public async Task<IActionResult> CreateTransaction([FromBody] TransactionDto dto)
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);

            if (dto.HoldingId == Guid.Empty || string.IsNullOrWhiteSpace(dto.TransactionType) || dto.Amount < 0)
            {
                return BadRequest(new { Message = "HoldingId, valid TransactionType, and positive Amount are required." });
            }

            try
            {
                var transaction = await _transactionService.ProcessTransactionAsync(
                    userId,
                    dto.HoldingId,
                    dto.TransactionType,
                    dto.Amount,
                    dto.QuantityOrUnits,
                    dto.PriceOrNAV,
                    dto.TransactionDate
                );

                return Ok(transaction);
            }
            catch (ArgumentException ex)
            {
                return NotFound(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Message = "An error occurred while logging the transaction.", Details = ex.Message });
            }
        }
    }
}
