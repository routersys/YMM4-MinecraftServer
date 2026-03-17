using MinecraftHost.Localization;
using MinecraftHost.Models.Logging;
using MinecraftHost.Models.Server;
using MinecraftHost.Services.Authorization;
using MinecraftHost.Services.Interfaces.Logging;
using MinecraftHost.Services.Interfaces.Server;
using MinecraftHost.Services.Logging;
using MinecraftHost.Services.Net;
using MinecraftHost.Settings.Configuration;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MinecraftHost.Services.Server;

public class JavaResolver : IJavaResolver
{
    private const string AdoptiumAssetsApiTemplate = "https://api.adoptium.net/v3/assets/latest/{0}/hotspot?architecture=x64&image_type={1}&os=windows&vendor=eclipse";
    private const string MojangVersionManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    private const int DefaultJavaMajorVersion = 21;
    private readonly HttpClient _httpClient;
    private readonly IStructuredLogService _structuredLogService;

    public JavaResolver()
        : this(StructuredLogServiceProvider.Instance, HttpClientProvider.Client)
    {
    }

    public JavaResolver(IStructuredLogService structuredLogService)
        : this(structuredLogService, HttpClientProvider.Client)
    {
    }

    public JavaResolver(IStructuredLogService structuredLogService, HttpClient httpClient)
    {
        _structuredLogService = structuredLogService;
        _httpClient = httpClient;
    }

    public async Task<string> ResolveJavaAsync(MinecraftServerConfig? config = null)
    {
        try
        {
            var correlationId = Guid.NewGuid().ToString("N");
            var requiredJavaMajorVersion = await DetermineRequiredJavaMajorVersionAsync(config);
            var savedPath = MinecraftHostSettings.Default.JavaPath;
            if (!string.IsNullOrWhiteSpace(savedPath))
            {
                if (File.Exists(savedPath))
                {
                    _structuredLogService.Log(StructuredLogLevel.Information, nameof(JavaResolver), Texts.JavaResolver_UseConfiguredJavaPath, "ResolveJava", correlationId: correlationId);
                    return savedPath;
                }
                throw new FileNotFoundException(string.Format(Texts.JavaResolver_ConfiguredJavaNotFoundFormat, savedPath));
            }

            var localAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var resourcesPath = Path.Combine(localAppDataPath, "YukkuriMovieMaker", "v4", "resources");
            var jreDir = Path.Combine(resourcesPath, "jre", requiredJavaMajorVersion.ToString());
            var javaExePath = Path.Combine(jreDir, "bin", "java.exe");

            if (File.Exists(javaExePath))
            {
                _structuredLogService.Log(StructuredLogLevel.Information, nameof(JavaResolver), Texts.JavaResolver_UseEmbeddedJre, "ResolveJava", correlationId: correlationId);
                return javaExePath;
            }

            var existingExe = await Task.Run(() => SearchJavaExe(jreDir));
            if (existingExe is not null)
            {
                _structuredLogService.Log(StructuredLogLevel.Information, nameof(JavaResolver), Texts.JavaResolver_UseEmbeddedJreSearchResult, "ResolveJava", correlationId: correlationId);
                return existingExe;
            }

            Directory.CreateDirectory(jreDir);
            var zipPath = Path.Combine(resourcesPath, $"jre-{requiredJavaMajorVersion}.zip");
            if (Directory.Exists(jreDir))
                Directory.Delete(jreDir, recursive: true);
            Directory.CreateDirectory(jreDir);

            var downloadUrl = await ResolveDownloadUrlAsync(requiredJavaMajorVersion);
            using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            EnsureSuccess(response, "ResolveJava.Download");

            await using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await response.Content.CopyToAsync(fs);
            }

            await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, jreDir, overwriteFiles: true));
            File.Delete(zipPath);

            existingExe = await Task.Run(() => SearchJavaExe(jreDir));
            if (existingExe is null)
                throw new FileNotFoundException(Texts.JavaResolver_JavaExeNotFoundAfterExtract);

            _structuredLogService.Log(StructuredLogLevel.Information, nameof(JavaResolver), Texts.JavaResolver_DownloadAndExtractCompleted, "ResolveJava", correlationId: correlationId);

            return existingExe;
        }
        catch (Exception ex)
        {
            throw BoundaryExceptionPolicy.Wrap(nameof(ResolveJavaAsync), ex);
        }
    }

    private string? SearchJavaExe(string directory)
    {
        if (!Directory.Exists(directory)) return null;
        var files = Directory.GetFiles(directory, "java.exe", SearchOption.AllDirectories);
        return files.Length > 0 ? files[0] : null;
    }

    private async Task<int> DetermineRequiredJavaMajorVersionAsync(MinecraftServerConfig? config)
    {
        var minecraftVersion = NormalizeMinecraftVersion(config?.Version);
        if (string.IsNullOrWhiteSpace(minecraftVersion))
            return DefaultJavaMajorVersion;

        try
        {
            using var manifestResponse = await _httpClient.GetAsync(MojangVersionManifestUrl);
            EnsureSuccess(manifestResponse, "ResolveJava.GetManifest");

            var manifestJson = await manifestResponse.Content.ReadAsStringAsync();
            var manifest = JsonNode.Parse(manifestJson);
            var versionNode = (manifest?["versions"] as JsonArray)
                ?.FirstOrDefault(v => string.Equals(v?["id"]?.GetValue<string>(), minecraftVersion, StringComparison.OrdinalIgnoreCase));

            var detailUrl = versionNode?["url"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(detailUrl))
            {
                using var detailResponse = await _httpClient.GetAsync(detailUrl);
                EnsureSuccess(detailResponse, "ResolveJava.GetVersionMeta");

                var detailJson = await detailResponse.Content.ReadAsStringAsync();
                var detail = JsonNode.Parse(detailJson);
                var majorVersion = detail?["javaVersion"]?["majorVersion"]?.GetValue<int>();
                if (majorVersion is > 0)
                    return majorVersion.Value;
            }
        }
        catch
        {
        }

        return GetFallbackJavaMajorVersion(minecraftVersion);
    }

    private async Task<string> ResolveDownloadUrlAsync(int javaMajorVersion)
    {
        var jreUrl = string.Format(AdoptiumAssetsApiTemplate, javaMajorVersion, "jre");
        var jreDownloadUrl = await TryResolvePackageDownloadUrlAsync(jreUrl);
        if (!string.IsNullOrWhiteSpace(jreDownloadUrl))
            return jreDownloadUrl;

        var jdkUrl = string.Format(AdoptiumAssetsApiTemplate, javaMajorVersion, "jdk");
        var jdkDownloadUrl = await TryResolvePackageDownloadUrlAsync(jdkUrl);
        if (!string.IsNullOrWhiteSpace(jdkDownloadUrl))
            return jdkDownloadUrl;

        throw new HttpRequestException($"ResolveJava.Download failed for Java {javaMajorVersion}.");
    }

    private async Task<string?> TryResolvePackageDownloadUrlAsync(string apiUrl)
    {
        using var response = await _httpClient.GetAsync(apiUrl);
        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync();
        var assets = JsonNode.Parse(json) as JsonArray;
        var downloadUrl = assets?[0]?["binary"]?["package"]?["link"]?.GetValue<string>();
        return string.IsNullOrWhiteSpace(downloadUrl) ? null : downloadUrl;
    }

    private static string? NormalizeMinecraftVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return null;

        var match = Regex.Match(version.Trim(), "^\\d+\\.\\d+(?:\\.\\d+)?");
        return match.Success ? match.Value : version.Trim();
    }

    private static int GetFallbackJavaMajorVersion(string minecraftVersion)
    {
        var parts = minecraftVersion.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return DefaultJavaMajorVersion;

        if (!int.TryParse(parts[0], out var majorPart) || majorPart != 1)
            return DefaultJavaMajorVersion;

        if (!int.TryParse(parts[1], out var minorPart))
            return DefaultJavaMajorVersion;

        var patchPart = 0;
        if (parts.Length >= 3)
            int.TryParse(parts[2], out patchPart);

        if (minorPart <= 16)
            return 8;
        if (minorPart == 17)
            return 16;
        if (minorPart == 20 && patchPart >= 5)
            return 21;
        if (minorPart <= 20)
            return 17;
        return 21;
    }

    private static void EnsureSuccess(HttpResponseMessage response, string operation)
    {
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{operation} failed with HTTP {(int)response.StatusCode}.");
    }
}