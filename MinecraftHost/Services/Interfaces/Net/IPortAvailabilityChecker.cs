using MinecraftHost.Models.Server;

namespace MinecraftHost.Services.Interfaces.Net;

public interface IPortAvailabilityChecker
{
    Task<bool> IsAvailableAsync(string ipAddress, int port, ServerType type);
}