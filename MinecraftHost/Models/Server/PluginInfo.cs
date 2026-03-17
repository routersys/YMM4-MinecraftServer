namespace MinecraftHost.Models.Server;

public class PluginInfo
{
    public string FileName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long SizeKB { get; set; }
    public string FullPath { get; set; } = string.Empty;
}