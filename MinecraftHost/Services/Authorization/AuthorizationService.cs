using MinecraftHost.Localization;
using MinecraftHost.Models.Authorization;
using MinecraftHost.Services.Interfaces.Authorization;

namespace MinecraftHost.Services.Authorization;

public sealed class AuthorizationService : IAuthorizationService
{
    private readonly IIdentityService _identityService;
    private readonly IPolicyService _policyService;

    public AuthorizationService(IIdentityService identityService, IPolicyService policyService)
    {
        _identityService = identityService;
        _policyService = policyService;
    }

    public AuthorizationDecision Authorize(OperationType operationType, string serverId = "")
    {
        var state = _policyService.Current;
        if (state.MaintenanceMode && operationType is not OperationType.StopServer)
            return AuthorizationDecision.Deny(Texts.Auth_Error_MaintenanceMode);

        if (!string.IsNullOrWhiteSpace(serverId) && state.LockedServerIds.Contains(serverId) && operationType is not OperationType.StopServer)
            return AuthorizationDecision.Deny(Texts.Auth_Error_ServerLocked);

        var requiredRole = operationType switch
        {
            OperationType.CreateServer => OperatorRole.Operator,
            OperationType.DeleteServer => OperatorRole.Administrator,
            OperationType.StartServer => OperatorRole.Operator,
            OperationType.StopServer => OperatorRole.Operator,
            OperationType.UpdateServerBuild => OperatorRole.Operator,
            OperationType.ManagePlugins => OperatorRole.Operator,
            OperationType.EditServerFiles => OperatorRole.Operator,
            OperationType.SendConsoleCommand => OperatorRole.Operator,
            OperationType.ViewAuditLog => OperatorRole.Operator,
            OperationType.ViewOperationsCenter => OperatorRole.Operator,
            OperationType.ManagePolicy => OperatorRole.Administrator,
            _ => OperatorRole.Administrator
        };

        if (_identityService.Role < requiredRole)
            return AuthorizationDecision.Deny(string.Format(Texts.Auth_Error_RoleInsufficientFormat, requiredRole));

        return AuthorizationDecision.Allow();
    }
}