using MinecraftHost.Models.Logging;

namespace MinecraftHost.Services.Interfaces.Logging;

public interface IStructuredLogService
{
    void Log(StructuredLogLevel level, string category, string message, string operation = "", string serverId = "", Exception? exception = null, string correlationId = "");
    Task<IReadOnlyList<StructuredLogEntry>> GetRecentEntriesAsync(int maxCount, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
    string GetLogsDirectory();
}