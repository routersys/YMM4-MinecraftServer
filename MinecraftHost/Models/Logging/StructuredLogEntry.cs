namespace MinecraftHost.Models.Logging;

public sealed class StructuredLogEntry
{
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    public StructuredLogLevel Level { get; init; } = StructuredLogLevel.Information;
    public string Category { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Operation { get; init; } = string.Empty;
    public string ServerId { get; init; } = string.Empty;
    public int ProcessId { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
    public string Exception { get; init; } = string.Empty;
}