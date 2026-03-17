using MinecraftHost.Localization;
using MinecraftHost.Models.Server;
using MinecraftHost.Services.Interfaces.Server;
using MinecraftHost.Services.Net;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MinecraftHost.Services.Server;

public sealed class JarIntegrityVerifier : IJarIntegrityVerifier
{
    private readonly HttpClient _httpClient;

    public JarIntegrityVerifier()
        : this(HttpClientProvider.Client)
    {
    }

    public JarIntegrityVerifier(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task VerifyAsync(ServerType serverType, string version, string downloadUrl, string jarPath, CancellationToken cancellationToken = default)
    {
        ValidateDownloadHost(serverType, downloadUrl);

        switch (serverType)
        {
            case ServerType.Paper:
                await VerifyPaperAsync(version, downloadUrl, jarPath, cancellationToken);
                return;
            case ServerType.Vanilla:
                await VerifyVanillaAsync(version, jarPath, cancellationToken);
                return;
            case ServerType.Fabric:
                await VerifyWithRemoteChecksumOrContentHashAsync(downloadUrl, jarPath, "Fabric", cancellationToken);
                return;
            case ServerType.Forge:
                await VerifyWithRemoteChecksumOrContentHashAsync(downloadUrl, jarPath, "Forge", cancellationToken);
                return;
            case ServerType.Bedrock:
                await VerifyWithRemoteChecksumOrContentHashAsync(downloadUrl, jarPath, "Bedrock", cancellationToken);
                return;
            default:
                throw new JarIntegrityVerificationException(string.Format(Texts.JarIntegrity_UnsupportedServerTypeFormat, serverType));
        }
    }

    private static void ValidateDownloadHost(ServerType serverType, string downloadUrl)
    {
        var uri = new Uri(downloadUrl);
        if (uri.Scheme != Uri.UriSchemeHttps)
            throw new JarIntegrityVerificationException(Texts.JarIntegrity_OnlyHttpsAllowed);

        var host = uri.Host.ToLowerInvariant();
        var allowed = serverType switch
        {
            ServerType.Paper => host is "api.papermc.io" or "papermc.io" or "fill.papermc.io" or "fill-data.papermc.io",
            ServerType.Vanilla => host is "piston-data.mojang.com" or "launcher.mojang.com" or "piston-meta.mojang.com",
            ServerType.Fabric => host is "meta.fabricmc.net" or "maven.fabricmc.net",
            ServerType.Forge => host is "maven.minecraftforge.net",
            ServerType.Bedrock => host is "repo1.maven.org" or "repo.maven.apache.org" or "repo.opencollab.dev" or "api.github.com" or "github.com" or "objects.githubusercontent.com" or "github-releases.githubusercontent.com" or "release-assets.githubusercontent.com",
            _ => false
        };

        if (!allowed)
            throw new JarIntegrityVerificationException(string.Format(Texts.JarIntegrity_UnauthorizedHostFormat, uri.Host));
    }

    private async Task VerifyVanillaAsync(string version, string jarPath, CancellationToken cancellationToken)
    {
        var manifestJson = await _httpClient.GetStringAsync("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json", cancellationToken);
        var manifest = JsonNode.Parse(manifestJson);

        var versionUrl = manifest?["versions"]?.AsArray()
            .FirstOrDefault(v => v?["id"]?.GetValue<string>() == version)?["url"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(versionUrl))
            throw new JarIntegrityVerificationException(string.Format(Texts.JarIntegrity_VanillaMetadataNotFoundFormat, version));

        var metaJson = await _httpClient.GetStringAsync(versionUrl, cancellationToken);
        var meta = JsonNode.Parse(metaJson);
        var expectedSha1 = meta?["downloads"]?["server"]?["sha1"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(expectedSha1))
            throw new JarIntegrityVerificationException(Texts.JarIntegrity_VanillaSha1NotFound);

        await using var stream = File.OpenRead(jarPath);
        var hash = await SHA1.HashDataAsync(stream, cancellationToken);
        var actual = Convert.ToHexString(hash).ToLowerInvariant();

        if (!string.Equals(actual, expectedSha1.ToLowerInvariant(), StringComparison.Ordinal))
            throw new JarIntegrityVerificationException(Texts.JarIntegrity_VanillaIntegrityFailed);
    }

    private async Task VerifyPaperAsync(string version, string downloadUrl, string jarPath, CancellationToken cancellationToken)
    {
        var uri = new Uri(downloadUrl);
        if (!TryParsePaperBuildNumber(uri, out var buildNumber))
            throw new JarIntegrityVerificationException(Texts.JarIntegrity_PaperBuildNumberParseFailed);

        var expectedSha256 = await TryGetPaperSha256FromV3Async(version, buildNumber, cancellationToken)
            ?? await TryGetPaperSha256FromV2Async(version, buildNumber, cancellationToken);

        if (string.IsNullOrWhiteSpace(expectedSha256))
            throw new JarIntegrityVerificationException(Texts.JarIntegrity_PaperSha256NotFound);

        await using var stream = File.OpenRead(jarPath);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        var actual = Convert.ToHexString(hash).ToLowerInvariant();

        if (!string.Equals(actual, expectedSha256.ToLowerInvariant(), StringComparison.Ordinal))
            throw new JarIntegrityVerificationException(Texts.JarIntegrity_PaperIntegrityFailed);
    }

    private static bool TryParsePaperBuildNumber(Uri downloadUri, out int buildNumber)
    {
        var segments = downloadUri.Segments.Select(s => s.Trim('/')).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        var buildIndex = Array.FindIndex(segments, s => s == "builds");
        if (buildIndex >= 0 && buildIndex + 1 < segments.Length && int.TryParse(segments[buildIndex + 1], out buildNumber))
            return true;

        var fileName = Path.GetFileName(downloadUri.AbsolutePath);
        var match = Regex.Match(fileName, @"paper-[\d\.]+-(\d+)\.jar", RegexOptions.IgnoreCase);
        if (match.Success && int.TryParse(match.Groups[1].Value, out buildNumber))
            return true;

        buildNumber = 0;
        return false;
    }

    private async Task<string?> TryGetPaperSha256FromV3Async(string version, int buildNumber, CancellationToken cancellationToken)
    {
        var metadataUrl = $"https://fill.papermc.io/v3/projects/paper/versions/{version}/builds/{buildNumber}";
        var metadataResponse = await _httpClient.GetAsync(metadataUrl, cancellationToken);
        if (!metadataResponse.IsSuccessStatusCode)
            return null;

        var metadataJson = await metadataResponse.Content.ReadAsStringAsync(cancellationToken);
        var metadata = JsonNode.Parse(metadataJson);
        if (metadata?["downloads"] is not JsonObject downloads)
            return null;

        foreach (var (_, value) in downloads)
        {
            var checksum = value?["checksums"]?["sha256"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(checksum))
                return checksum;
        }

        return null;
    }

    private async Task<string?> TryGetPaperSha256FromV2Async(string version, int buildNumber, CancellationToken cancellationToken)
    {
        var metadataUrl = $"https://api.papermc.io/v2/projects/paper/versions/{version}/builds/{buildNumber}";
        var metadataResponse = await _httpClient.GetAsync(metadataUrl, cancellationToken);
        if (!metadataResponse.IsSuccessStatusCode)
            return null;

        var metadataJson = await metadataResponse.Content.ReadAsStringAsync(cancellationToken);
        var metadata = JsonNode.Parse(metadataJson);
        return metadata?["downloads"]?["application"]?["sha256"]?.GetValue<string>();
    }

    private async Task VerifyWithRemoteChecksumOrContentHashAsync(string downloadUrl, string filePath, string label, CancellationToken cancellationToken)
    {
        var expectedSha256 = await TryGetChecksumAsync($"{downloadUrl}.sha256", cancellationToken);
        if (!string.IsNullOrWhiteSpace(expectedSha256))
        {
            await EnsureHashMatchAsync(filePath, expectedSha256, SHA256.HashDataAsync, string.Format(Texts.JarIntegrity_Sha256VerifyFailedFormat, label), cancellationToken);
            return;
        }

        var expectedSha1 = await TryGetChecksumAsync($"{downloadUrl}.sha1", cancellationToken);
        if (!string.IsNullOrWhiteSpace(expectedSha1))
        {
            await EnsureHashMatchAsync(filePath, expectedSha1, SHA1.HashDataAsync, string.Format(Texts.JarIntegrity_Sha1VerifyFailedFormat, label), cancellationToken);
            return;
        }

        await VerifyByContentHashAsync(downloadUrl, filePath, label, cancellationToken);
    }

    private async Task VerifyByContentHashAsync(string downloadUrl, string filePath, string label, CancellationToken cancellationToken)
    {
        await using var localStream = File.OpenRead(filePath);
        var localHash = await SHA256.HashDataAsync(localStream, cancellationToken);
        var localDigest = Convert.ToHexString(localHash).ToLowerInvariant();

        await using var remoteStream = await _httpClient.GetStreamAsync(downloadUrl, cancellationToken);
        var remoteHash = await SHA256.HashDataAsync(remoteStream, cancellationToken);
        var remoteDigest = Convert.ToHexString(remoteHash).ToLowerInvariant();

        if (!string.Equals(localDigest, remoteDigest, StringComparison.Ordinal))
            throw new JarIntegrityVerificationException(string.Format(Texts.JarIntegrity_ContentHashVerifyFailedFormat, label));
    }

    private async Task<string?> TryGetChecksumAsync(string checksumUrl, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync(checksumUrl, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        return NormalizeChecksum(raw);
    }

    private static string? NormalizeChecksum(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var token = raw
            .Trim()
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(token))
            return null;

        return token.Trim().ToLowerInvariant();
    }

    private static async Task EnsureHashMatchAsync(
        string filePath,
        string expectedHex,
        Func<Stream, CancellationToken, ValueTask<byte[]>> hashProvider,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await hashProvider(stream, cancellationToken);
        var actual = Convert.ToHexString(hash).ToLowerInvariant();

        if (!string.Equals(actual, expectedHex.ToLowerInvariant(), StringComparison.Ordinal))
            throw new JarIntegrityVerificationException(errorMessage);
    }
}