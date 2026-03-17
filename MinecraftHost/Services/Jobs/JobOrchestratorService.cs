using MinecraftHost.Localization;
using MinecraftHost.Models.Jobs;
using MinecraftHost.Models.Logging;
using MinecraftHost.Services.Authorization;
using MinecraftHost.Services.Interfaces.Jobs;
using MinecraftHost.Services.Interfaces.Logging;
using MinecraftHost.Services.Interfaces.Observability;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Sockets;

namespace MinecraftHost.Services.Jobs;

public sealed class JobOrchestratorService : IJobOrchestratorService
{
    private readonly ConcurrentQueue<Guid> _jobOrder = new();
    private readonly ConcurrentDictionary<Guid, JobRecord> _jobs = new();
    private readonly IObservabilityService _observabilityService;
    private readonly IStructuredLogService _structuredLogService;
    private const int Capacity = 2000;

    public JobOrchestratorService(IObservabilityService observabilityService, IStructuredLogService structuredLogService)
    {
        _observabilityService = observabilityService;
        _structuredLogService = structuredLogService;
    }

    public async Task<T> ExecuteAsync<T>(string name, string serverId, Func<CancellationToken, Task<T>> work, int maxAttempts = 3, CancellationToken cancellationToken = default)
    {
        var job = new JobRecord
        {
            Name = name,
            ServerId = serverId,
            MaxAttempts = Math.Max(1, maxAttempts)
        };

        Enqueue(job);

        Exception? lastError = null;
        for (var attempt = 1; attempt <= job.MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            job.Attempt = attempt;
            job.Status = JobStatus.Running;
            job.StartedUtc = DateTime.UtcNow;
            var sw = Stopwatch.StartNew();

            try
            {
                var result = await work(cancellationToken);
                sw.Stop();
                job.DurationMs = sw.ElapsedMilliseconds;
                job.Status = JobStatus.Succeeded;
                job.CompletedUtc = DateTime.UtcNow;
                job.LastError = string.Empty;
                _observabilityService.RecordJob(job);
                _structuredLogService.Log(StructuredLogLevel.Information, nameof(JobOrchestratorService), string.Format(Texts.JobOrchestrator_LogJobSuccessFormat, name, attempt), "JobSuccess", serverId);
                return result;
            }
            catch (Exception ex)
            {
                sw.Stop();
                job.DurationMs = sw.ElapsedMilliseconds;
                job.Status = JobStatus.Failed;
                job.CompletedUtc = DateTime.UtcNow;
                job.LastError = ex.Message;
                lastError = ex;
                _observabilityService.RecordJob(job);
                _structuredLogService.Log(StructuredLogLevel.Warning, nameof(JobOrchestratorService), string.Format(Texts.JobOrchestrator_LogJobFailedFormat, name, attempt, ex.Message), "JobRetry", serverId, ex);

                if (attempt < job.MaxAttempts && ShouldRetry(ex))
                    await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
                else if (attempt < job.MaxAttempts)
                    break;
            }
        }

        throw BoundaryExceptionPolicy.Wrap($"Job:{name}", lastError ?? new InvalidOperationException("Unknown job failure"));
    }

    public Task ExecuteAsync(string name, string serverId, Func<CancellationToken, Task> work, int maxAttempts = 3, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(name, serverId, async ct =>
        {
            await work(ct);
            return true;
        }, maxAttempts, cancellationToken);
    }

    public IReadOnlyList<JobRecord> GetRecentJobs(int maxCount)
    {
        if (maxCount <= 0)
            return [];

        var data = _jobOrder
            .ToArray()
            .Select(id => _jobs.TryGetValue(id, out var job) ? job : null)
            .Where(job => job is not null)
            .Cast<JobRecord>();

        return data
            .OrderByDescending(j => j.CreatedUtc)
            .Take(maxCount)
            .Select(Clone)
            .ToArray();
    }

    private void Enqueue(JobRecord job)
    {
        _jobs[job.Id] = job;
        _jobOrder.Enqueue(job.Id);
        while (_jobOrder.Count > Capacity && _jobOrder.TryDequeue(out var removeId))
            _jobs.TryRemove(removeId, out _);
    }

    private static JobRecord Clone(JobRecord job)
    {
        return new JobRecord
        {
            Id = job.Id,
            Name = job.Name,
            ServerId = job.ServerId,
            Status = job.Status,
            Attempt = job.Attempt,
            MaxAttempts = job.MaxAttempts,
            CreatedUtc = job.CreatedUtc,
            StartedUtc = job.StartedUtc,
            CompletedUtc = job.CompletedUtc,
            LastError = job.LastError,
            DurationMs = job.DurationMs
        };
    }

    private static bool ShouldRetry(Exception exception)
    {
        if (exception is BoundaryOperationException boundary)
            return boundary.IsTransient;

        return exception is HttpRequestException
            or TaskCanceledException
            or TimeoutException
            or SocketException;
    }
}