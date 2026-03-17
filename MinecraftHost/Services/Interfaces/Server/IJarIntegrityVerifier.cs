using MinecraftHost.Models.Server;

namespace MinecraftHost.Services.Interfaces.Server;

public interface IJarIntegrityVerifier
{
    Task VerifyAsync(ServerType serverType, string version, string downloadUrl, string jarPath, CancellationToken cancellationToken = default);
}