using MinecraftHost.Services.Interfaces.Authorization;

namespace MinecraftHost.Services.Authorization;

public static class AuthorizationServiceProvider
{
    private static readonly Lazy<IAuthorizationService> Factory = new(() => new AuthorizationService(new IdentityService(), PolicyServiceProvider.Instance));

    public static IAuthorizationService Instance => Factory.Value;
}