namespace MinecraftHost.Services.Server;

public sealed class JarIntegrityVerificationException(string message) : Exception(message)
{
}