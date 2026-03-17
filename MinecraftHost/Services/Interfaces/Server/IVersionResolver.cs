using MinecraftHost.Models.Server;

namespace MinecraftHost.Services.Interfaces.Server;

public interface IVersionResolver
{
    Task<IReadOnlyList<string>> GetAvailableVersionsAsync(ServerType serverType);
    Task<string?> GetLatestVersionAsync(ServerType serverType);
    Task<string?> GetLatestBuildIdentifierAsync(ServerType serverType, string version);
    Task<IReadOnlyList<string>> GetAvailableBuildIdentifiersAsync(ServerType serverType, string version);
}