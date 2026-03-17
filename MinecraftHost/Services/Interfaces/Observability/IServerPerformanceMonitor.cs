namespace MinecraftHost.Services.Interfaces.Observability;

public interface IServerPerformanceMonitor : IDisposable
{
    double CpuUsagePercent { get; }
    long MemoryUsageMB { get; }

    event EventHandler? PerformanceUpdated;
}