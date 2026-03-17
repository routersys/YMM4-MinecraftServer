namespace MinecraftHost.Services.Interfaces.Authorization;

public interface IAuthorizationStateNotifier
{
    event Action? StateChanged;
    void NotifyChanged();
}