using MinecraftHost.Models.Jobs;

namespace MinecraftHost.Services.Interfaces.Jobs;

public interface IJobOrchestratorService
{
    Task<T> ExecuteAsync<T>(string name, string serverId, Func<CancellationToken, Task<T>> work, int maxAttempts = 3, CancellationToken cancellationToken = default);
    Task ExecuteAsync(string name, string serverId, Func<CancellationToken, Task> work, int maxAttempts = 3, CancellationToken cancellationToken = default);
    IReadOnlyList<JobRecord> GetRecentJobs(int maxCount);
}