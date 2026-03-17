using System.Net.Http;

namespace MinecraftHost.Services.Net;

public static class HttpClientProvider
{
    private static readonly Lazy<HttpClient> SharedClient = new(CreateClient);

    public static HttpClient Client => SharedClient.Value;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MinecraftServerManager/1.0");
        return client;
    }
}