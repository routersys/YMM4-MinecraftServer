using MinecraftHost.Models.Server;
using MinecraftHost.Services.Authorization;
using MinecraftHost.Services.Interfaces.Server;
using MinecraftHost.Services.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace MinecraftHost.Services.Server;

public class VersionResolver : IVersionResolver
{
    private const string PaperApiV3BaseUrl = "https://fill.papermc.io/v3/projects/paper";
    private const string FabricMetaApiBaseUrl = "https://meta.fabricmc.net/v2/versions";
    private const string ForgeMetadataUrl = "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml";
    private const string BedrockVersionsUrl = "https://raw.githubusercontent.com/Bedrock-OSS/BDS-Versions/main/versions.json";
    private readonly HttpClient _httpClient;

    public VersionResolver()
        : this(HttpClientProvider.Client)
    {
    }

    public VersionResolver(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<string>> GetAvailableVersionsAsync(ServerType serverType)
    {
        try
        {
            return serverType switch
            {
                ServerType.Vanilla => await GetVanillaVersionsAsync(),
                ServerType.Paper => await GetPaperVersionsAsync(),
                ServerType.Fabric => await GetFabricVersionsAsync(),
                ServerType.Forge => await GetForgeVersionsAsync(),
                ServerType.Bedrock => await GetBedrockVersionsAsync(),
                _ => []
            };
        }
        catch (Exception ex)
        {
            throw BoundaryExceptionPolicy.Wrap(nameof(GetAvailableVersionsAsync), ex);
        }
    }

    public async Task<IReadOnlyList<string>> GetAvailableBuildIdentifiersAsync(ServerType serverType, string version)
    {
        if (string.IsNullOrWhiteSpace(version) || serverType != ServerType.Paper)
            return [];

        try
        {
            return await GetPaperBuildIdentifiersAsync(version);
        }
        catch (Exception ex)
        {
            throw BoundaryExceptionPolicy.Wrap(nameof(GetAvailableBuildIdentifiersAsync), ex);
        }
    }

    public async Task<string?> GetLatestVersionAsync(ServerType serverType)
    {
        var versions = await GetAvailableVersionsAsync(serverType);
        return versions.Count > 0 ? versions[0] : null;
    }

    public async Task<string?> GetLatestBuildIdentifierAsync(ServerType serverType, string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return null;

        try
        {
            return serverType == ServerType.Paper
                ? await GetLatestPaperBuildIdentifierAsync(version)
                : null;
        }
        catch (Exception ex)
        {
            throw BoundaryExceptionPolicy.Wrap(nameof(GetLatestBuildIdentifierAsync), ex);
        }
    }

    private async Task<IReadOnlyList<string>> GetVanillaVersionsAsync()
    {
        var json = await _httpClient.GetStringAsync("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json");
        var manifest = JsonNode.Parse(json);
        var versions = manifest?["versions"] as JsonArray;
        var result = new List<string>();
        if (versions is not null)
        {
            foreach (var v in versions)
            {
                if (v?["type"]?.GetValue<string>() == "release")
                    result.Add(v["id"]!.GetValue<string>());
            }
        }
        return result;
    }

    private async Task<IReadOnlyList<string>> GetFabricVersionsAsync()
    {
        var response = await _httpClient.GetAsync($"{FabricMetaApiBaseUrl}/game");
        EnsureSuccess(response, "GetFabricVersions");

        var json = await response.Content.ReadAsStringAsync();
        var node = JsonNode.Parse(json) as JsonArray;
        if (node is null)
            return [];

        var result = new List<string>();
        foreach (var item in node)
        {
            if (item?["stable"]?.GetValue<bool>() != true)
                continue;

            var version = item["version"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(version))
                result.Add(version);
        }

        return result;
    }

    private async Task<IReadOnlyList<string>> GetForgeVersionsAsync()
    {
        var versions = await GetMavenMetadataVersionsAsync(ForgeMetadataUrl);
        if (versions.Count == 0)
            return versions;

        versions.Sort(CompareForgeVersionDescending);
        return versions;
    }

    private async Task<IReadOnlyList<string>> GetBedrockVersionsAsync()
    {
        var githubVersions = await GetBedrockVersionsFromGitHubAsync();
        var mergedVersions = new List<string>(githubVersions);
        var seen = new HashSet<string>(githubVersions, StringComparer.OrdinalIgnoreCase);

        if (seen.Add("snapshot"))
            mergedVersions.Add("snapshot");

        if (mergedVersions.Count > 0)
        {
            mergedVersions.Sort(CompareBedrockVersionDescending);
            return mergedVersions;
        }

        throw new HttpRequestException("Bedrock バージョン一覧の取得に失敗しました。");
    }

    private async Task<IReadOnlyList<string>> GetBedrockVersionsFromGitHubAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync(BedrockVersionsUrl);
            if (!response.IsSuccessStatusCode)
                return [];

            var json = await response.Content.ReadAsStringAsync();
            var node = JsonNode.Parse(json);
            if (node?["windows"] is not JsonObject windowsVersions)
                return [];

            var result = new List<string>();
            foreach (var kvp in windowsVersions)
            {
                if (!string.IsNullOrWhiteSpace(kvp.Key))
                    result.Add(kvp.Key);
            }

            return result;
        }
        catch
        {
            return [];
        }
    }

    private async Task<IReadOnlyList<string>> GetPaperVersionsAsync()
    {
        var response = await _httpClient.GetAsync(PaperApiV3BaseUrl);
        EnsureSuccess(response, "GetPaperVersions");

        var json = await response.Content.ReadAsStringAsync();
        var node = JsonNode.Parse(json);
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (node?["versions"] is JsonObject groupedVersions)
        {
            foreach (var (_, value) in groupedVersions)
            {
                if (value is not JsonArray groupedArray)
                    continue;

                foreach (var groupedVersionNode in groupedArray)
                {
                    if (groupedVersionNode is JsonValue groupedValue)
                    {
                        var groupedVersion = groupedValue.GetValue<string>();
                        if (seen.Add(groupedVersion))
                            result.Add(groupedVersion);
                    }
                }
            }
        }

        var versions = GetCandidateArray(node, "versions", "data", "items", "results");
        if (versions is not null)
        {
            for (var i = versions.Count - 1; i >= 0; i--)
            {
                var versionNode = versions[i];
                if (versionNode is null)
                    continue;

                if (versionNode is JsonValue)
                {
                    var version = versionNode.GetValue<string>();
                    if (seen.Add(version))
                        result.Add(version);
                    continue;
                }

                var versionName = versionNode["version"]?.GetValue<string>()
                    ?? versionNode["id"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(versionName))
                {
                    if (seen.Add(versionName))
                        result.Add(versionName);
                }
            }
        }

        CollectVersionsFromObject(result, seen, GetCandidateObject(node, "versions", "data", "items", "results"));

        if (result.Count <= 1)
        {
            foreach (var v in await GetPaperVersionsV2Async())
            {
                if (seen.Add(v))
                    result.Add(v);
            }
        }

        return result;
    }

    private async Task<string?> GetLatestPaperBuildIdentifierAsync(string version)
    {
        var buildIdentifiers = await GetPaperBuildIdentifiersAsync(version);
        if (buildIdentifiers.Count == 0)
            return null;

        return buildIdentifiers[0];
    }

    private async Task<IReadOnlyList<string>> GetPaperBuildIdentifiersAsync(string version)
    {
        var response = await _httpClient.GetAsync($"{PaperApiV3BaseUrl}/versions/{version}/builds?limit=1000");
        EnsureSuccess(response, "GetPaperBuildIdentifiers");

        var json = await response.Content.ReadAsStringAsync();
        var node = JsonNode.Parse(json);
        var builds = GetCandidateArray(node, "builds", "data", "items", "results");
        if (builds is null || builds.Count == 0)
            return [];

        var candidates = new HashSet<int>();
        foreach (var build in builds)
        {
            if (build is null)
                continue;

            if (build is JsonValue)
            {
                if (int.TryParse(build.ToJsonString().Trim('"'), out var rawBuildNumber))
                    candidates.Add(rawBuildNumber);
                continue;
            }

            var channel = build["channel"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(channel) && channel != "default")
                continue;

            var number = GetBuildNumber(build);
            if (number.HasValue)
                candidates.Add(number.Value);
        }

        if (candidates.Count == 0)
        {
            foreach (var build in builds)
            {
                if (build is null)
                    continue;

                if (build is JsonValue)
                {
                    if (int.TryParse(build.ToJsonString().Trim('"'), out var rawBuildNumber))
                        candidates.Add(rawBuildNumber);
                    continue;
                }

                var number = GetBuildNumber(build);
                if (number.HasValue)
                    candidates.Add(number.Value);
            }
        }

        return candidates.OrderByDescending(x => x).Select(x => x.ToString()).ToArray();
    }

    private async Task<List<string>> GetMavenMetadataVersionsAsync(string metadataUrl)
    {
        var response = await _httpClient.GetAsync(metadataUrl);
        EnsureSuccess(response, $"GetMavenMetadataVersions({metadataUrl})");

        var xml = await response.Content.ReadAsStringAsync();
        var document = XDocument.Parse(xml);
        var versions = document
            .Descendants("version")
            .Select(x => x.Value)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return versions;
    }

    private static int? GetBuildNumber(JsonNode build)
    {
        if (build["build"] is not null)
            return build["build"]!.GetValue<int>();
        if (build["id"] is not null)
            return build["id"]!.GetValue<int>();
        if (build["number"] is not null)
            return build["number"]!.GetValue<int>();
        if (build["buildNumber"] is not null)
            return build["buildNumber"]!.GetValue<int>();
        return null;
    }

    private static int CompareForgeVersionDescending(string? left, string? right)
    {
        if (ReferenceEquals(left, right))
            return 0;

        if (left is null)
            return 1;

        if (right is null)
            return -1;

        var leftParts = left.Split('-', 2, StringSplitOptions.TrimEntries);
        var rightParts = right.Split('-', 2, StringSplitOptions.TrimEntries);

        var leftMinecraft = leftParts[0];
        var rightMinecraft = rightParts[0];
        var compareMinecraft = CompareTokenArrays(ParseNumberTokens(leftMinecraft), ParseNumberTokens(rightMinecraft));
        if (compareMinecraft != 0)
            return compareMinecraft;

        var leftForge = leftParts.Length > 1 ? leftParts[1] : string.Empty;
        var rightForge = rightParts.Length > 1 ? rightParts[1] : string.Empty;
        return CompareTokenArrays(ParseNumberTokens(leftForge), ParseNumberTokens(rightForge));
    }

    private static int[] ParseNumberTokens(string version)
    {
        return Regex.Matches(version, "\\d+")
            .Select(match => int.Parse(match.Value))
            .ToArray();
    }

    private static int CompareTokenArrays(IReadOnlyList<int> left, IReadOnlyList<int> right)
    {
        var count = Math.Max(left.Count, right.Count);
        for (var i = 0; i < count; i++)
        {
            var l = i < left.Count ? left[i] : 0;
            var r = i < right.Count ? right[i] : 0;
            if (l == r)
                continue;

            return l > r ? -1 : 1;
        }

        return 0;
    }

    private static int CompareBedrockVersionDescending(string? left, string? right)
    {
        if (ReferenceEquals(left, right))
            return 0;

        if (left is null)
            return 1;

        if (right is null)
            return -1;

        var leftSnapshot = left.Contains("snapshot", StringComparison.OrdinalIgnoreCase);
        var rightSnapshot = right.Contains("snapshot", StringComparison.OrdinalIgnoreCase);
        if (leftSnapshot != rightSnapshot)
            return leftSnapshot ? 1 : -1;

        return CompareTokenArrays(ParseNumberTokens(left), ParseNumberTokens(right));
    }

    private static JsonObject? GetCandidateObject(JsonNode? node, params string[] keys)
    {
        if (node is JsonObject rootObject)
        {
            foreach (var key in keys)
            {
                if (rootObject[key] is JsonObject candidate)
                    return candidate;
            }

            foreach (var key in keys)
            {
                if (rootObject[key] is JsonObject nested)
                {
                    if (nested["items"] is JsonObject items)
                        return items;
                    if (nested["results"] is JsonObject results)
                        return results;
                    if (nested["data"] is JsonObject data)
                        return data;
                }
            }

            if (rootObject["items"] is JsonObject rootItems)
                return rootItems;
            if (rootObject["results"] is JsonObject rootResults)
                return rootResults;
            if (rootObject["data"] is JsonObject rootData)
                return rootData;
        }

        return null;
    }

    private static void CollectVersionsFromObject(List<string> result, HashSet<string> seen, JsonObject? source)
    {
        if (source is null)
            return;

        foreach (var (key, value) in source)
        {
            if (!string.IsNullOrWhiteSpace(key) && char.IsDigit(key[0]))
            {
                if (seen.Add(key))
                    result.Add(key);
                continue;
            }

            if (value is JsonValue jsonValue)
            {
                if (jsonValue.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
                {
                    if (seen.Add(text))
                        result.Add(text);
                }
                continue;
            }

            if (value is JsonObject obj)
            {
                var version = obj["version"]?.GetValue<string>()
                    ?? obj["id"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(version))
                {
                    if (seen.Add(version))
                        result.Add(version);
                }
            }
        }
    }

    private static void CollectBuildsFromObject(HashSet<int> result, JsonObject? source)
    {
        if (source is null)
            return;

        foreach (var (key, value) in source)
        {
            if (int.TryParse(key, out var keyAsBuild))
                result.Add(keyAsBuild);

            if (value is JsonValue jsonValue)
            {
                if (jsonValue.TryGetValue<int>(out var buildNumber))
                    result.Add(buildNumber);
                continue;
            }

            if (value is JsonObject obj)
            {
                var number = GetBuildNumber(obj);
                if (number.HasValue)
                    result.Add(number.Value);
            }
        }
    }

    private async Task<IReadOnlyList<string>> GetPaperVersionsV2Async()
    {
        var response = await _httpClient.GetAsync("https://api.papermc.io/v2/projects/paper");
        EnsureSuccess(response, "GetPaperVersionsV2");

        var json = await response.Content.ReadAsStringAsync();
        var node = JsonNode.Parse(json);
        var versions = node?["versions"] as JsonArray;
        if (versions is null)
            return [];

        var result = new List<string>();
        for (var i = versions.Count - 1; i >= 0; i--)
        {
            if (versions[i] is JsonValue value)
                result.Add(value.GetValue<string>());
        }
        return result;
    }

    private async Task<IReadOnlyList<string>> GetPaperBuildIdentifiersV2Async(string version)
    {
        var response = await _httpClient.GetAsync($"https://api.papermc.io/v2/projects/paper/versions/{version}/builds");
        EnsureSuccess(response, "GetPaperBuildIdentifiersV2");

        var json = await response.Content.ReadAsStringAsync();
        var node = JsonNode.Parse(json);
        var builds = node?["builds"] as JsonArray;
        if (builds is null)
            return [];

        var candidates = new HashSet<int>();
        foreach (var build in builds)
        {
            if (build is JsonValue value && value.TryGetValue<int>(out var number))
                candidates.Add(number);
            else if (build is JsonObject obj)
            {
                var channel = obj["channel"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(channel) && channel != "default")
                    continue;
                var buildNumber = GetBuildNumber(obj);
                if (buildNumber.HasValue)
                    candidates.Add(buildNumber.Value);
            }
        }

        return candidates.OrderByDescending(x => x).Select(x => x.ToString()).ToArray();
    }

    private static JsonArray? GetCandidateArray(JsonNode? node, params string[] keys)
    {
        if (node is JsonArray rootArray)
            return rootArray;

        if (node is null)
            return null;

        foreach (var key in keys)
        {
            if (node[key] is JsonArray directArray)
                return directArray;

            if (node[key] is JsonObject keyedObject)
            {
                if (keyedObject["items"] is JsonArray itemsArray)
                    return itemsArray;

                if (keyedObject["results"] is JsonArray resultsArray)
                    return resultsArray;

                if (keyedObject["data"] is JsonArray nestedDataArray)
                    return nestedDataArray;
            }
        }

        if (node["items"] is JsonArray items)
            return items;

        if (node["results"] is JsonArray results)
            return results;

        if (node["data"] is JsonArray data)
            return data;

        return null;
    }

    private static void EnsureSuccess(HttpResponseMessage response, string operation)
    {
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{operation} failed with HTTP {(int)response.StatusCode}.");
    }
}