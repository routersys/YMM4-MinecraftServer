using MinecraftHost.Services.Interfaces.Jobs;
using MinecraftHost.Services.Logging;
using MinecraftHost.Services.Observability;

namespace MinecraftHost.Services.Jobs;

public static class JobOrchestratorServiceProvider
{
    private static readonly Lazy<IJobOrchestratorService> Factory = new(() => new JobOrchestratorService(ObservabilityServiceProvider.Instance, StructuredLogServiceProvider.Instance));

    public static IJobOrchestratorService Instance => Factory.Value;
}