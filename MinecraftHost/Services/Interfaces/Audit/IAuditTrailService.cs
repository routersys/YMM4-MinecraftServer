using MinecraftHost.Models.Logging;

namespace MinecraftHost.Services.Interfaces.Audit;

public interface IAuditTrailService
{
    Task RecordChangeAsync(string category, string action, string entityId, string property, string before, string after, string actor = "local-user", string correlationId = "");
    Task RecordEventAsync(string category, string action, string entityId, string message, string actor = "local-user", string correlationId = "");
    Task<IReadOnlyList<AuditLogEntry>> GetRecentAsync(int maxCount, CancellationToken cancellationToken = default);
}