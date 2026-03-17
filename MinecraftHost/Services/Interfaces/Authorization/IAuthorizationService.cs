using MinecraftHost.Models.Authorization;

namespace MinecraftHost.Services.Interfaces.Authorization;

public interface IAuthorizationService
{
    AuthorizationDecision Authorize(OperationType operationType, string serverId = "");
}