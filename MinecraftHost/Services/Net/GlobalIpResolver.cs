using System.Net.Http;

namespace MinecraftHost.Services.Net;

public sealed class GlobalIpResolver(HttpClient httpClient) : Interfaces.Net.IGlobalIpResolver
{
    private static readonly string[] Ipv4Apis =
    [
        "https://api.ipify.org",
        "https://ipv4.icanhazip.com"
    ];

    private static readonly string[] Ipv6Apis =
    [
        "https://api6.ipify.org",
        "https://ipv6.icanhazip.com"
    ];

    public async Task<(string? Ipv4, string? Ipv6)> ResolveAsync()
    {
        var ipv4Task = GetFirstSuccessfulResultAsync(Ipv4Apis);
        var ipv6Task = GetFirstSuccessfulResultAsync(Ipv6Apis);

        await Task.WhenAll(ipv4Task, ipv6Task);

        return (await ipv4Task, await ipv6Task);
    }

    private async Task<string?> GetFirstSuccessfulResultAsync(string[] apis)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var tasks = apis.Select(api => GetIpAsync(api, cts.Token)).ToList();

        while (tasks.Count > 0)
        {
            var completedTask = await Task.WhenAny(tasks);
            tasks.Remove(completedTask);
            var result = await completedTask;
            if (!string.IsNullOrWhiteSpace(result))
            {
                await cts.CancelAsync();
                return result;
            }
        }

        return null;
    }

    private async Task<string?> GetIpAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            var response = await httpClient.GetStringAsync(url, cancellationToken);
            return response.Trim();
        }
        catch
        {
            return null;
        }
    }
}