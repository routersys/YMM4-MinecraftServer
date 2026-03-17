using MinecraftHost.Localization;
using MinecraftHost.Models.Server;
using MinecraftHost.Settings.Configuration;

namespace MinecraftHost.Settings.Migration;

public sealed class MinecraftHostSettingsMigrator
{
    public const int CurrentSchemaVersion = 3;

    public void Migrate(MinecraftHostSettings settings)
    {
        settings.SchemaVersion = Math.Clamp(settings.SchemaVersion, 1, CurrentSchemaVersion);

        if (settings.SchemaVersion < 2)
            ApplyVersion2Defaults(settings);

        if (settings.SchemaVersion < 3)
            ApplyVersion3Defaults(settings);

        Normalize(settings);
        settings.SchemaVersion = CurrentSchemaVersion;
    }

    private static void ApplyVersion2Defaults(MinecraftHostSettings settings)
    {
        settings.StructuredLogMaxFiles = 14;
        settings.StructuredLogMaxFileSizeMB = 10;
        settings.StructuredLogsDirectory = string.Empty;
    }

    private static void ApplyVersion3Defaults(MinecraftHostSettings settings)
    {
        settings.RequireRoleElevationAuthentication = true;
        settings.PreferWindowsHelloForRoleElevation = true;
        settings.RoleElevationPinHash = string.Empty;
    }

    private static void Normalize(MinecraftHostSettings settings)
    {
        settings.Servers ??= [];
        settings.JavaPath ??= string.Empty;
        settings.StructuredLogsDirectory ??= string.Empty;
        settings.OperatorRole = string.IsNullOrWhiteSpace(settings.OperatorRole) ? "Administrator" : settings.OperatorRole;
        settings.RoleElevationPinHash ??= string.Empty;
        settings.MaxConsoleLines = Math.Clamp(settings.MaxConsoleLines, 200, 20000);
        settings.AutoRestartMaxAttempts = Math.Clamp(settings.AutoRestartMaxAttempts, 1, 20);
        settings.AutoRestartDelaySeconds = Math.Clamp(settings.AutoRestartDelaySeconds, 1, 300);
        settings.StructuredLogMaxFiles = Math.Clamp(settings.StructuredLogMaxFiles, 1, 365);
        settings.StructuredLogMaxFileSizeMB = Math.Clamp(settings.StructuredLogMaxFileSizeMB, 1, 200);

        foreach (var server in settings.Servers)
            NormalizeServer(server);
    }

    private static void NormalizeServer(MinecraftServerConfig server)
    {
        server.Id = string.IsNullOrWhiteSpace(server.Id) ? Guid.NewGuid().ToString() : server.Id;
        server.Name = string.IsNullOrWhiteSpace(server.Name) ? Texts.MainPage_NewServerName : server.Name;
        server.Version = string.IsNullOrWhiteSpace(server.Version) ? "latest" : server.Version;
        server.DirectoryName = string.IsNullOrWhiteSpace(server.DirectoryName) ? Guid.NewGuid().ToString() : server.DirectoryName;
        server.MaxMemoryMB = Math.Clamp(server.MaxMemoryMB, 512, 131072);
        server.Port = Math.Clamp(server.Port, 1, 65535);
    }
}