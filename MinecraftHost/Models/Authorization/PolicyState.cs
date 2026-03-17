namespace MinecraftHost.Models.Authorization;

public sealed class PolicyState
{
    public bool MaintenanceMode { get; set; }
    public HashSet<string> LockedServerIds { get; set; } = [];
}