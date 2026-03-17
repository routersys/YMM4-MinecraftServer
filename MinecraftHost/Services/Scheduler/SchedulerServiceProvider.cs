using MinecraftHost.Services.Interfaces.Scheduler;
using MinecraftHost.Services.Jobs;
using MinecraftHost.Services.Logging;
using MinecraftHost.Services.Server;

namespace MinecraftHost.Services.Scheduler;

public static class SchedulerServiceProvider
{
    private static ISchedulerService? _instance;

    public static ISchedulerService Instance
    {
        get => _instance ??= new SchedulerService(
            new ServerManager(),
            JobOrchestratorServiceProvider.Instance,
            StructuredLogServiceProvider.Instance);
    }
}