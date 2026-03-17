using MinecraftHost.Models.Logging;
using MinecraftHost.Services.Interfaces.Audit;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace MinecraftHost.Services;

public sealed class JsonFileAuditTrailService : IAuditTrailService
{
    private readonly string _auditDirectory;
    private readonly string _auditFilePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public JsonFileAuditTrailService()
    {
        var basePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppDomain.CurrentDomain.BaseDirectory;
        _auditDirectory = Path.Combine(basePath, "audit");
        Directory.CreateDirectory(_auditDirectory);
        _auditFilePath = Path.Combine(_auditDirectory, "audit.log");
    }

    public async Task RecordChangeAsync(string category, string action, string entityId, string property, string before, string after, string actor = "local-user", string correlationId = "")
    {
        var entry = new AuditLogEntry(DateTime.UtcNow, category, action, entityId, property, before, after, correlationId, actor);
        await AppendAsync(entry);
    }

    public async Task RecordEventAsync(string category, string action, string entityId, string message, string actor = "local-user", string correlationId = "")
    {
        var entry = new AuditLogEntry(DateTime.UtcNow, category, action, entityId, string.Empty, message, string.Empty, correlationId, actor);
        await AppendAsync(entry);
    }

    public async Task<IReadOnlyList<AuditLogEntry>> GetRecentAsync(int maxCount, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_auditFilePath) || maxCount <= 0)
            return [];

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var queue = new Queue<AuditLogEntry>(maxCount);
            await using var stream = new FileStream(_auditFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null)
                    break;

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var entry = JsonSerializer.Deserialize<AuditLogEntry>(line, SerializerOptions);
                if (entry is null)
                    continue;

                if (queue.Count == maxCount)
                    queue.Dequeue();
                queue.Enqueue(entry);
            }

            return queue.ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task AppendAsync(AuditLogEntry entry)
    {
        var line = JsonSerializer.Serialize(entry, SerializerOptions);

        await _gate.WaitAsync();
        try
        {
            await using var stream = new FileStream(_auditFilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
            await using var writer = new StreamWriter(stream, Encoding.UTF8);
            await writer.WriteLineAsync(line);
            await writer.FlushAsync();
        }
        finally
        {
            _gate.Release();
        }
    }
}