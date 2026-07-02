using System;
using System.Collections.Generic;

namespace LedgerX.Core.Entities
{
    public class User
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string Role { get; set; } = "User"; // "User" or "Admin"
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigational properties
        public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
        public ICollection<Holding> Holdings { get; set; } = new List<Holding>();
        public ICollection<Alert> Alerts { get; set; } = new List<Alert>();
        public ICollection<AnalyticsSnapshot> AnalyticsSnapshots { get; set; } = new List<AnalyticsSnapshot>();
    }
}
