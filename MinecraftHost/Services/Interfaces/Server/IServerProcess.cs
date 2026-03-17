using MinecraftHost.Models.Server;

namespace MinecraftHost.Services.Interfaces.Server;

public interface IServerProcess : IDisposable, IAsyncDisposable
{
    event EventHandler<string>? OutputReceived;
    event EventHandler<string>? ErrorReceived;
    event EventHandler? Exited;

    bool IsRunning { get; }

    void Start();
    Task SendCommandAsync(string command);
    void Stop();
    Task StopAsync();
    bool TryGetMetricsSnapshot(out ServerProcessMetricsSnapshot snapshot);
}