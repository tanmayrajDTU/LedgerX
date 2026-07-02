using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using LedgerX.Core.Entities;
using LedgerX.Core.Interfaces;
using LedgerX.Infrastructure.Data;

namespace LedgerX.Infrastructure.Services
{
    public class JobTracker : IJobTracker
    {
        private readonly LedgerXDbContext _dbContext;
        private readonly ILogger<JobTracker> _logger;

        public JobTracker(LedgerXDbContext dbContext, ILogger<JobTracker> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        public async Task<Guid> CreateJobAsync(string jobType, string payload)
        {
            var job = new Job
            {
                JobType = jobType,
                Payload = payload,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _dbContext.Jobs.Add(job);
            await _dbContext.SaveChangesAsync();
            return job.Id;
        }

        public async Task<Guid> StartExecutionAsync(Guid jobId, string workerName, int retryCount)
        {
            var job = await _dbContext.Jobs.FindAsync(jobId);
            if (job != null)
            {
                job.Status = "Processing";
                job.UpdatedAt = DateTime.UtcNow;
            }

            var execution = new JobExecution
            {
                JobId = jobId,
                WorkerName = workerName,
                StartedAt = DateTime.UtcNow,
                RetryCount = retryCount
            };

            _dbContext.JobExecutions.Add(execution);
            await _dbContext.SaveChangesAsync();
            return execution.Id;
        }

        public async Task CompleteExecutionAsync(Guid executionId, Guid jobId)
        {
            var execution = await _dbContext.JobExecutions.FindAsync(executionId);
            if (execution != null)
            {
                execution.CompletedAt = DateTime.UtcNow;
                execution.DurationMs = (long)(execution.CompletedAt.Value - execution.StartedAt).TotalMilliseconds;
            }

            var job = await _dbContext.Jobs.FindAsync(jobId);
            if (job != null)
            {
                job.Status = "Completed";
                job.UpdatedAt = DateTime.UtcNow;
            }

            await _dbContext.SaveChangesAsync();
        }

        public async Task FailExecutionAsync(Guid executionId, Guid jobId, string errorMessage, bool moveToDeadLetter)
        {
            var execution = await _dbContext.JobExecutions.FindAsync(executionId);
            if (execution != null)
            {
                execution.CompletedAt = DateTime.UtcNow;
                execution.DurationMs = (long)(execution.CompletedAt.Value - execution.StartedAt).TotalMilliseconds;
                execution.ErrorMessage = errorMessage;
            }

            var job = await _dbContext.Jobs.FindAsync(jobId);
            if (job != null)
            {
                job.Status = moveToDeadLetter ? "DeadLetter" : "Failed";
                job.UpdatedAt = DateTime.UtcNow;
            }

            await _dbContext.SaveChangesAsync();
        }
    }
}
