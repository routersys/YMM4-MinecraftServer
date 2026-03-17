using MinecraftHost.Services.Interfaces.Server;

namespace MinecraftHost.Services.Interfaces.Observability;

public interface IServerMonitoringService
{
    IServerPerformanceMonitor CreateMonitor(IServerProcess process);
}