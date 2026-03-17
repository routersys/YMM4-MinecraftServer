namespace MinecraftHost.Models.Server;

public readonly record struct ServerProcessMetricsSnapshot(TimeSpan TotalProcessorTime, long WorkingSetBytes, DateTime TimestampUtc);