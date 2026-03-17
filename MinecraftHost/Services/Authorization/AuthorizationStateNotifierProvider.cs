using MinecraftHost.Services.Interfaces.Authorization;

namespace MinecraftHost.Services.Authorization;

public static class AuthorizationStateNotifierProvider
{
    private static readonly Lazy<IAuthorizationStateNotifier> Factory = new(() => new AuthorizationStateNotifier());

    public static IAuthorizationStateNotifier Instance => Factory.Value;
}