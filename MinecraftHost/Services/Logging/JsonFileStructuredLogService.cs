using MinecraftHost.Models.Logging;
using MinecraftHost.Services.Interfaces.Logging;
using MinecraftHost.Settings.Configuration;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace MinecraftHost.Services.Logging;

public sealed class JsonFileStructuredLogService : IStructuredLogService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly object _syncRoot = new();

    public void Log(StructuredLogLevel level, string category, string message, string operation = "", string serverId = "", Exception? exception = null, string correlationId = "")
    {
        var entry = new StructuredLogEntry
        {
            TimestampUtc = DateTime.UtcNow,
            Level = level,
            Category = category,
            Message = message,
            Operation = operation,
            ServerId = serverId,
            ProcessId = Environment.ProcessId,
            CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString("N") : correlationId,
            Exception = exception?.ToString() ?? string.Empty
        };

        Append(entry);
    }

    public Task<IReadOnlyList<StructuredLogEntry>> GetRecentEntriesAsync(int maxCount, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedCount = Math.Clamp(maxCount, 100, 10000);
        var result = new List<StructuredLogEntry>(normalizedCount);

        lock (_syncRoot)
        {
            var directory = EnsureLogsDirectory();
            var files = Directory.GetFiles(directory, "structured-*.jsonl").OrderByDescending(x => x, StringComparer.OrdinalIgnoreCase).ToArray();

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var lines = File.ReadAllLines(file);
                for (var i = lines.Length - 1; i >= 0; i--)
                {
                    var line = lines[i];
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    try
                    {
                        var entry = JsonSerializer.Deserialize<StructuredLogEntry>(line, JsonOptions);
                        if (entry is not null)
                            result.Add(entry);
                    }
                    catch
                    {
                    }

                    if (result.Count >= normalizedCount)
                        return Task.FromResult<IReadOnlyList<StructuredLogEntry>>(result);
                }
            }

            return Task.FromResult<IReadOnlyList<StructuredLogEntry>>(result);
        }
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            var directory = EnsureLogsDirectory();
            foreach (var file in Directory.GetFiles(directory, "structured-*.jsonl"))
                File.Delete(file);
        }

        return Task.CompletedTask;
    }

    public string GetLogsDirectory()
    {
        return EnsureLogsDirectory();
    }

    private void Append(StructuredLogEntry entry)
    {
        lock (_syncRoot)
        {
            var directory = EnsureLogsDirectory();
            var targetPath = ResolveActiveFilePath(directory);
            var line = JsonSerializer.Serialize(entry, JsonOptions);
            File.AppendAllText(targetPath, line + Environment.NewLine, Encoding.UTF8);
            ApplyRetention(directory);
        }
    }

    private static string EnsureLogsDirectory()
    {
        var settings = MinecraftHostSettings.Default;
        var basePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppDomain.CurrentDomain.BaseDirectory;
        var configured = settings.StructuredLogsDirectory;
        var fullPath = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(basePath, "logs")
            : Path.GetFullPath(configured);

        Directory.CreateDirectory(fullPath);
        return fullPath;
    }

    private static string ResolveActiveFilePath(string directory)
    {
        var settings = MinecraftHostSettings.Default;
        var maxBytes = Math.Max(1, settings.StructuredLogMaxFileSizeMB) * 1024L * 1024L;
        var baseName = $"structured-{DateTime.UtcNow:yyyyMMdd}";
        var path = Path.Combine(directory, $"{baseName}.jsonl");
        if (!File.Exists(path))
            return path;

        var length = new FileInfo(path).Length;
        if (length < maxBytes)
            return path;

        return Path.Combine(directory, $"{baseName}-{DateTime.UtcNow:HHmmss}.jsonl");
    }

    private static void ApplyRetention(string directory)
    {
        var maxFiles = Math.Clamp(MinecraftHostSettings.Default.StructuredLogMaxFiles, 1, 365);
        var files = Directory.GetFiles(directory, "structured-*.jsonl")
            .OrderByDescending(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        for (var i = maxFiles; i < files.Length; i++)
            File.Delete(files[i]);
    }
}