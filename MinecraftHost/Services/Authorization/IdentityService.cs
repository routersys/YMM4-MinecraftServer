using MinecraftHost.Models.Authorization;
using MinecraftHost.Services.Interfaces.Authorization;
using MinecraftHost.Settings.Configuration;

namespace MinecraftHost.Services.Authorization;

public sealed class IdentityService : IIdentityService
{
    public string UserName { get; } = Environment.UserName;

    public OperatorRole Role
    {
        get
        {
            var raw = MinecraftHostSettings.Default.OperatorRole;
            return Enum.TryParse<OperatorRole>(raw, ignoreCase: true, out var role)
                ? role
                : OperatorRole.Administrator;
        }
    }
}