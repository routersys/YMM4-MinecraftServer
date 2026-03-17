using MinecraftHost.Services.Interfaces.Authorization;

namespace MinecraftHost.Services.Authorization;

public sealed class AuthorizationStateNotifier : IAuthorizationStateNotifier
{
    public event Action? StateChanged;

    public void NotifyChanged()
    {
        StateChanged?.Invoke();
    }
}