using MinecraftHost.Services.Interfaces.Authorization;

namespace MinecraftHost.Services.Authorization;

public static class PolicyServiceProvider
{
    private static readonly Lazy<IPolicyService> Factory = new(() => new PolicyService());

    public static IPolicyService Instance => Factory.Value;
}