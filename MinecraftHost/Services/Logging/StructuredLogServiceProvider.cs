using MinecraftHost.Services.Interfaces.Logging;

namespace MinecraftHost.Services.Logging;

public static class StructuredLogServiceProvider
{
    private static readonly Lazy<IStructuredLogService> LazyInstance = new(() => new JsonFileStructuredLogService());

    public static IStructuredLogService Instance => LazyInstance.Value;
}