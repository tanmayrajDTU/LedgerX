using System;
using System.Threading.Tasks;

namespace LedgerX.Core.Interfaces
{
    public interface IJobTracker
    {
        Task<Guid> CreateJobAsync(string jobType, string payload);
        Task<Guid> StartExecutionAsync(Guid jobId, string workerName, int retryCount);
        Task CompleteExecutionAsync(Guid executionId, Guid jobId);
        Task FailExecutionAsync(Guid executionId, Guid jobId, string errorMessage, bool moveToDeadLetter);
    }
}
