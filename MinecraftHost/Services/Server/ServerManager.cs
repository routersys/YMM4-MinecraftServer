using MinecraftHost.Models.Logging;
using MinecraftHost.Models.Server;
using MinecraftHost.Services.Authorization;
using MinecraftHost.Services.Interfaces.Logging;
using MinecraftHost.Services.Interfaces.Server;
using MinecraftHost.Services.Logging;
using MinecraftHost.Services.Net;
using MinecraftHost.Settings.Configuration;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MinecraftHost.Services.Server;

public class ServerManager : IServerManager
{
    private const string PaperApiV2BaseUrl = "https://api.papermc.io/v2/projects/paper";
    private const string PaperApiV3BaseUrl = "https://fill.papermc.io/v3/projects/paper";
    private const string FabricMetaApiBaseUrl = "https://meta.fabricmc.net/v2/versions";
    private const string ForgeMavenBaseUrl = "https://maven.minecraftforge.net/net/minecraftforge/forge";
    private const string BedrockVersionsUrl = "https://raw.githubusercontent.com/Bedrock-OSS/BDS-Versions/main/versions.json";
    private readonly HttpClient _httpClient;
    private readonly IJarIntegrityVerifier _jarIntegrityVerifier;
    private readonly IStructuredLogService _structuredLogService;

    public ServerManager()
        : this(new JarIntegrityVerifier(), StructuredLogServiceProvider.Instance, HttpClientProvider.Client)
    {
    }

    public ServerManager(IJarIntegrityVerifier jarIntegrityVerifier, IStructuredLogService structuredLogService)
        : this(jarIntegrityVerifier, structuredLogService, HttpClientProvider.Client)
    {
    }

    public ServerManager(IJarIntegrityVerifier jarIntegrityVerifier, IStructuredLogService structuredLogService, HttpClient httpClient)
    {
        _jarIntegrityVerifier = jarIntegrityVerifier;
        _structuredLogService = structuredLogService;
        _httpClient = httpClient;
    }

    public string GetServerDirectory(MinecraftServerConfig config)
    {
        try
        {
            var basePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppDomain.CurrentDomain.BaseDirectory;
            var dir = Path.Combine(basePath, "servers", config.DirectoryName);
            Directory.CreateDirectory(dir);
            return dir;
        }
        catch (Exception ex)
        {
            throw BoundaryExceptionPolicy.Wrap(nameof(GetServerDirectory), ex);
        }
    }

    public async Task<string> EnsureServerJarAsync(MinecraftServerConfig config, string? javaPath = null, IProgress<double>? progress = null)
    {
        string? backupPath = null;
        var backupCreated = false;
        var existingJarNeedsReplacement = false;

        try
        {
            var dir = GetServerDirectory(config);
            var jarName = CreateJarFileName(config);
            var jarPath = Path.Combine(dir, jarName);
            DeleteLegacyServerJars(dir, jarPath);
            var correlationId = Guid.NewGuid().ToString("N");
            var forgeJarExecutable = config.ServerType != ServerType.Forge || HasMainManifestAttribute(jarPath);

            if (config.ServerType == ServerType.Forge)
                return await EnsureForgeServerJarAsync(config, dir, jarPath, javaPath, progress, correlationId);

            if (config.ServerType == ServerType.Bedrock)
                return await EnsureBedrockServerAsync(config, dir, progress, correlationId);

            if (File.Exists(jarPath) && IsInstalledVersionMatch(dir, config) && forgeJarExecutable)
            {
                _structuredLogService.Log(StructuredLogLevel.Information, nameof(ServerManager), "同一バージョン/ビルドのため既存サーバーJARを利用します。", "EnsureServerJar", config.Id, correlationId: correlationId);
                progress?.Report(100);
                return jarPath;
            }

            var downloadUrl = config.ServerType switch
            {
                ServerType.Vanilla => await ResolveVanillaDownloadUrlAsync(config.Version),
                ServerType.Paper => await ResolvePaperDownloadUrlAsync(config.Version, config.BuildIdentifier),
                ServerType.Fabric => await ResolveFabricDownloadUrlAsync(config.Version),
                ServerType.Forge => await ResolveForgeDownloadUrlAsync(config.Version),
                ServerType.Bedrock => await ResolveBedrockDownloadUrlAsync(config.Version),
                _ => throw new InvalidOperationException($"未対応のサーバータイプです: {config.ServerType}")
            };

            if (File.Exists(jarPath))
            {
                if (config.ServerType == ServerType.Forge && !forgeJarExecutable)
                {
                    existingJarNeedsReplacement = true;
                }
                else
                {
                    try
                    {
                        await _jarIntegrityVerifier.VerifyAsync(config.ServerType, config.Version, downloadUrl, jarPath);
                        PersistInstalledVersion(dir, config);
                        _structuredLogService.Log(StructuredLogLevel.Information, nameof(ServerManager), "既存のサーバーJARを利用します。", "EnsureServerJar", config.Id, correlationId: correlationId);
                        progress?.Report(100);
                        return jarPath;
                    }
                    catch (JarIntegrityVerificationException)
                    {
                        existingJarNeedsReplacement = true;
                    }
                }
            }

            progress?.Report(0);

            if (existingJarNeedsReplacement && MinecraftHostSettings.Default.EnableSafeUpdateBackup)
            {
                var backupProgress = new Progress<double>(p => progress?.Report(Math.Clamp(p * 0.4, 0, 40)));
                backupPath = await CreateDirectoryBackupAsync(dir, backupProgress);
                backupCreated = !string.IsNullOrWhiteSpace(backupPath);
                PruneBackups(dir, Math.Clamp(MinecraftHostSettings.Default.SafeUpdateBackupRetention, 1, 30));
            }

            progress?.Report(existingJarNeedsReplacement ? 40 : 0);

            if (existingJarNeedsReplacement && File.Exists(jarPath))
                File.Delete(jarPath);

            _structuredLogService.Log(StructuredLogLevel.Information, nameof(ServerManager), $"サーバーJARのダウンロードを開始します: {config.ServerType} {config.Version}", "EnsureServerJar", config.Id, correlationId: correlationId);

            var downloadProgress = new Progress<double>(p => progress?.Report(Math.Clamp(40 + (p * 0.6), 40, 100)));
            await DownloadFileAsync(downloadUrl, jarPath, downloadProgress);

            await _jarIntegrityVerifier.VerifyAsync(config.ServerType, config.Version, downloadUrl, jarPath);
            PersistInstalledVersion(dir, config);
            progress?.Report(100);
            _structuredLogService.Log(StructuredLogLevel.Information, nameof(ServerManager), "サーバーJARの整合性検証に成功しました。", "VerifyJar", config.Id, correlationId: correlationId);

            if (backupCreated)
                _structuredLogService.Log(StructuredLogLevel.Information, nameof(ServerManager), "安全更新バックアップを作成しました。", "CreateServerBackup", config.Id, correlationId: correlationId);

            return jarPath;
        }
        catch (Exception ex)
        {
            try
            {
                var dir = GetServerDirectory(config);
                var jarName = CreateJarFileName(config);
                var jarPath = Path.Combine(dir, jarName);
                if (!string.IsNullOrWhiteSpace(backupPath) && File.Exists(backupPath))
                {
                    await RestoreDirectoryBackupAsync(dir, backupPath);
                    _structuredLogService.Log(StructuredLogLevel.Warning, nameof(ServerManager), "失敗のためサーバー全体をバックアップから復元しました。", "RestoreServerBackup", config.Id, correlationId: Guid.NewGuid().ToString("N"));
                }
                else if (File.Exists(jarPath))
                {
                    File.Delete(jarPath);
                }
            }
            catch
            {
            }

            _structuredLogService.Log(StructuredLogLevel.Error, nameof(ServerManager), "サーバーJAR準備処理に失敗しました。", "EnsureServerJar", config.Id, ex);
            throw BoundaryExceptionPolicy.Wrap(nameof(EnsureServerJarAsync), ex);
        }
    }

    private async Task<string> EnsureForgeServerJarAsync(
        MinecraftServerConfig config,
        string serverDirectory,
        string jarPath,
        string? javaPath,
        IProgress<double>? progress,
        string correlationId)
    {
        var executableDownloadUrl = await ResolveForgeDownloadUrlAsync(config.Version);

        if (File.Exists(jarPath) && IsInstalledVersionMatch(serverDirectory, config) && HasMainManifestAttribute(jarPath) && HasForgeLibraries(serverDirectory))
        {
            await _jarIntegrityVerifier.VerifyAsync(config.ServerType, config.Version, executableDownloadUrl, jarPath);
            progress?.Report(100);
            return jarPath;
        }

        progress?.Report(0);
        var installerUrl = ResolveForgeInstallerDownloadUrl(config.Version);
        var installerPath = Path.Combine(serverDirectory, "forge-installer.jar");
        var downloadProgress = new Progress<double>(p => progress?.Report(Math.Clamp(p * 60, 0, 60)));
        await DownloadFileAsync(installerUrl, installerPath, downloadProgress);

        var installProgressReporter = CreateProcessProgressReporter(progress, 60, 95);
        await EnsureForgeLibrariesInstalledAsync(javaPath, installerPath, serverDirectory, installProgressReporter);
        progress?.Report(95);

        var launchJarPath = TryGetForgeLaunchJarPath(serverDirectory, config.Version);
        if (string.IsNullOrWhiteSpace(launchJarPath) || !File.Exists(launchJarPath))
            throw new InvalidOperationException($"Forge バージョン {config.Version} の起動JARが生成されませんでした。");

        if (!string.Equals(launchJarPath, jarPath, StringComparison.OrdinalIgnoreCase))
            File.Copy(launchJarPath, jarPath, overwrite: true);

        await _jarIntegrityVerifier.VerifyAsync(config.ServerType, config.Version, executableDownloadUrl, jarPath);
        PersistInstalledVersion(serverDirectory, config);
        progress?.Report(100);

        _structuredLogService.Log(StructuredLogLevel.Information, nameof(ServerManager), "Forge サーバーの依存関係インストールと整合性検証が完了しました。", "EnsureForgeServerJar", config.Id, correlationId: correlationId);
        return jarPath;
    }

    private async Task<string> EnsureBedrockServerAsync(
        MinecraftServerConfig config,
        string serverDirectory,
        IProgress<double>? progress,
        string correlationId)
    {
        var exePath = Path.Combine(serverDirectory, "bedrock_server.exe");
        if (File.Exists(exePath) && IsInstalledVersionMatch(serverDirectory, config))
        {
            progress?.Report(100);
            return exePath;
        }

        progress?.Report(0);
        var downloadUrl = await ResolveBedrockDownloadUrlAsync(config.Version);
        var zipPath = Path.Combine(serverDirectory, "bedrock_server.zip");

        var downloadProgress = new Progress<double>(p => progress?.Report(Math.Clamp(p * 50, 0, 50)));
        await DownloadFileAsync(downloadUrl, zipPath, downloadProgress);

        progress?.Report(60);

        await Task.Run(() =>
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var entries = archive.Entries;
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var destPath = Path.GetFullPath(Path.Combine(serverDirectory, entry.FullName));
                if (!destPath.StartsWith(serverDirectory, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(destPath);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                    if (File.Exists(destPath))
                    {
                        var name = entry.Name.ToLowerInvariant();
                        if (name is "server.properties" or "allowlist.json" or "permissions.json")
                            continue;
                    }
                    entry.ExtractToFile(destPath, overwrite: true);
                }
                progress?.Report(60 + (i * 40.0 / entries.Count));
            }
        });

        File.Delete(zipPath);
        PersistInstalledVersion(serverDirectory, config);
        progress?.Report(100);

        _structuredLogService.Log(StructuredLogLevel.Information, nameof(ServerManager), "Bedrock サーバーの準備が完了しました。", "EnsureBedrockServer", config.Id, correlationId: correlationId);
        return exePath;
    }

    private static async Task<string?> CreateDirectoryBackupAsync(string serverDirectory, IProgress<double>? progress)
    {
        if (!Directory.Exists(serverDirectory))
            return null;

        return await Task.Run(() =>
        {
            var backupsDirectory = Path.Combine(serverDirectory, "backups");
            Directory.CreateDirectory(backupsDirectory);
            var backupName = $"server-{DateTime.UtcNow:yyyyMMddHHmmssfff}.zip";
            var backupPath = Path.Combine(backupsDirectory, backupName);

            using var archive = ZipFile.Open(backupPath, ZipArchiveMode.Create);
            var allFiles = Directory.GetFiles(serverDirectory, "*", SearchOption.AllDirectories)
                .Where(path => !IsPathInDirectory(path, backupsDirectory));
            var files = allFiles as string[] ?? allFiles.ToArray();
            if (files.Length == 0)
                progress?.Report(100);

            for (var i = 0; i < files.Length; i++)
            {
                var file = files[i];
                var relativePath = Path.GetRelativePath(serverDirectory, file);
                archive.CreateEntryFromFile(file, relativePath, CompressionLevel.SmallestSize);
                progress?.Report(((i + 1) * 100.0) / files.Length);
            }

            return backupPath;
        });
    }

    private static async Task RestoreDirectoryBackupAsync(string serverDirectory, string backupPath)
    {
        await Task.Run(() =>
        {
            var tempDirectory = Path.Combine(Path.GetTempPath(), $"mh-restore-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            try
            {
                ZipFile.ExtractToDirectory(backupPath, tempDirectory, overwriteFiles: true);

                foreach (var file in Directory.GetFiles(serverDirectory, "*", SearchOption.TopDirectoryOnly))
                {
                    var fileName = Path.GetFileName(file);
                    if (string.Equals(fileName, "backups", StringComparison.OrdinalIgnoreCase))
                        continue;

                    File.Delete(file);
                }

                foreach (var directory in Directory.GetDirectories(serverDirectory, "*", SearchOption.TopDirectoryOnly))
                {
                    var directoryName = Path.GetFileName(directory);
                    if (string.Equals(directoryName, "backups", StringComparison.OrdinalIgnoreCase))
                        continue;

                    Directory.Delete(directory, recursive: true);
                }

                foreach (var sourceDirectory in Directory.GetDirectories(tempDirectory, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(tempDirectory, sourceDirectory);
                    Directory.CreateDirectory(Path.Combine(serverDirectory, relative));
                }

                foreach (var sourceFile in Directory.GetFiles(tempDirectory, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(tempDirectory, sourceFile);
                    var destination = Path.Combine(serverDirectory, relative);
                    var destinationDirectory = Path.GetDirectoryName(destination);
                    if (!string.IsNullOrWhiteSpace(destinationDirectory))
                        Directory.CreateDirectory(destinationDirectory);

                    File.Copy(sourceFile, destination, overwrite: true);
                }
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                    Directory.Delete(tempDirectory, recursive: true);
            }
        });
    }

    private static void PruneBackups(string serverDirectory, int keepCount)
    {
        var backupsDirectory = Path.Combine(serverDirectory, "backups");
        if (!Directory.Exists(backupsDirectory))
            return;

        var backupFiles = new DirectoryInfo(backupsDirectory)
            .GetFiles("server-*", SearchOption.TopDirectoryOnly)
            .Where(file => file.Extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) || file.Extension.Equals(".jar", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.CreationTimeUtc)
            .ToArray();

        for (var i = keepCount; i < backupFiles.Length; i++)
            backupFiles[i].Delete();
    }

    private static bool IsPathInDirectory(string path, string directory)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath.StartsWith(fullDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || string.Equals(fullPath, fullDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetVersionMarkerPath(string serverDirectory)
    {
        return Path.Combine(serverDirectory, ".installed-version.json");
    }

    private static bool IsInstalledVersionMatch(string serverDirectory, MinecraftServerConfig config)
    {
        var markerPath = GetVersionMarkerPath(serverDirectory);
        if (!File.Exists(markerPath))
            return false;

        try
        {
            var marker = JsonNode.Parse(File.ReadAllText(markerPath));
            var savedServerType = marker?["serverType"]?.GetValue<string>();
            var savedVersion = marker?["version"]?.GetValue<string>();
            var savedBuild = marker?["buildIdentifier"]?.GetValue<string>();

            if (!string.Equals(savedServerType, config.ServerType.ToString(), StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.Equals(savedVersion, config.Version, StringComparison.Ordinal))
                return false;

            var normalizedSavedBuild = string.IsNullOrWhiteSpace(savedBuild) ? string.Empty : savedBuild;
            var normalizedTargetBuild = string.IsNullOrWhiteSpace(config.BuildIdentifier) ? string.Empty : config.BuildIdentifier;
            return string.Equals(normalizedSavedBuild, normalizedTargetBuild, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static void PersistInstalledVersion(string serverDirectory, MinecraftServerConfig config)
    {
        var marker = new JsonObject
        {
            ["serverType"] = config.ServerType.ToString(),
            ["version"] = config.Version,
            ["buildIdentifier"] = string.IsNullOrWhiteSpace(config.BuildIdentifier) ? string.Empty : config.BuildIdentifier
        };

        File.WriteAllText(GetVersionMarkerPath(serverDirectory), marker.ToJsonString());
    }

    public async Task WriteServerPropertiesAsync(MinecraftServerConfig config)
    {
        try
        {
            var dir = GetServerDirectory(config);
            var propsPath = Path.Combine(dir, "server.properties");

            if (File.Exists(propsPath))
            {
                var lines = (await File.ReadAllLinesAsync(propsPath, Encoding.UTF8)).ToList();
                var portIdx = lines.FindIndex(l => l.StartsWith("server-port="));
                if (portIdx >= 0)
                    lines[portIdx] = $"server-port={config.Port}";
                else
                    lines.Add($"server-port={config.Port}");

                if (config.ServerType == ServerType.Bedrock)
                {
                    var portV6Idx = lines.FindIndex(l => l.StartsWith("server-portv6="));
                    if (portV6Idx >= 0)
                        lines[portV6Idx] = $"server-portv6={config.Port + 1}";
                    else
                        lines.Add($"server-portv6={config.Port + 1}");
                }

                await File.WriteAllLinesAsync(propsPath, lines, Encoding.UTF8);
            }
            else
            {
                var v6PortLine = config.ServerType == ServerType.Bedrock ? $"\nserver-portv6={config.Port + 1}" : "";
                await File.WriteAllTextAsync(propsPath,
                    $"server-port={config.Port}{v6PortLine}\nonline-mode=true\n", Encoding.UTF8);
            }
        }
        catch (Exception ex)
        {
            throw BoundaryExceptionPolicy.Wrap(nameof(WriteServerPropertiesAsync), ex);
        }
    }

    private async Task<string> ResolveVanillaDownloadUrlAsync(string version)
    {
        const string manifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

        var manifestResponse = await _httpClient.GetAsync(manifestUrl);
        EnsureSuccess(manifestResponse, "ResolveVanillaDownloadUrl.Manifest");

        var manifestJson = await manifestResponse.Content.ReadAsStringAsync();
        var manifest = JsonNode.Parse(manifestJson);

        string? versionUrl = null;
        var versions = manifest?["versions"]?.AsArray();
        if (versions is not null)
        {
            foreach (var v in versions)
            {
                if (v?["id"]?.GetValue<string>() == version)
                {
                    versionUrl = v["url"]?.GetValue<string>();
                    break;
                }
            }
        }

        if (versionUrl is null)
            throw new InvalidOperationException($"Vanilla バージョン {version} がマニフェストに見つかりません。");

        var metaResponse = await _httpClient.GetAsync(versionUrl);
        EnsureSuccess(metaResponse, "ResolveVanillaDownloadUrl.Metadata");

        var versionMetaJson = await metaResponse.Content.ReadAsStringAsync();
        var versionMeta = JsonNode.Parse(versionMetaJson);
        var downloadUrl = versionMeta?["downloads"]?["server"]?["url"]?.GetValue<string>();

        if (downloadUrl is null)
            throw new InvalidOperationException($"バージョン {version} のサーバーJARダウンロードURLが見つかりません。");

        return downloadUrl;
    }

    private async Task<string> ResolvePaperDownloadUrlAsync(string version, string? preferredBuildIdentifier = null)
    {
        if (int.TryParse(preferredBuildIdentifier, out var preferredBuildNumber))
            return await ResolvePaperDownloadUrlByBuildNumberAsync(version, preferredBuildNumber);

        var buildsUrl = $"{PaperApiV2BaseUrl}/versions/{version}/builds";

        var buildsResponse = await _httpClient.GetAsync(buildsUrl);
        EnsureSuccess(buildsResponse, "ResolvePaperDownloadUrl.Builds");

        var buildJson = await buildsResponse.Content.ReadAsStringAsync();
        var buildNode = JsonNode.Parse(buildJson);
        var builds = buildNode?["builds"]?.AsArray();

        if (builds is null || builds.Count == 0)
            throw new InvalidOperationException($"Paper バージョン {version} のビルドが見つかりません。");

        JsonNode? latestBuild = null;
        var latestBuildNumber = int.MinValue;
        foreach (var build in builds)
        {
            var buildNumberNode = build?["build"];
            if (buildNumberNode is null)
                continue;

            var isDefault = build?["channel"]?.GetValue<string>() == "default";
            if (!isDefault)
                continue;

            var candidateBuildNumber = buildNumberNode.GetValue<int>();
            if (candidateBuildNumber > latestBuildNumber)
            {
                latestBuild = build;
                latestBuildNumber = candidateBuildNumber;
            }
        }

        if (latestBuild is null)
        {
            foreach (var build in builds)
            {
                var buildNumberNode = build?["build"];
                if (buildNumberNode is null)
                    continue;

                var candidateBuildNumber = buildNumberNode.GetValue<int>();
                if (candidateBuildNumber > latestBuildNumber)
                {
                    latestBuild = build;
                    latestBuildNumber = candidateBuildNumber;
                }
            }
        }

        latestBuild ??= builds[^1];

        var buildNumber = latestBuild!["build"]!.GetValue<int>();
        return await ResolvePaperDownloadUrlByBuildNumberAsync(version, buildNumber);
    }

    private async Task<string> ResolvePaperDownloadUrlByBuildNumberAsync(string version, int buildNumber)
    {
        var defaultFileName = $"paper-{version}-{buildNumber}.jar";

        var v3DetailUrl = $"{PaperApiV3BaseUrl}/versions/{version}/builds/{buildNumber}";
        var v3Response = await _httpClient.GetAsync(v3DetailUrl);
        if (v3Response.IsSuccessStatusCode)
        {
            var json = await v3Response.Content.ReadAsStringAsync();
            var node = JsonNode.Parse(json);
            var directUrl = GetDownloadEntryProperty(node, "url");
            if (!string.IsNullOrWhiteSpace(directUrl))
                return directUrl;

            var fileName = GetDownloadEntryProperty(node, "name") ?? defaultFileName;
            return $"{PaperApiV3BaseUrl}/versions/{version}/builds/{buildNumber}/downloads/{fileName}";
        }

        var v2DetailUrl = $"{PaperApiV2BaseUrl}/versions/{version}/builds/{buildNumber}";
        var v2Response = await _httpClient.GetAsync(v2DetailUrl);
        if (v2Response.IsSuccessStatusCode)
        {
            var json = await v2Response.Content.ReadAsStringAsync();
            var node = JsonNode.Parse(json);
            var fileName = node?["downloads"]?["application"]?["name"]?.GetValue<string>()
                ?? defaultFileName;
            return $"{PaperApiV2BaseUrl}/versions/{version}/builds/{buildNumber}/downloads/{fileName}";
        }

        return $"{PaperApiV2BaseUrl}/versions/{version}/builds/{buildNumber}/downloads/{defaultFileName}";
    }

    private async Task<string> ResolveFabricDownloadUrlAsync(string version)
    {
        var loaderResponse = await _httpClient.GetAsync($"{FabricMetaApiBaseUrl}/loader/{version}");
        EnsureSuccess(loaderResponse, "ResolveFabricDownloadUrl.Loader");

        var loaderJson = await loaderResponse.Content.ReadAsStringAsync();
        var loaderNode = JsonNode.Parse(loaderJson) as JsonArray;

        var loaderVersion = loaderNode?
            .FirstOrDefault(x => x?["loader"]?["stable"]?.GetValue<bool>() == true)?["loader"]?["version"]?.GetValue<string>()
            ?? loaderNode?.FirstOrDefault()?["loader"]?["version"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(loaderVersion))
            throw new InvalidOperationException($"Fabric ローダーが見つかりません。version={version}");

        var installerResponse = await _httpClient.GetAsync($"{FabricMetaApiBaseUrl}/installer");
        EnsureSuccess(installerResponse, "ResolveFabricDownloadUrl.Installer");

        var installerJson = await installerResponse.Content.ReadAsStringAsync();
        var installerNode = JsonNode.Parse(installerJson) as JsonArray;
        var installerVersion = installerNode?
            .FirstOrDefault(x => x?["stable"]?.GetValue<bool>() == true)?["version"]?.GetValue<string>()
            ?? installerNode?.FirstOrDefault()?["version"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(installerVersion))
            throw new InvalidOperationException("Fabric installer のバージョン取得に失敗しました。");

        return $"{FabricMetaApiBaseUrl}/loader/{version}/{loaderVersion}/{installerVersion}/server/jar";
    }

    private async Task<string> ResolveForgeDownloadUrlAsync(string version)
    {
        var candidates = new[]
        {
            $"{ForgeMavenBaseUrl}/{version}/forge-{version}-shim.jar",
            $"{ForgeMavenBaseUrl}/{version}/forge-{version}-server.jar",
            $"{ForgeMavenBaseUrl}/{version}/forge-{version}-universal.jar"
        };

        foreach (var candidate in candidates)
        {
            if (await UrlExistsAsync(candidate))
                return candidate;
        }

        throw new InvalidOperationException($"Forge バージョン {version} の実行可能なアーティファクトが見つかりません。");
    }

    private static string ResolveForgeInstallerDownloadUrl(string version)
    {
        return $"{ForgeMavenBaseUrl}/{version}/forge-{version}-installer.jar";
    }

    private async Task<string> ResolveBedrockDownloadUrlAsync(string version)
    {
        var response = await _httpClient.GetAsync(BedrockVersionsUrl);
        EnsureSuccess(response, "ResolveBedrockDownloadUrl");
        var json = await response.Content.ReadAsStringAsync();
        var node = JsonNode.Parse(json);
        var cdnRoot = node?["cdn_root"]?.GetValue<string>() ?? "https://minecraft.azureedge.net";

        if (node?["windows"] is JsonObject windowsVersions)
        {
            var targetVersion = version;
            if (windowsVersions.TryGetPropertyValue(version, out var aliasNode) && aliasNode is JsonValue)
            {
                var value = aliasNode.GetValue<string>();
                if (value.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    return value;
                targetVersion = value;
            }

            if (!string.IsNullOrWhiteSpace(targetVersion))
            {
                return $"{cdnRoot.TrimEnd('/')}/bin-win/bedrock-server-{targetVersion}.zip";
            }
        }
        throw new InvalidOperationException($"Bedrock バージョン {version} の実行可能なアーティファクトが見つかりません。");
    }

    private static bool HasForgeLibraries(string serverDirectory)
    {
        var forgeLibraryDirectory = Path.Combine(serverDirectory, "libraries", "net", "minecraftforge");
        return Directory.Exists(forgeLibraryDirectory);
    }

    private static string? TryGetForgeLaunchJarPath(string serverDirectory, string version)
    {
        var candidates = new[]
        {
            Path.Combine(serverDirectory, $"forge-{version}-shim.jar"),
            Path.Combine(serverDirectory, $"forge-{version}-server.jar"),
            Path.Combine(serverDirectory, $"forge-{version}-universal.jar")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static async Task EnsureForgeLibrariesInstalledAsync(string? javaPath, string installerJarPath, string workingDirectory, Action<string>? outputHandler)
    {
        var executable = string.IsNullOrWhiteSpace(javaPath) ? "java" : javaPath;
        await RunExternalProcessAsync(
            executable,
            $"-jar \"{installerJarPath}\" --installServer",
            workingDirectory,
            TimeSpan.FromMinutes(20),
            outputHandler,
            "Forge installer の実行に失敗しました。");
    }

    private static Action<string> CreateProcessProgressReporter(IProgress<double>? progress, double start, double end)
    {
        var gate = new object();
        var current = start;
        var lastTickUtc = DateTime.UtcNow;

        return line =>
        {
            if (progress is null)
                return;

            var candidate = current;
            var match = Regex.Match(line, "(?<!\\d)(\\d{1,3})\\s*%");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var percent))
            {
                var normalized = Math.Clamp(percent, 0, 100) / 100d;
                candidate = start + ((end - start) * normalized);
            }

            lock (gate)
            {
                if (candidate > current)
                {
                    current = candidate;
                }
                else
                {
                    var nowUtc = DateTime.UtcNow;
                    if ((nowUtc - lastTickUtc).TotalMilliseconds >= 700)
                    {
                        current = Math.Min(end - 0.1, current + 0.4);
                        lastTickUtc = nowUtc;
                    }
                }

                progress.Report(Math.Clamp(current, start, end));
            }
        };
    }

    private static async Task RunExternalProcessAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        TimeSpan timeout,
        Action<string>? outputHandler,
        string failedMessage)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"{failedMessage} プロセスを開始できません。");
        var errorBuffer = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data))
                return;

            outputHandler?.Invoke(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data))
                return;

            errorBuffer.AppendLine(e.Data);
            outputHandler?.Invoke(e.Data);
        };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            throw new InvalidOperationException($"{failedMessage} タイムアウトしました。({timeout.TotalMinutes:0}分)");
        }

        if (process.ExitCode != 0)
        {
            var details = errorBuffer.ToString().Trim();
            if (string.IsNullOrWhiteSpace(details))
                details = $"ExitCode={process.ExitCode}";
            throw new InvalidOperationException($"{failedMessage} {details}");
        }
    }

    private static bool IsSnapshotVersion(string version)
    {
        return version.Contains("snapshot", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetDownloadEntryProperty(JsonNode? node, string propertyName)
    {
        if (node?["downloads"] is not JsonObject downloads)
            return null;

        foreach (var (_, value) in downloads)
        {
            if (value?[propertyName] is JsonValue text)
                return text.GetValue<string>();
        }

        return null;
    }

    private async Task<bool> UrlExistsAsync(string url)
    {
        using var headRequest = new HttpRequestMessage(HttpMethod.Head, url);
        using var headResponse = await _httpClient.SendAsync(headRequest);
        if (headResponse.IsSuccessStatusCode)
            return true;

        if (headResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
            return false;

        using var getRequest = new HttpRequestMessage(HttpMethod.Get, url);
        using var getResponse = await _httpClient.SendAsync(getRequest, HttpCompletionOption.ResponseHeadersRead);
        return getResponse.IsSuccessStatusCode;
    }

    private static string CreateJarFileName(MinecraftServerConfig config)
    {
        return "server.jar";
    }

    private static bool HasMainManifestAttribute(string jarPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(jarPath);
            var manifestEntry = archive.GetEntry("META-INF/MANIFEST.MF");
            if (manifestEntry is null)
                return false;

            using var reader = new StreamReader(manifestEntry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                if (line is not null && line.StartsWith("Main-Class:", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static void DeleteLegacyServerJars(string directory, string currentJarPath)
    {
        var currentJarName = Path.GetFileName(currentJarPath);
        foreach (var file in Directory.GetFiles(directory, "*.jar", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(file);
            if (string.Equals(fileName, currentJarName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (fileName.StartsWith("paper-", StringComparison.OrdinalIgnoreCase)
                || fileName.StartsWith("vanilla-", StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(file);
            }
        }
    }

    private async Task DownloadFileAsync(string url, string dest, IProgress<double>? progress)
    {
        var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        EnsureSuccess(response, "DownloadFile");

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        var bytesRead = 0L;

        await using var fs = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None);
        await using var stream = await response.Content.ReadAsStreamAsync();

        var buffer = new byte[65536];
        int read;
        while ((read = await stream.ReadAsync(buffer)) > 0)
        {
            await fs.WriteAsync(buffer.AsMemory(0, read));
            bytesRead += read;
            if (totalBytes > 0)
                progress?.Report((double)bytesRead / totalBytes * 100.0);
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response, string operation)
    {
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{operation} failed with HTTP {(int)response.StatusCode}.");
    }
}