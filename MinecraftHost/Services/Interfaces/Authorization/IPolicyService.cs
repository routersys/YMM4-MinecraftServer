using MinecraftHost.Models.Authorization;

namespace MinecraftHost.Services.Interfaces.Authorization;

public interface IPolicyService
{
    PolicyState Current { get; }
    Task SaveAsync(PolicyState state, CancellationToken cancellationToken = default);
}