namespace MinecraftHost.Models.Server;

public class MinecraftServerConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "新規サーバー";
    public ServerType ServerType { get; set; } = ServerType.Vanilla;
    public string Version { get; set; } = "1.21.4";
    public string BuildIdentifier { get; set; } = string.Empty;
    public int MaxMemoryMB { get; set; } = 2048;
    public int Port { get; set; } = 25565;
    public string DirectoryName { get; set; } = Guid.NewGuid().ToString();
}