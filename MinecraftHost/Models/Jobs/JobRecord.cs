namespace MinecraftHost.Models.Jobs;

public sealed class JobRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = string.Empty;
    public string ServerId { get; init; } = string.Empty;
    public JobStatus Status { get; set; } = JobStatus.Pending;
    public int Attempt { get; set; }
    public int MaxAttempts { get; init; } = 1;
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
    public DateTime? StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public string LastError { get; set; } = string.Empty;
    public long DurationMs { get; set; }
}