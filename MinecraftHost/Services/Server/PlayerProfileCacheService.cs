using MinecraftHost.Services.Net;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace MinecraftHost.Services.Server;

public sealed class PlayerProfileCacheResult
{
    public string Name { get; init; } = string.Empty;
    public string Uuid { get; init; } = string.Empty;
    public string AvatarPath { get; init; } = string.Empty;
}

public sealed class PlayerProfileCacheService
{
    private sealed class CacheEntry
    {
        public string Name { get; set; } = string.Empty;
        public string Uuid { get; set; } = string.Empty;
        public string AvatarFileName { get; set; } = string.Empty;
        public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    }

    private sealed class CacheStore
    {
        public Dictionary<string, CacheEntry> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _sync = new(1, 1);
    private readonly string _cacheRootDirectory;
    private readonly string _avatarDirectory;
    private readonly string _dbPath;

    public PlayerProfileCacheService()
        : this(HttpClientProvider.Client)
    {
    }

    public PlayerProfileCacheService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        var pluginDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppDomain.CurrentDomain.BaseDirectory;
        _cacheRootDirectory = Path.Combine(pluginDirectory, "cache", "minecraft-host", "players");
        _avatarDirectory = Path.Combine(_cacheRootDirectory, "avatars");
        _dbPath = Path.Combine(_cacheRootDirectory, "profiles.json");
        Directory.CreateDirectory(_avatarDirectory);
    }

    public async Task<PlayerProfileCacheResult?> GetOrCreateAsync(string userName, string? preferredUuid = null)
    {
        var normalizedName = (userName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
            return null;

        await _sync.WaitAsync();
        try
        {
            var store = await LoadStoreAsync();
            var key = normalizedName.ToLowerInvariant();
            store.Entries.TryGetValue(key, out var entry);

            var resolvedUuid = NormalizeUuid(preferredUuid);
            if (string.IsNullOrWhiteSpace(resolvedUuid) && entry is not null)
                resolvedUuid = NormalizeUuid(entry.Uuid);

            if (string.IsNullOrWhiteSpace(resolvedUuid))
                resolvedUuid = await ResolveUuidByNameAsync(normalizedName);

            if (string.IsNullOrWhiteSpace(resolvedUuid))
            {
                if (entry is null)
                    return null;

                var offlineAvatar = ResolveAvatarPath(entry.AvatarFileName);
                return new PlayerProfileCacheResult
                {
                    Name = string.IsNullOrWhiteSpace(entry.Name) ? normalizedName : entry.Name,
                    Uuid = NormalizeUuid(entry.Uuid),
                    AvatarPath = File.Exists(offlineAvatar) ? offlineAvatar : string.Empty
                };
            }

            var avatarFileName = entry?.AvatarFileName ?? string.Empty;
            var avatarPath = ResolveAvatarPath(avatarFileName);

            if (!File.Exists(avatarPath))
            {
                var downloaded = await DownloadAvatarAsync(resolvedUuid, normalizedName);
                if (!string.IsNullOrWhiteSpace(downloaded))
                {
                    avatarPath = downloaded;
                    avatarFileName = Path.GetFileName(downloaded);
                }
            }

            var saved = new CacheEntry
            {
                Name = normalizedName,
                Uuid = resolvedUuid,
                AvatarFileName = avatarFileName,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            store.Entries[key] = saved;
            if (!string.IsNullOrWhiteSpace(saved.Uuid))
                store.Entries[$"uuid:{saved.Uuid.ToLowerInvariant()}"] = saved;

            await SaveStoreAsync(store);

            return new PlayerProfileCacheResult
            {
                Name = saved.Name,
                Uuid = saved.Uuid,
                AvatarPath = File.Exists(avatarPath) ? avatarPath : string.Empty
            };
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<string?> ResolveUuidAsync(string userName)
    {
        var result = await GetOrCreateAsync(userName);
        return result?.Uuid;
    }

    public async Task<string?> TryGetAvatarPathAsync(string userName, string? preferredUuid = null)
    {
        var result = await GetOrCreateAsync(userName, preferredUuid);
        if (result is null || string.IsNullOrWhiteSpace(result.AvatarPath))
            return null;

        return result.AvatarPath;
    }

    private async Task<CacheStore> LoadStoreAsync()
    {
        try
        {
            if (!File.Exists(_dbPath))
                return new CacheStore();

            await using var stream = File.OpenRead(_dbPath);
            return await JsonSerializer.DeserializeAsync<CacheStore>(stream) ?? new CacheStore();
        }
        catch
        {
            return new CacheStore();
        }
    }

    private async Task SaveStoreAsync(CacheStore store)
    {
        try
        {
            Directory.CreateDirectory(_cacheRootDirectory);
            await using var stream = File.Create(_dbPath);
            await JsonSerializer.SerializeAsync(stream, store, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
        }
    }

    private async Task<string> ResolveUuidByNameAsync(string userName)
    {
        for (var retry = 0; retry < 2; retry++)
        {
            try
            {
                using var response = await _httpClient.GetAsync($"https://api.mojang.com/users/profiles/minecraft/{Uri.EscapeDataString(userName)}");
                if (response.StatusCode == HttpStatusCode.NoContent || response.StatusCode == HttpStatusCode.NotFound)
                    return string.Empty;

                response.EnsureSuccessStatusCode();
                var payload = await JsonSerializer.DeserializeAsync<MojangProfileResponse>(await response.Content.ReadAsStreamAsync());
                return NormalizeUuid(payload?.id);
            }
            catch when (retry == 0)
            {
                await Task.Delay(350);
            }
            catch
            {
                return string.Empty;
            }
        }

        return string.Empty;
    }

    private async Task<string> DownloadAvatarAsync(string uuid, string userName)
    {
        var normalizedUuid = NormalizeUuid(uuid);
        if (string.IsNullOrWhiteSpace(normalizedUuid))
            return string.Empty;

        var fileName = $"{normalizedUuid.ToLowerInvariant()}.png";
        var destinationPath = Path.Combine(_avatarDirectory, fileName);

        var urls = new[]
        {
            $"https://crafatar.com/avatars/{normalizedUuid}?size=48&overlay=true",
            $"https://minotar.net/helm/{Uri.EscapeDataString(userName)}/48.png"
        };

        foreach (var url in urls)
        {
            try
            {
                var bytes = await _httpClient.GetByteArrayAsync(url);
                if (bytes.Length == 0)
                    continue;

                await File.WriteAllBytesAsync(destinationPath, bytes);
                return destinationPath;
            }
            catch
            {
            }
        }

        return string.Empty;
    }

    private string ResolveAvatarPath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return string.Empty;

        return Path.Combine(_avatarDirectory, fileName);
    }

    private static string NormalizeUuid(string? raw)
    {
        var value = (raw ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var compact = value.Replace("-", string.Empty, StringComparison.Ordinal);
        if (compact.Length != 32)
            return value;

        return $"{compact[..8]}-{compact[8..12]}-{compact[12..16]}-{compact[16..20]}-{compact[20..32]}";
    }

    private sealed class MojangProfileResponse
    {
        public string id { get; set; } = string.Empty;
    }
}