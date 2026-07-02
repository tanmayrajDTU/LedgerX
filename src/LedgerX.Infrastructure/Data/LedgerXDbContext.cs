using Microsoft.EntityFrameworkCore;
using LedgerX.Core.Entities;

namespace LedgerX.Infrastructure.Data
{
    public class LedgerXDbContext : DbContext
    {
        public LedgerXDbContext(DbContextOptions<LedgerXDbContext> options) : base(options)
        {
        }

        public DbSet<User> Users => Set<User>();
        public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
        public DbSet<Holding> Holdings => Set<Holding>();
        public DbSet<Transaction> Transactions => Set<Transaction>();
        public DbSet<PriceHistory> PriceHistory => Set<PriceHistory>();
        public DbSet<Job> Jobs => Set<Job>();
        public DbSet<JobExecution> JobExecutions => Set<JobExecution>();
        public DbSet<Alert> Alerts => Set<Alert>();
        public DbSet<AnalyticsSnapshot> AnalyticsSnapshots => Set<AnalyticsSnapshot>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // User configuration
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasIndex(u => u.Username).IsUnique();
                entity.HasIndex(u => u.Email).IsUnique();
            });

            // Holding configuration
            modelBuilder.Entity<Holding>(entity =>
            {
                entity.Property(h => h.QuantityOrUnits).HasPrecision(18, 4);
                entity.Property(h => h.BuyPriceOrNAV).HasPrecision(18, 4);
                entity.Property(h => h.Principal).HasPrecision(18, 2);
                entity.Property(h => h.InterestRate).HasPrecision(5, 2);
                entity.Property(h => h.Balance).HasPrecision(18, 2);
            });

            // Transaction configuration
            modelBuilder.Entity<Transaction>(entity =>
            {
                entity.Property(t => t.Amount).HasPrecision(18, 2);
                entity.Property(t => t.QuantityOrUnits).HasPrecision(18, 4);
                entity.Property(t => t.PriceOrNAV).HasPrecision(18, 4);
            });

            // PriceHistory configuration
            modelBuilder.Entity<PriceHistory>(entity =>
            {
                entity.HasIndex(p => new { p.SymbolOrName, p.PriceDate }).IsUnique();
                entity.Property(p => p.Price).HasPrecision(18, 4);
            });

            // Alert configuration
            modelBuilder.Entity<Alert>(entity =>
            {
                entity.Property(a => a.ThresholdValue).HasPrecision(18, 4);
                entity.Property(a => a.CurrentValue).HasPrecision(18, 4);
            });

            // AnalyticsSnapshot configuration
            modelBuilder.Entity<AnalyticsSnapshot>(entity =>
            {
                entity.Property(a => a.NetWorth).HasPrecision(18, 2);
                entity.Property(a => a.InvestedAmount).HasPrecision(18, 2);
                entity.Property(a => a.CurrentValue).HasPrecision(18, 2);
                entity.Property(a => a.TotalGainLoss).HasPrecision(18, 2);
                entity.Property(a => a.Cagr).HasPrecision(5, 4);
                entity.Property(a => a.AbsoluteReturn).HasPrecision(5, 4);
                entity.Property(a => a.Volatility).HasPrecision(5, 4);
                entity.Property(a => a.ConcentrationRisk).HasPrecision(5, 4);
                entity.Property(a => a.LargestHoldingPercentage).HasPrecision(5, 4);
                entity.Property(a => a.DiversificationScore).HasPrecision(5, 2);
            });

            // Job & Execution configuration
            modelBuilder.Entity<JobExecution>()
                .HasOne(je => je.Job)
                .WithMany(j => j.JobExecutions)
                .HasForeignKey(je => je.JobId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
