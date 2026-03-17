using MinecraftHost.Models.Server;

namespace MinecraftHost.Services.Interfaces.Server;

public interface IServerManager
{
    Task<string> EnsureServerJarAsync(MinecraftServerConfig config, string? javaPath = null, IProgress<double>? progress = null);
    string GetServerDirectory(MinecraftServerConfig config);
    Task WriteServerPropertiesAsync(MinecraftServerConfig config);
}