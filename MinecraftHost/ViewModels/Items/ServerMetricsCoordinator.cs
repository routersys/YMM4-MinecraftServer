using MinecraftHost.Models.Server;
using MinecraftHost.Services.Interfaces.Observability;
using MinecraftHost.Services.Interfaces.Server;
using System.Windows;
using System.Windows.Threading;

namespace MinecraftHost.ViewModels.Items;

public sealed class ServerMetricsCoordinator : IDisposable
{
    private readonly Func<IServerProcess?> _processAccessor;
    private readonly Func<IServerPerformanceMonitor?> _monitorAccessor;
    private readonly Action<string, string> _metricsSink;
    private DispatcherTimer? _refreshTimer;
    private ServerProcessMetricsSnapshot? _lastMetricsSnapshot;

    public ServerMetricsCoordinator(
        Func<IServerProcess?> processAccessor,
        Func<IServerPerformanceMonitor?> monitorAccessor,
        Action<string, string> metricsSink)
    {
        _processAccessor = processAccessor;
        _monitorAccessor = monitorAccessor;
        _metricsSink = metricsSink;
    }

    public void Start()
    {
        Stop();
        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background, Application.Current.Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _refreshTimer.Tick += OnRefreshTimerTick;
        _refreshTimer.Start();
        _lastMetricsSnapshot = null;
    }

    public void Stop()
    {
        if (_refreshTimer is not null)
        {
            _refreshTimer.Stop();
            _refreshTimer.Tick -= OnRefreshTimerTick;
            _refreshTimer = null;
        }

        _lastMetricsSnapshot = null;
    }

    public void Refresh()
    {
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(Refresh));
            return;
        }

        var process = _processAccessor();
        if (process is not null && process.TryGetMetricsSnapshot(out var snapshot))
        {
            var cpu = 0d;
            if (_lastMetricsSnapshot is { } last)
            {
                var elapsedMs = (snapshot.TimestampUtc - last.TimestampUtc).TotalMilliseconds;
                if (elapsedMs > 0)
                {
                    var cpuUsedMs = (snapshot.TotalProcessorTime - last.TotalProcessorTime).TotalMilliseconds;
                    cpu = Math.Clamp((cpuUsedMs / (Environment.ProcessorCount * elapsedMs)) * 100.0, 0, 100);
                }
            }

            _lastMetricsSnapshot = snapshot;
            _metricsSink($"{cpu:0.0}%", $"{snapshot.WorkingSetBytes / (1024 * 1024)} MB");
            return;
        }

        var monitor = _monitorAccessor();
        if (monitor is not null)
        {
            _metricsSink($"{monitor.CpuUsagePercent:0.0}%", $"{monitor.MemoryUsageMB} MB");
            return;
        }

        _metricsSink("0.0%", "0 MB");
    }

    public void Dispose()
    {
        Stop();
    }

    private void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        Refresh();
    }
}