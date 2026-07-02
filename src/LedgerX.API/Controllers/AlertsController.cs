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

namespace LedgerX.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class AlertsController : ControllerBase
    {
        private readonly LedgerXDbContext _dbContext;
        private readonly ICacheService _cacheService;

        public AlertsController(LedgerXDbContext dbContext, ICacheService cacheService)
        {
            _dbContext = dbContext;
            _cacheService = cacheService;
        }

        public class CreateAlertDto
        {
            public Guid? HoldingId { get; set; }
            public string AlertType { get; set; } = string.Empty; // Price, AllocationDrift, FDMaturity, PortfolioConcentration
            public decimal ThresholdValue { get; set; }
            public string Message { get; set; } = string.Empty;
        }

        [HttpGet]
        public async Task<IActionResult> GetAlerts()
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
            
            var alerts = await _dbContext.Alerts
                .Include(a => a.Holding)
                .Where(a => a.UserId == userId)
                .OrderByDescending(a => a.CreatedAt)
                .Select(a => new
                {
                    a.Id,
                    a.HoldingId,
                    HoldingName = a.Holding != null ? a.Holding.SymbolOrName : "Portfolio-wide",
                    a.AlertType,
                    a.ThresholdValue,
                    a.CurrentValue,
                    a.Message,
                    a.IsTriggered,
                    a.TriggeredAt,
                    a.CreatedAt
                })
                .ToListAsync();

            return Ok(alerts);
        }

        [HttpPost]
        public async Task<IActionResult> CreateAlert([FromBody] CreateAlertDto dto)
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);

            if (string.IsNullOrWhiteSpace(dto.AlertType) || string.IsNullOrWhiteSpace(dto.Message))
            {
                return BadRequest(new { Message = "AlertType and Message are required." });
            }

            var alert = new Alert
            {
                UserId = userId,
                HoldingId = dto.HoldingId,
                AlertType = dto.AlertType,
                ThresholdValue = dto.ThresholdValue,
                CurrentValue = 0,
                Message = dto.Message,
                IsTriggered = false
            };

            _dbContext.Alerts.Add(alert);
            await _dbContext.SaveChangesAsync();

            // Invalidate Redis dashboard cache
            await _cacheService.RemoveAsync($"dashboard:summary:{userId}");

            return CreatedAtAction(nameof(GetAlerts), new { id = alert.Id }, alert);
        }

        [HttpPut("{id}/dismiss")]
        public async Task<IActionResult> DismissAlert(Guid id)
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);

            var alert = await _dbContext.Alerts.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId);
            if (alert == null)
            {
                return NotFound(new { Message = "Alert not found." });
            }

            alert.IsTriggered = false;
            alert.TriggeredAt = null;
            await _dbContext.SaveChangesAsync();

            // Invalidate Redis cache
            await _cacheService.RemoveAsync($"dashboard:summary:{userId}");

            return Ok(new { Message = "Alert dismissed successfully." });
        }
    }
}
