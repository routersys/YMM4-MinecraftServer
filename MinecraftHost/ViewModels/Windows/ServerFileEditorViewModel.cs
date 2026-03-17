using MinecraftHost.Localization;
using MinecraftHost.Models.Server;
using MinecraftHost.Services.Server;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using YukkuriMovieMaker.Commons;

namespace MinecraftHost.ViewModels.Windows;

public class ServerTargetFile : Bindable
{
    public string Name { get; }
    public string Path { get; }
    public ServerEditorTargetKind Kind { get; }

    public ServerTargetFile(string name, string path, ServerEditorTargetKind kind = ServerEditorTargetKind.File)
    {
        Name = name;
        Path = path;
        Kind = kind;
    }
}

public enum ServerEditorTargetKind
{
    File,
    WorldManager
}

public class PlayerPolicyEntry : Bindable
{
    private string _name = string.Empty;
    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    private string _uuid = string.Empty;
    public string Uuid
    {
        get => _uuid;
        set => Set(ref _uuid, value);
    }

    private int _level = 4;
    public int Level
    {
        get => _level;
        set => Set(ref _level, value);
    }

    private bool _bypassesPlayerLimit;
    public bool BypassesPlayerLimit
    {
        get => _bypassesPlayerLimit;
        set => Set(ref _bypassesPlayerLimit, value);
    }

    public string Created { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss K");
    public string Source { get; set; } = "MinecraftHost";
    public string Expires { get; set; } = "forever";
    public string Reason { get; set; } = "Banned by an operator.";

    private string _avatarPath = string.Empty;
    public string AvatarPath
    {
        get => _avatarPath;
        set => Set(ref _avatarPath, value);
    }
}

public class IpPolicyEntry : Bindable
{
    private string _ip = string.Empty;
    public string Ip
    {
        get => _ip;
        set => Set(ref _ip, value);
    }

    public string Created { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss K");
    public string Source { get; set; } = "MinecraftHost";
    public string Expires { get; set; } = "forever";
    public string Reason { get; set; } = "Banned by an operator.";
}

public class WorldSourceEntry : Bindable
{
    public string DisplayName { get; }
    public string FullPath { get; }
    public bool IsAutoDiscovered { get; }
    public string ThumbnailPath { get; }
    public bool HasThumbnail { get; }

    public WorldSourceEntry(string displayName, string fullPath, bool isAutoDiscovered)
    {
        DisplayName = displayName;
        FullPath = fullPath;
        IsAutoDiscovered = isAutoDiscovered;

        var iconPath = Path.Combine(fullPath, "icon.png");
        if (File.Exists(iconPath))
        {
            ThumbnailPath = iconPath;
            HasThumbnail = true;
        }
        else
        {
            ThumbnailPath = string.Empty;
            HasThumbnail = false;
        }
    }
}

public class WorldSlotViewModel : Bindable
{
    public string Key { get; }
    public string DisplayName { get; }

    private string _targetFolderName;
    public string TargetFolderName
    {
        get => _targetFolderName;
        set => Set(ref _targetFolderName, value);
    }

    private WorldSourceEntry? _selectedSource;
    public WorldSourceEntry? SelectedSource
    {
        get => _selectedSource;
        set => Set(ref _selectedSource, value);
    }

    public WorldSlotViewModel(string key, string displayName, string targetFolderName)
    {
        Key = key;
        DisplayName = displayName;
        _targetFolderName = targetFolderName;
    }
}

public class ServerFileEditorViewModel : Bindable
{
    private static readonly Regex PlayerNamePattern = new("^[A-Za-z0-9_]{3,16}$", RegexOptions.Compiled);
    private readonly PlayerProfileCacheService _profileCacheService = new();

    private readonly string _directoryPath;
    private readonly string _appDataSavesDirectory;

    public ObservableCollection<ServerTargetFile> Files { get; } = new();
    public ObservableCollection<PlayerPolicyEntry> PlayerEntries { get; } = new();
    public ObservableCollection<IpPolicyEntry> IpEntries { get; } = new();
    public ObservableCollection<WorldSourceEntry> WorldSources { get; } = new();
    public ObservableCollection<WorldSlotViewModel> WorldSlots { get; } = new();
    public ObservableCollection<int> OperatorLevels { get; } = [1, 2, 3, 4];

    private ServerTargetFile? _selectedFile;
    public ServerTargetFile? SelectedFile
    {
        get => _selectedFile;
        set
        {
            Set(ref _selectedFile, value);
            _ = LoadFileContentAsync();
            OnPropertyChanged(nameof(CanEdit));
            OnPropertyChanged(nameof(CanSaveFile));
            OnPropertyChanged(nameof(SelectedFileExtension));
            OnPropertyChanged(nameof(IsPlayerPolicyEditor));
            OnPropertyChanged(nameof(IsBannedIpsEditor));
            OnPropertyChanged(nameof(IsOpsEditor));
            OnPropertyChanged(nameof(IsWorldManager));
            SaveCommand.RaiseCanExecuteChanged();
            AddPlayerEntryCommand.RaiseCanExecuteChanged();
            RemovePlayerEntryCommand.RaiseCanExecuteChanged();
            AddIpEntryCommand.RaiseCanExecuteChanged();
            RemoveIpEntryCommand.RaiseCanExecuteChanged();
            AddWorldSourceCommand.RaiseCanExecuteChanged();
            ApplyWorldMappingsCommand.RaiseCanExecuteChanged();
        }
    }

    private string _fileContent = string.Empty;
    public string FileContent
    {
        get => _fileContent;
        set => Set(ref _fileContent, value);
    }

    public bool CanEdit => SelectedFile is not null;
    public bool CanSaveFile => CanEdit && !IsPlayerPolicyEditor && !IsBannedIpsEditor && !IsWorldManager;

    public string SelectedFileExtension =>
        SelectedFile is not null ? System.IO.Path.GetExtension(SelectedFile.Name) : string.Empty;

    public bool IsWhitelistEditor => string.Equals(SelectedFile?.Name, "whitelist.json", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedFile?.Name, "allowlist.json", StringComparison.OrdinalIgnoreCase);
    public bool IsBannedPlayersEditor => string.Equals(SelectedFile?.Name, "banned-players.json", StringComparison.OrdinalIgnoreCase);
    public bool IsOpsEditor => string.Equals(SelectedFile?.Name, "ops.json", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedFile?.Name, "permissions.json", StringComparison.OrdinalIgnoreCase);
    public bool IsBannedIpsEditor => string.Equals(SelectedFile?.Name, "banned-ips.json", StringComparison.OrdinalIgnoreCase);

    public bool IsPlayerPolicyEditor => IsWhitelistEditor || IsBannedPlayersEditor || IsOpsEditor;
    public bool IsWorldManager => SelectedFile?.Kind == ServerEditorTargetKind.WorldManager;

    private string _newPlayerName = string.Empty;
    public string NewPlayerName
    {
        get => _newPlayerName;
        set
        {
            Set(ref _newPlayerName, value);
            AddPlayerEntryCommand.RaiseCanExecuteChanged();
        }
    }

    private string _newIpAddress = string.Empty;
    public string NewIpAddress
    {
        get => _newIpAddress;
        set
        {
            Set(ref _newIpAddress, value);
            AddIpEntryCommand.RaiseCanExecuteChanged();
        }
    }

    private PlayerPolicyEntry? _selectedPlayerEntry;
    public PlayerPolicyEntry? SelectedPlayerEntry
    {
        get => _selectedPlayerEntry;
        set
        {
            Set(ref _selectedPlayerEntry, value);
            RemovePlayerEntryCommand.RaiseCanExecuteChanged();
        }
    }

    private IpPolicyEntry? _selectedIpEntry;
    public IpPolicyEntry? SelectedIpEntry
    {
        get => _selectedIpEntry;
        set
        {
            Set(ref _selectedIpEntry, value);
            RemoveIpEntryCommand.RaiseCanExecuteChanged();
        }
    }

    private string _playerEditorStatus = string.Empty;
    public string PlayerEditorStatus
    {
        get => _playerEditorStatus;
        set => Set(ref _playerEditorStatus, value);
    }

    private string _ipEditorStatus = string.Empty;
    public string IpEditorStatus
    {
        get => _ipEditorStatus;
        set => Set(ref _ipEditorStatus, value);
    }

    private string _newWorldSourcePath = string.Empty;
    public string NewWorldSourcePath
    {
        get => _newWorldSourcePath;
        set
        {
            Set(ref _newWorldSourcePath, value);
            AddWorldSourceCommand.RaiseCanExecuteChanged();
        }
    }

    private string _worldOperationStatus = string.Empty;
    public string WorldOperationStatus
    {
        get => _worldOperationStatus;
        set => Set(ref _worldOperationStatus, value);
    }

    public ActionCommand SaveCommand { get; }
    public ActionCommand AddPlayerEntryCommand { get; }
    public ActionCommand RemovePlayerEntryCommand { get; }
    public ActionCommand AddIpEntryCommand { get; }
    public ActionCommand RemoveIpEntryCommand { get; }
    public ActionCommand RefreshWorldSourcesCommand { get; }
    public ActionCommand AddWorldSourceCommand { get; }
    public ActionCommand RemoveWorldSourceCommand { get; }
    public ActionCommand ApplyWorldMappingsCommand { get; }

    public ServerFileEditorViewModel(string directoryPath, ServerType serverType = ServerType.Vanilla)
    {
        _directoryPath = directoryPath;
        _appDataSavesDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft", "saves");

        var levelName = LoadCurrentLevelName();
        WorldSlots.Add(new WorldSlotViewModel("overworld", Texts.ServerFileEditor_WorldSlotOverworld, levelName));
        WorldSlots.Add(new WorldSlotViewModel("nether", Texts.ServerFileEditor_WorldSlotNether, $"{levelName}_nether"));
        WorldSlots.Add(new WorldSlotViewModel("end", Texts.ServerFileEditor_WorldSlotEnd, $"{levelName}_the_end"));

        var isBedrock = serverType == ServerType.Bedrock;
        var commonFiles = isBedrock ? new[]
        {
            "server.properties",
            "allowlist.json"
        } : new[]
        {
            "server.properties",
            "eula.txt",
            "whitelist.json",
            "ops.json",
            "banned-players.json",
            "banned-ips.json"
        };

        var paperFiles = new[]
        {
            "bukkit.yml",
            "spigot.yml",
            "paper-global.yml",
            "paper-world-defaults.yml",
            "commands.yml"
        };

        foreach (var name in commonFiles)
            Files.Add(new ServerTargetFile(name, Path.Combine(_directoryPath, name)));

        if (serverType == ServerType.Paper)
        {
            foreach (var name in paperFiles)
                Files.Add(new ServerTargetFile(name, Path.Combine(_directoryPath, name)));
        }

        if (!isBedrock)
        {
            Files.Add(new ServerTargetFile("worlds", string.Empty, ServerEditorTargetKind.WorldManager));
        }

        SaveCommand = new ActionCommand(_ => CanSaveFile, _ => _ = SaveFileContentAsync());
        AddPlayerEntryCommand = new ActionCommand(_ => IsPlayerPolicyEditor && !string.IsNullOrWhiteSpace(NewPlayerName), _ => _ = AddPlayerEntryAsync());
        RemovePlayerEntryCommand = new ActionCommand(_ => IsPlayerPolicyEditor && SelectedPlayerEntry is not null, p => RemovePlayerEntry(p as PlayerPolicyEntry));
        AddIpEntryCommand = new ActionCommand(_ => IsBannedIpsEditor && !string.IsNullOrWhiteSpace(NewIpAddress), _ => _ = AddIpEntryAsync());
        RemoveIpEntryCommand = new ActionCommand(_ => IsBannedIpsEditor && SelectedIpEntry is not null, p => RemoveIpEntry(p as IpPolicyEntry));
        RefreshWorldSourcesCommand = new ActionCommand(_ => true, _ => RefreshWorldSources());
        AddWorldSourceCommand = new ActionCommand(_ => IsWorldManager && !string.IsNullOrWhiteSpace(NewWorldSourcePath), _ => AddWorldSource(NewWorldSourcePath));
        RemoveWorldSourceCommand = new ActionCommand(_ => IsWorldManager, p => RemoveWorldSource(p as WorldSourceEntry));
        ApplyWorldMappingsCommand = new ActionCommand(_ => IsWorldManager, _ => _ = ApplyWorldMappingsAsync());

        RefreshWorldSources();

        if (Files.Count > 0)
            SelectedFile = Files[0];
    }

    private async Task LoadFileContentAsync()
    {
        UnsubscribeEntryEvents();
        PlayerEntries.Clear();
        IpEntries.Clear();
        PlayerEditorStatus = string.Empty;
        IpEditorStatus = string.Empty;

        if (SelectedFile is null)
        {
            FileContent = string.Empty;
            return;
        }

        if (IsWorldManager)
        {
            FileContent = string.Empty;
            return;
        }

        if (IsPlayerPolicyEditor)
        {
            await LoadPlayerPolicyEntriesAsync();
            return;
        }

        if (IsBannedIpsEditor)
        {
            await LoadIpPolicyEntriesAsync();
            return;
        }

        if (File.Exists(SelectedFile.Path))
        {
            try
            {
                FileContent = await File.ReadAllTextAsync(SelectedFile.Path, Encoding.UTF8);
            }
            catch
            {
                FileContent = string.Empty;
            }
        }
        else
        {
            FileContent = string.Empty;
        }
    }

    private async Task SaveFileContentAsync()
    {
        if (SelectedFile is null || !CanSaveFile)
            return;

        try
        {
            await File.WriteAllTextAsync(SelectedFile.Path, FileContent, Encoding.UTF8);
        }
        catch
        {
        }
    }

    private async Task LoadPlayerPolicyEntriesAsync()
    {
        if (SelectedFile is null || !File.Exists(SelectedFile.Path))
            return;

        try
        {
            var raw = await File.ReadAllTextAsync(SelectedFile.Path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(raw))
                return;

            var root = JsonNode.Parse(raw) as JsonArray;
            if (root is null)
                return;

            foreach (var node in root.OfType<JsonObject>())
            {
                var name = ReadString(node, "name");
                var uuid = NormalizeUuid(node.ContainsKey("xuid") ? ReadString(node, "xuid") : ReadString(node, "uuid"));
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var entry = new PlayerPolicyEntry
                {
                    Name = name,
                    Uuid = uuid,
                    Created = ReadString(node, "created", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss K")),
                    Source = ReadString(node, "source", "MinecraftHost"),
                    Expires = ReadString(node, "expires", "forever"),
                    Reason = ReadString(node, "reason", "Banned by an operator."),
                    Level = ReadInt(node, "level", 4),
                    BypassesPlayerLimit = node.ContainsKey("ignoresPlayerLimit") ? ReadBool(node, "ignoresPlayerLimit", false) : ReadBool(node, "bypassesPlayerLimit", false)
                };
                AttachEntryEvents(entry);
                PlayerEntries.Add(entry);
                _ = PopulateAvatarAsync(entry);
            }
        }
        catch
        {
            PlayerEditorStatus = Texts.ServerFileEditor_JsonLoadFailed;
        }
    }

    private async Task LoadIpPolicyEntriesAsync()
    {
        if (SelectedFile is null || !File.Exists(SelectedFile.Path))
            return;

        try
        {
            var raw = await File.ReadAllTextAsync(SelectedFile.Path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(raw))
                return;

            var root = JsonNode.Parse(raw) as JsonArray;
            if (root is null)
                return;

            foreach (var node in root.OfType<JsonObject>())
            {
                var ip = ReadString(node, "ip");
                if (string.IsNullOrWhiteSpace(ip))
                    continue;

                var entry = new IpPolicyEntry
                {
                    Ip = ip,
                    Created = ReadString(node, "created", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss K")),
                    Source = ReadString(node, "source", "MinecraftHost"),
                    Expires = ReadString(node, "expires", "forever"),
                    Reason = ReadString(node, "reason", "Banned by an operator.")
                };
                entry.PropertyChanged += OnIpEntryPropertyChanged;
                IpEntries.Add(entry);
            }
        }
        catch
        {
            IpEditorStatus = Texts.ServerFileEditor_JsonLoadFailed;
        }
    }

    private void AttachEntryEvents(PlayerPolicyEntry entry)
    {
        entry.PropertyChanged += OnPlayerEntryPropertyChanged;
    }

    private void UnsubscribeEntryEvents()
    {
        foreach (var entry in PlayerEntries)
            entry.PropertyChanged -= OnPlayerEntryPropertyChanged;

        foreach (var entry in IpEntries)
            entry.PropertyChanged -= OnIpEntryPropertyChanged;
    }

    private async void OnPlayerEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!IsOpsEditor)
            return;

        if (e.PropertyName is not nameof(PlayerPolicyEntry.Level) and not nameof(PlayerPolicyEntry.BypassesPlayerLimit))
            return;

        await SavePlayerPolicyEntriesAsync();
    }

    private async void OnIpEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!IsBannedIpsEditor)
            return;

        if (e.PropertyName != nameof(IpPolicyEntry.Ip))
            return;

        await SaveIpPolicyEntriesAsync();
    }

    private async Task AddPlayerEntryAsync()
    {
        if (!IsPlayerPolicyEditor)
            return;

        var name = (NewPlayerName ?? string.Empty).Trim();
        if (!PlayerNamePattern.IsMatch(name))
        {
            PlayerEditorStatus = Texts.ServerFileEditor_InvalidUserName;
            return;
        }

        if (PlayerEntries.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            PlayerEditorStatus = Texts.ServerFileEditor_DuplicateUserName;
            return;
        }

        PlayerEditorStatus = Texts.ServerFileEditor_ResolvingUuid;
        var resolved = await ResolvePlayerProfileAsync(name);
        if (resolved is null)
        {
            PlayerEditorStatus = Texts.ServerFileEditor_ResolveUuidFailed;
            return;
        }

        var entry = new PlayerPolicyEntry
        {
            Name = resolved.Name,
            Uuid = resolved.Uuid,
            AvatarPath = resolved.AvatarPath,
            Level = 4,
            BypassesPlayerLimit = false
        };

        AttachEntryEvents(entry);
        PlayerEntries.Add(entry);
        NewPlayerName = string.Empty;
        await SavePlayerPolicyEntriesAsync();
        PlayerEditorStatus = Texts.ServerFileEditor_Added;
    }

    private void RemovePlayerEntry(PlayerPolicyEntry? entry)
    {
        var target = entry ?? SelectedPlayerEntry;
        if (target is null)
            return;

        target.PropertyChanged -= OnPlayerEntryPropertyChanged;
        PlayerEntries.Remove(target);
        SelectedPlayerEntry = PlayerEntries.FirstOrDefault();
        _ = SavePlayerPolicyEntriesAsync();
        PlayerEditorStatus = Texts.ServerFileEditor_Removed;
    }

    private async Task SavePlayerPolicyEntriesAsync()
    {
        if (!IsPlayerPolicyEditor || SelectedFile is null)
            return;

        var isAllowlist = string.Equals(SelectedFile.Name, "allowlist.json", StringComparison.OrdinalIgnoreCase);
        var isPermissions = string.Equals(SelectedFile.Name, "permissions.json", StringComparison.OrdinalIgnoreCase);
        var array = new JsonArray();
        foreach (var entry in PlayerEntries.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            var item = new JsonObject();
            if (isAllowlist)
            {
                item["ignoresPlayerLimit"] = entry.BypassesPlayerLimit;
                item["name"] = entry.Name;
            }
            else if (isPermissions)
            {
                item["permission"] = "operator";
                item["xuid"] = entry.Uuid;
                item["name"] = entry.Name;
            }
            else
            {
                item["uuid"] = entry.Uuid;
                item["name"] = entry.Name;

                if (IsBannedPlayersEditor)
                {
                    item["created"] = string.IsNullOrWhiteSpace(entry.Created) ? DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss K") : entry.Created;
                    item["source"] = string.IsNullOrWhiteSpace(entry.Source) ? "MinecraftHost" : entry.Source;
                    item["expires"] = string.IsNullOrWhiteSpace(entry.Expires) ? "forever" : entry.Expires;
                    item["reason"] = string.IsNullOrWhiteSpace(entry.Reason) ? "Banned by an operator." : entry.Reason;
                }

                if (IsOpsEditor)
                {
                    item["level"] = Math.Clamp(entry.Level, 1, 4);
                    item["bypassesPlayerLimit"] = entry.BypassesPlayerLimit;
                }
            }

            array.Add(item);
        }

        var options = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(SelectedFile.Path, array.ToJsonString(options), Encoding.UTF8);
    }

    private async Task AddIpEntryAsync()
    {
        if (!IsBannedIpsEditor)
            return;

        var ip = (NewIpAddress ?? string.Empty).Trim();
        if (!IsValidIpInput(ip))
        {
            IpEditorStatus = Texts.ServerFileEditor_InvalidIpAddress;
            return;
        }

        if (IpEntries.Any(x => string.Equals(x.Ip, ip, StringComparison.OrdinalIgnoreCase)))
        {
            IpEditorStatus = Texts.ServerFileEditor_DuplicateIpAddress;
            return;
        }

        var entry = new IpPolicyEntry
        {
            Ip = ip
        };
        entry.PropertyChanged += OnIpEntryPropertyChanged;
        IpEntries.Add(entry);
        NewIpAddress = string.Empty;
        await SaveIpPolicyEntriesAsync();
        IpEditorStatus = Texts.ServerFileEditor_Added;
    }

    private static bool IsValidIpInput(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
            return false;

        if (IPAddress.TryParse(ip, out _))
            return true;

        if (ip.Count(c => c == '.') == 3)
        {
            var segments = ip.Split('.');
            if (segments.All(s => s == "*" || byte.TryParse(s, out _)))
                return true;
        }

        return false;
    }

    private void RemoveIpEntry(IpPolicyEntry? entry)
    {
        var target = entry ?? SelectedIpEntry;
        if (target is null)
            return;

        target.PropertyChanged -= OnIpEntryPropertyChanged;
        IpEntries.Remove(target);
        SelectedIpEntry = IpEntries.FirstOrDefault();
        _ = SaveIpPolicyEntriesAsync();
        IpEditorStatus = Texts.ServerFileEditor_Removed;
    }

    private async Task SaveIpPolicyEntriesAsync()
    {
        if (!IsBannedIpsEditor || SelectedFile is null)
            return;

        var array = new JsonArray();
        foreach (var entry in IpEntries.OrderBy(x => x.Ip, StringComparer.OrdinalIgnoreCase))
        {
            var item = new JsonObject
            {
                ["ip"] = entry.Ip,
                ["created"] = string.IsNullOrWhiteSpace(entry.Created) ? DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss K") : entry.Created,
                ["source"] = string.IsNullOrWhiteSpace(entry.Source) ? "MinecraftHost" : entry.Source,
                ["expires"] = string.IsNullOrWhiteSpace(entry.Expires) ? "forever" : entry.Expires,
                ["reason"] = string.IsNullOrWhiteSpace(entry.Reason) ? "Banned by an operator." : entry.Reason
            };
            array.Add(item);
        }

        var options = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(SelectedFile.Path, array.ToJsonString(options), Encoding.UTF8);
    }

    private static string ReadString(JsonObject obj, string key, string fallback = "")
    {
        try
        {
            return obj[key]?.GetValue<string>() ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static int ReadInt(JsonObject obj, string key, int fallback)
    {
        try
        {
            return obj[key]?.GetValue<int>() ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static bool ReadBool(JsonObject obj, string key, bool fallback)
    {
        try
        {
            return obj[key]?.GetValue<bool>() ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private async Task<PlayerProfileCacheResult?> ResolvePlayerProfileAsync(string name)
    {
        return await _profileCacheService.GetOrCreateAsync(name);
    }

    private async Task PopulateAvatarAsync(PlayerPolicyEntry entry)
    {
        try
        {
            var result = await _profileCacheService.GetOrCreateAsync(entry.Name, entry.Uuid);
            if (result is null)
                return;

            entry.Uuid = string.IsNullOrWhiteSpace(entry.Uuid) ? result.Uuid : entry.Uuid;
            entry.AvatarPath = result.AvatarPath;
        }
        catch
        {
        }
    }

    private static string NormalizeUuid(string raw)
    {
        var value = (raw ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var compact = value.Replace("-", string.Empty, StringComparison.Ordinal);
        if (compact.Length != 32)
            return value;

        return $"{compact[..8]}-{compact[8..12]}-{compact[12..16]}-{compact[16..20]}-{compact[20..32]}";
    }

    private string LoadCurrentLevelName()
    {
        var propertiesPath = Path.Combine(_directoryPath, "server.properties");
        if (!File.Exists(propertiesPath))
            return "world";

        try
        {
            var line = File.ReadLines(propertiesPath, Encoding.UTF8)
                .FirstOrDefault(x => x.StartsWith("level-name=", StringComparison.OrdinalIgnoreCase));
            if (line is null)
                return "world";

            var levelName = line[(line.IndexOf('=') + 1)..].Trim();
            return string.IsNullOrWhiteSpace(levelName) ? "world" : levelName;
        }
        catch
        {
            return "world";
        }
    }

    private void RefreshWorldSources()
    {
        var manualEntries = WorldSources.Where(x => !x.IsAutoDiscovered).Select(x => x.FullPath).ToList();
        WorldSources.Clear();

        if (Directory.Exists(_appDataSavesDirectory))
        {
            foreach (var directory in Directory.GetDirectories(_appDataSavesDirectory).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(directory);
                WorldSources.Add(new WorldSourceEntry($"{name} (AppData)", directory, true));
            }
        }

        foreach (var path in manualEntries.Where(Directory.Exists))
        {
            if (WorldSources.Any(x => string.Equals(x.FullPath, path, StringComparison.OrdinalIgnoreCase)))
                continue;

            WorldSources.Add(new WorldSourceEntry(Path.GetFileName(path), path, false));
        }

        foreach (var slot in WorldSlots)
        {
            if (slot.SelectedSource is null)
                continue;

            slot.SelectedSource = WorldSources.FirstOrDefault(x => string.Equals(x.FullPath, slot.SelectedSource.FullPath, StringComparison.OrdinalIgnoreCase));
        }

        WorldOperationStatus = string.Empty;
    }

    private void AddWorldSource(string path)
    {
        var normalized = NormalizeWorldPath(path);
        if (string.IsNullOrWhiteSpace(normalized) || !Directory.Exists(normalized))
        {
            WorldOperationStatus = Texts.ServerFileEditor_InvalidFolderPath;
            return;
        }

        if (WorldSources.Any(x => string.Equals(x.FullPath, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            NewWorldSourcePath = string.Empty;
            WorldOperationStatus = Texts.ServerFileEditor_AlreadyExistsInList;
            return;
        }

        var display = Path.GetFileName(normalized);
        WorldSources.Add(new WorldSourceEntry(display, normalized, false));
        NewWorldSourcePath = string.Empty;
        WorldOperationStatus = Texts.ServerFileEditor_Added;
    }

    public bool TryAssignWorldDrop(WorldSlotViewModel? slot, string? path)
    {
        if (slot is null)
            return false;

        var normalized = NormalizeWorldPath(path);
        if (string.IsNullOrWhiteSpace(normalized) || !Directory.Exists(normalized))
            return false;

        if (!WorldSources.Any(x => string.Equals(x.FullPath, normalized, StringComparison.OrdinalIgnoreCase)))
            WorldSources.Add(new WorldSourceEntry(Path.GetFileName(normalized), normalized, false));

        slot.SelectedSource = WorldSources.FirstOrDefault(x => string.Equals(x.FullPath, normalized, StringComparison.OrdinalIgnoreCase));
        WorldOperationStatus = string.Format(Texts.ServerFileEditor_AssignedToSlotFormat, slot.DisplayName);
        return true;
    }

    private static string NormalizeWorldPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var candidate = path.Trim().Trim('"');
        return Path.GetFullPath(candidate);
    }

    private void RemoveWorldSource(WorldSourceEntry? entry)
    {
        if (entry is null)
            return;

        foreach (var slot in WorldSlots.Where(x => x.SelectedSource is not null && string.Equals(x.SelectedSource.FullPath, entry.FullPath, StringComparison.OrdinalIgnoreCase)))
            slot.SelectedSource = null;

        WorldSources.Remove(entry);
        WorldOperationStatus = Texts.ServerFileEditor_RemovedFromList;
    }

    private async Task ApplyWorldMappingsAsync()
    {
        try
        {
            WorldOperationStatus = Texts.ServerFileEditor_ApplyingWorlds;
            var slots = WorldSlots.Where(x => x.SelectedSource is not null && !string.IsNullOrWhiteSpace(x.TargetFolderName)).ToArray();
            if (slots.Length == 0)
            {
                WorldOperationStatus = Texts.ServerFileEditor_NoAssignments;
                return;
            }

            await Task.Run(() =>
            {
                foreach (var slot in slots)
                {
                    var source = ResolveWorldSourceDirectory(slot);
                    if (string.IsNullOrWhiteSpace(source) || !Directory.Exists(source))
                        throw new DirectoryNotFoundException(string.Format(Texts.ServerFileEditor_WorldSourceNotFoundFormat, slot.DisplayName));

                    var target = Path.Combine(_directoryPath, slot.TargetFolderName.Trim());
                    if (Directory.Exists(target))
                        Directory.Delete(target, true);

                    CopyDirectory(source, target);
                }
            });

            var overworld = WorldSlots.FirstOrDefault(x => string.Equals(x.Key, "overworld", StringComparison.OrdinalIgnoreCase));
            if (overworld is not null && !string.IsNullOrWhiteSpace(overworld.TargetFolderName))
                await WriteLevelNameAsync(overworld.TargetFolderName.Trim());

            WorldOperationStatus = Texts.ServerFileEditor_Applied;
        }
        catch (Exception ex)
        {
            WorldOperationStatus = string.Format(Texts.ServerFileEditor_ApplyFailedFormat, ex.Message);
        }
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, file);
            var destination = Path.Combine(destinationDirectory, relative);
            var destinationParent = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(destinationParent))
                Directory.CreateDirectory(destinationParent);
            File.Copy(file, destination, true);
        }
    }

    private string ResolveWorldSourceDirectory(WorldSlotViewModel slot)
    {
        var sourcePath = slot.SelectedSource?.FullPath;
        if (string.IsNullOrWhiteSpace(sourcePath))
            return string.Empty;

        var key = slot.Key.ToLowerInvariant();
        if (key == "nether")
        {
            var candidate = Path.Combine(sourcePath, "DIM-1");
            if (Directory.Exists(candidate))
                return candidate;
        }

        if (key == "end")
        {
            var candidate = Path.Combine(sourcePath, "DIM1");
            if (Directory.Exists(candidate))
                return candidate;
        }

        return sourcePath;
    }

    private async Task WriteLevelNameAsync(string levelName)
    {
        var propertiesPath = Path.Combine(_directoryPath, "server.properties");
        List<string> lines = [];

        if (File.Exists(propertiesPath))
            lines = (await File.ReadAllLinesAsync(propertiesPath, Encoding.UTF8)).ToList();

        var index = lines.FindIndex(x => x.StartsWith("level-name=", StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            lines[index] = $"level-name={levelName}";
        else
            lines.Add($"level-name={levelName}");

        await File.WriteAllLinesAsync(propertiesPath, lines, Encoding.UTF8);
    }
}