namespace MinecraftHost.Services.Interfaces.Server;

public interface IServerProcessFactory
{
    IServerProcess Create(string javaPath, string jarPath, int maxMemoryMB, string workingDirectory, int port);
}