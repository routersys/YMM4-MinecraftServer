using MinecraftHost.Models.Server;
using System.Buffers.Binary;
using System.Net.Sockets;

namespace MinecraftHost.Services.Net;

public sealed class PortAvailabilityChecker : Interfaces.Net.IPortAvailabilityChecker
{
    public async Task<bool> IsAvailableAsync(string ipAddress, int port, ServerType type)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var isLocalOpen = type is ServerType.Bedrock
                ? await CheckUdpAsync("127.0.0.1", port)
                : await CheckTcpAsync("127.0.0.1", port);

            if (isLocalOpen)
                return true;

            var isRemoteOpen = type is ServerType.Bedrock
                ? await CheckUdpAsync(ipAddress, port)
                : await CheckTcpAsync(ipAddress, port);

            if (isRemoteOpen)
                return true;

            if (attempt < 3)
                await Task.Delay(TimeSpan.FromSeconds(2));
        }

        return false;
    }

    private static async Task<bool> CheckTcpAsync(string ipAddress, int port)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await client.ConnectAsync(ipAddress, port, cts.Token);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> CheckUdpAsync(string ipAddress, int port)
    {
        try
        {
            using var client = new UdpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

            ReadOnlySpan<byte> magic = [0x00, 0xff, 0xff, 0x00, 0xfe, 0xfe, 0xfe, 0xfe, 0xfd, 0xfd, 0xfd, 0xfd, 0x12, 0x34, 0x56, 0x78];
            var payload = new byte[33];
            payload[0] = 0x01;
            BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(1, 8), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            magic.CopyTo(payload.AsSpan(9, 16));

            await client.SendAsync(payload, ipAddress, port, cts.Token);
            var result = await client.ReceiveAsync(cts.Token);
            return result.Buffer.Length > 0 && result.Buffer[0] == 0x1c;
        }
        catch
        {
            return false;
        }
    }
}