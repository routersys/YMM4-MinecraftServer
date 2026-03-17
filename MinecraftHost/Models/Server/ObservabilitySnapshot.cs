namespace MinecraftHost.Models.Server;

public readonly record struct ObservabilitySnapshot(
    long TotalJobs,
    long SucceededJobs,
    long FailedJobs,
    long RunningJobs,
    double AverageJobDurationMs,
    DateTime TimestampUtc
);