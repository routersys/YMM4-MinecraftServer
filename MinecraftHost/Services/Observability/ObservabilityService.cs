using MinecraftHost.Models.Jobs;
using MinecraftHost.Models.Server;
using MinecraftHost.Services.Interfaces.Observability;
using System.Collections.Concurrent;

namespace MinecraftHost.Services.Observability;

public sealed class ObservabilityService : IObservabilityService
{
    private readonly ConcurrentQueue<JobRecord> _recentJobs = new();
    private const int Capacity = 1000;

    public void RecordJob(JobRecord jobRecord)
    {
        _recentJobs.Enqueue(Clone(jobRecord));
        while (_recentJobs.Count > Capacity)
            _recentJobs.TryDequeue(out _);
    }

    public ObservabilitySnapshot GetSnapshot()
    {
        var jobs = _recentJobs.ToArray();
        var total = jobs.LongLength;
        var succeeded = jobs.LongCount(j => j.Status == JobStatus.Succeeded);
        var failed = jobs.LongCount(j => j.Status == JobStatus.Failed);
        var running = jobs.LongCount(j => j.Status == JobStatus.Running);
        var avgDuration = jobs.Length == 0 ? 0 : jobs.Average(j => j.DurationMs);
        return new ObservabilitySnapshot(total, succeeded, failed, running, avgDuration, DateTime.UtcNow);
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
}