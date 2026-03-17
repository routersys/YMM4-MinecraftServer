using MinecraftHost.Localization;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;

namespace MinecraftHost.Services.Authorization;

public static class BoundaryExceptionPolicy
{
    public static BoundaryOperationException Wrap(string operation, Exception exception)
    {
        if (exception is BoundaryOperationException boundaryOperationException)
            return boundaryOperationException;

        var (boundaryType, isTransient) = Classify(exception);
        var message = string.Format(Texts.BoundaryException_WrapMessageFormat, operation, boundaryType, exception.Message);
        return new BoundaryOperationException(operation, boundaryType, message, exception, isTransient);
    }

    private static (BoundaryType boundaryType, bool isTransient) Classify(Exception exception)
    {
        return exception switch
        {
            HttpRequestException => (BoundaryType.Network, true),
            TaskCanceledException => (BoundaryType.Network, true),
            TimeoutException => (BoundaryType.Network, true),
            SocketException => (BoundaryType.Network, true),
            IOException => (BoundaryType.IO, false),
            UnauthorizedAccessException => (BoundaryType.IO, false),
            Win32Exception => (BoundaryType.Process, false),
            InvalidOperationException => (BoundaryType.Process, false),
            _ => (BoundaryType.Unknown, false)
        };
    }
}