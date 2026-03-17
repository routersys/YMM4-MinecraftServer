namespace MinecraftHost.Services.Interfaces.Net;

public interface IGlobalIpResolver
{
    Task<(string? Ipv4, string? Ipv6)> ResolveAsync();
}