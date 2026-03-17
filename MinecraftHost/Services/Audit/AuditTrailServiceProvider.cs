using MinecraftHost.Services.Interfaces.Audit;

namespace MinecraftHost.Services;

public static class AuditTrailServiceProvider
{
    private static readonly Lazy<IAuditTrailService> InstanceFactory = new(() => new JsonFileAuditTrailService());

    public static IAuditTrailService Instance => InstanceFactory.Value;
}