using MinecraftHost.Services.Interfaces.Observability;
using MinecraftHost.Services.Interfaces.Server;

namespace MinecraftHost.Services.Observability;

public sealed class ServerMonitoringService : IServerMonitoringService
{
    public IServerPerformanceMonitor CreateMonitor(IServerProcess process)
    {
        return new MinecraftServerPerformanceMonitor(process);
    }
}