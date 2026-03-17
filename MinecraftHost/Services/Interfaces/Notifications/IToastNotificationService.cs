namespace MinecraftHost.Services.Interfaces.Notifications;

public interface IToastNotificationService
{
    void ShowServerStartedToast(string? ipv4Address, string? ipv6Address, int port);
}