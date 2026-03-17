using MinecraftHost.Models.Server;
using MinecraftHost.Services.Interfaces.Observability;
using MinecraftHost.Services.Interfaces.Server;

namespace MinecraftHost.Services.Observability;

public sealed class MinecraftServerPerformanceMonitor : IServerPerformanceMonitor
{
    private readonly IServerProcess _process;
    private readonly Timer? _timer;
    private TimeSpan _lastProcessorTime;
    private DateTime _lastMonitorTime;

    public double CpuUsagePercent { get; private set; }
    public long MemoryUsageMB { get; private set; }

    public event EventHandler? PerformanceUpdated;

    public MinecraftServerPerformanceMonitor(IServerProcess process)
    {
        _process = process;

        try
        {
            if (_process.TryGetMetricsSnapshot(out var snapshot))
            {
                _lastProcessorTime = snapshot.TotalProcessorTime;
                _lastMonitorTime = snapshot.TimestampUtc;
                _timer = new Timer(UpdatePerformance, null, 1000, 1000);
            }
        }
        catch
        {
        }
    }

    private void UpdatePerformance(object? state)
    {
        try
        {
            if (!_process.TryGetMetricsSnapshot(out ServerProcessMetricsSnapshot snapshot))
            {
                _timer?.Change(Timeout.Infinite, Timeout.Infinite);
                return;
            }

            var currentTime = snapshot.TimestampUtc;
            var currentProcessorTime = snapshot.TotalProcessorTime;

            var cpuUsedMs = (currentProcessorTime - _lastProcessorTime).TotalMilliseconds;
            var totalMsPassed = (currentTime - _lastMonitorTime).TotalMilliseconds;
            if (totalMsPassed <= 0)
                return;

            var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);

            CpuUsagePercent = Math.Clamp(cpuUsageTotal * 100, 0, 100);
            MemoryUsageMB = snapshot.WorkingSetBytes / (1024 * 1024);

            _lastProcessorTime = currentProcessorTime;
            _lastMonitorTime = currentTime;

            PerformanceUpdated?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}