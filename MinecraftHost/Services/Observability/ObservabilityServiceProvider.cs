using MinecraftHost.Services.Interfaces.Observability;

namespace MinecraftHost.Services.Observability;

public static class ObservabilityServiceProvider
{
    private static readonly Lazy<IObservabilityService> Factory = new(() => new ObservabilityService());

    public static IObservabilityService Instance => Factory.Value;
}