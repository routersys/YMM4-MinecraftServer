using MinecraftHost.Models.Server;
using MinecraftHost.Settings.Migration;
using System.Collections.ObjectModel;
using YukkuriMovieMaker.Plugin;

namespace MinecraftHost.Settings.Configuration;

public class MinecraftHostSettings : SettingsBase<MinecraftHostSettings>
{
    private static readonly MinecraftHostSettingsMigrator Migrator = new();

    public override SettingsCategory Category => SettingsCategory.Tool;
    public override string Name => "MCHost";
    public override bool HasSettingView => true;
    public override object? SettingView => new SettingPage();

    public ObservableCollection<MinecraftServerConfig> Servers { get; set; } = [];
    public ObservableCollection<MinecraftHost.Models.Scheduler.ScheduledTaskConfig> ScheduledTasks { get; set; } = [];
    public string JavaPath { get; set; } = string.Empty;
    public bool EnablePortForwarding { get; set; } = true;
    public int MaxConsoleLines { get; set; } = 2000;
    public bool EnableAutoRestart { get; set; } = false;
    public int AutoRestartMaxAttempts { get; set; } = 3;
    public int AutoRestartDelaySeconds { get; set; } = 5;
    public int SchemaVersion { get; set; } = MinecraftHostSettingsMigrator.CurrentSchemaVersion;
    public string StructuredLogsDirectory { get; set; } = string.Empty;
    public int StructuredLogMaxFiles { get; set; } = 14;
    public int StructuredLogMaxFileSizeMB { get; set; } = 10;
    public bool EnableSafeUpdateBackup { get; set; } = true;
    public int SafeUpdateBackupRetention { get; set; } = 5;
    public string OperatorRole { get; set; } = "Administrator";
    public bool RequireRoleElevationAuthentication { get; set; } = true;
    public bool PreferWindowsHelloForRoleElevation { get; set; } = true;
    public string RoleElevationPinHash { get; set; } = string.Empty;
    public bool IsGlobalEulaAgreed { get; set; } = false;

    public override void Initialize()
    {
        Migrator.Migrate(this);
        Save();
    }
}