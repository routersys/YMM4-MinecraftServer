namespace MinecraftHost.Models.Logging;

public sealed record AuditLogEntry(
    DateTime TimestampUtc,
    string Category,
    string Action,
    string EntityId,
    string Property,
    string Before,
    string After,
    string CorrelationId,
    string Actor
);