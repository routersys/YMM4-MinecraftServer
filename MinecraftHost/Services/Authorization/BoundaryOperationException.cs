namespace MinecraftHost.Services.Authorization;

public sealed class BoundaryOperationException(
    string operation,
    BoundaryType boundaryType,
    string message,
    Exception innerException,
    bool isTransient = false) : Exception(message, innerException)
{
    public string Operation { get; } = operation;
    public BoundaryType BoundaryType { get; } = boundaryType;
    public bool IsTransient { get; } = isTransient;
}