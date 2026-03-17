using MinecraftHost.Services.Interfaces.Server;

namespace MinecraftHost.Services.Server;

public sealed class ServerProcessFactory : IServerProcessFactory
{
    public IServerProcess Create(string javaPath, string jarPath, int maxMemoryMB, string workingDirectory, int port)
    {
        return new ServerProcess(javaPath, jarPath, maxMemoryMB, workingDirectory, port);
    }
}