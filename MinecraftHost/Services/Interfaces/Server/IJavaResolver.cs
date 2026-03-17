using MinecraftHost.Models.Server;

namespace MinecraftHost.Services.Interfaces.Server;

public interface IJavaResolver
{
    Task<string> ResolveJavaAsync(MinecraftServerConfig? config = null);
}