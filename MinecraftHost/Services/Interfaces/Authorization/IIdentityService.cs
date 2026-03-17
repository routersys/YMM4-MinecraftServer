using MinecraftHost.Models.Authorization;

namespace MinecraftHost.Services.Interfaces.Authorization;

public interface IIdentityService
{
    string UserName { get; }
    OperatorRole Role { get; }
}