using MinecraftHost.Localization;
using MinecraftHost.Models.Authorization;
using MinecraftHost.Models.Collections;
using MinecraftHost.Models.Logging;
using MinecraftHost.Models.Server;
using MinecraftHost.Services;
using MinecraftHost.Services.Authorization;
using MinecraftHost.Services.Interfaces.Audit;
using MinecraftHost.Services.Interfaces.Authorization;
using MinecraftHost.Services.Interfaces.Jobs;
using MinecraftHost.Services.Interfaces.Logging;
using MinecraftHost.Services.Interfaces.Net;
using MinecraftHost.Services.Interfaces.Notifications;
using MinecraftHost.Services.Interfaces.Observability;
using MinecraftHost.Services.Interfaces.Server;
using MinecraftHost.Services.Jobs;
using MinecraftHost.Services.Logging;
using MinecraftHost.Services.Net;
using MinecraftHost.Services.Notifications;
using MinecraftHost.Settings.Configuration;
using MinecraftHost.ViewModels.Windows;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using YukkuriMovieMaker.Commons;

namespace MinecraftHost.ViewModels.Items;

public class ServerItemViewModel : Bindable, IDisposable
{
    private readonly MinecraftServerConfig _config;
    private readonly IServerManager _serverManager;
    private readonly IJavaResolver _javaResolver;
    private readonly IVersionResolver _versionResolver;
    private readonly IServerProcessFactory _serverProcessFactory;
    private readonly IServerMonitoringService _serverMonitoringService;
    private readonly IStructuredLogService _structuredLogService;
    private readonly IAuditTrailService _auditTrailService;
    private readonly IAuthorizationService _authorizationService;
    private readonly IJobOrchestratorService _jobOrchestratorService;
    private readonly PlayerRosterCoordinator _playerRosterCoordinator;
    private readonly ServerMetricsCoordinator _metricsCoordinator;
    private readonly IGlobalIpResolver _globalIpResolver;
    private readonly IPortAvailabilityChecker _portAvailabilityChecker;
    private readonly IToastNotificationService _toastNotificationService;

    private IServerProcess? _process;
    private IServerPerformanceMonitor? _monitor;
    private CancellationTokenSource? _autoRestartCts;
    private bool _manualStopRequested;
    private int _restartAttempt;
    private bool _eulaAgreementRequired;
    private bool _toastNotifiedForCurrentRun;
    private readonly List<string> _versionCatalog = [];
    private const int VersionChunkSize = 80;
    private bool _isLoadingMoreVersions;

    public MinecraftServerConfig Config => _config;

    public string Name
    {
        get => _config.Name;
        set
        {
            var before = _config.Name;
            _config.Name = value;
            OnPropertyChanged();
            RecordConfigChange(nameof(Name), before, value);
        }
    }

    public ServerType ServerType
    {
        get => _config.ServerType;
        set
        {
            var before = _config.ServerType.ToString();
            _config.ServerType = value;
            if (value == ServerType.Bedrock && _config.Port == 25565)
                Port = 19132;
            else if (value != ServerType.Bedrock && _config.Port == 19132)
                Port = 25565;
            BuildIdentifier = string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsBedrockServer));
            OnPropertyChanged(nameof(IsPaperServer));
            OnPropertyChanged(nameof(IsModSupportedServer));
            ManagePluginsCommand.RaiseCanExecuteChanged();
            ManageModsCommand.RaiseCanExecuteChanged();
            _ = LoadVersionsAsync();
            RecordConfigChange(nameof(ServerType), before, value.ToString());
        }
    }

    public string Version
    {
        get => _config.Version;
        set
        {
            var before = _config.Version;
            _config.Version = value;
            BuildIdentifier = string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsUpdateAvailable));
            OnPropertyChanged(nameof(UpdateMessage));
            OnPropertyChanged(nameof(UpdateTargetDisplay));
            UseLatestVersionCommand.RaiseCanExecuteChanged();
            _ = LoadBuildIdentifiersAsync();
            RecordConfigChange(nameof(Version), before, value);
        }
    }

    public string BuildIdentifier
    {
        get => _config.BuildIdentifier;
        set
        {
            if (_config.BuildIdentifier == value)
                return;

            var before = _config.BuildIdentifier;
            _config.BuildIdentifier = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentBuildDisplay));
            OnPropertyChanged(nameof(SelectedBuildIdentifier));
            OnPropertyChanged(nameof(IsUpdateAvailable));
            OnPropertyChanged(nameof(UpdateMessage));
            OnPropertyChanged(nameof(UpdateTargetDisplay));
            UseLatestVersionCommand.RaiseCanExecuteChanged();
            SelectBuildCommand.RaiseCanExecuteChanged();
            RecordConfigChange(nameof(BuildIdentifier), before, value);
        }
    }

    public string? SelectedBuildIdentifier
    {
        get => string.IsNullOrWhiteSpace(BuildIdentifier) ? null : BuildIdentifier;
        set => BuildIdentifier = value ?? string.Empty;
    }

    public string CurrentBuildDisplay => string.IsNullOrWhiteSpace(BuildIdentifier)
        ? Texts.ServerItem_CurrentBuildUnpinned
        : string.Format(Texts.ServerItem_CurrentBuildPinnedFormat, BuildIdentifier);

    public int MaxMemoryMB
    {
        get => _config.MaxMemoryMB;
        set
        {
            var before = _config.MaxMemoryMB;
            _config.MaxMemoryMB = value;
            OnPropertyChanged();
            RecordConfigChange(nameof(MaxMemoryMB), before.ToString(), value.ToString());
        }
    }

    public int Port
    {
        get => _config.Port;
        set
        {
            var before = _config.Port;
            _config.Port = value;
            OnPropertyChanged();
            RecordConfigChange(nameof(Port), before.ToString(), value.ToString());
        }
    }

    private ObservableCollection<string> _availableVersions = new();
    public ObservableCollection<string> AvailableVersions
    {
        get => _availableVersions;
        private set => Set(ref _availableVersions, value);
    }

    public ObservableCollection<string> AvailableBuildIdentifiers { get; } = new();
    public bool HasMoreVersions => AvailableVersions.Count < _versionCatalog.Count;

    private string? _latestVersion;
    public string? LatestVersion
    {
        get => _latestVersion;
        private set
        {
            Set(ref _latestVersion, value);
            OnPropertyChanged(nameof(IsUpdateAvailable));
            OnPropertyChanged(nameof(UpdateMessage));
            OnPropertyChanged(nameof(UpdateTargetDisplay));
            UseLatestVersionCommand.RaiseCanExecuteChanged();
            SelectBuildCommand.RaiseCanExecuteChanged();
        }
    }

    private string? _latestBuildIdentifier;
    public string? LatestBuildIdentifier
    {
        get => _latestBuildIdentifier;
        private set
        {
            Set(ref _latestBuildIdentifier, value);
            OnPropertyChanged(nameof(IsUpdateAvailable));
            OnPropertyChanged(nameof(UpdateMessage));
            OnPropertyChanged(nameof(UpdateTargetDisplay));
            UseLatestVersionCommand.RaiseCanExecuteChanged();
        }
    }

    private bool IsVersionUpdateAvailable => LatestVersion is not null && Version != LatestVersion;

    private bool IsBuildUpdateAvailable =>
        _config.ServerType == ServerType.Paper &&
        string.Equals(Version, LatestVersion, StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(LatestBuildIdentifier) &&
        !string.Equals(BuildIdentifier, LatestBuildIdentifier, StringComparison.Ordinal);

    public bool IsUpdateAvailable => IsVersionUpdateAvailable || IsBuildUpdateAvailable;

    public string UpdateTargetDisplay => IsVersionUpdateAvailable
        ? LatestVersion ?? string.Empty
        : IsBuildUpdateAvailable ? string.Format(Texts.ServerItem_UpdateTargetBuildFormat, LatestBuildIdentifier) : string.Empty;

    public string UpdateMessage => IsVersionUpdateAvailable
        ? string.Format(Texts.ServerItem_UpdateMessageLatestVersionFormat, LatestVersion)
        : IsBuildUpdateAvailable ? string.Format(Texts.ServerItem_UpdateMessageLatestBuildFormat, LatestBuildIdentifier) : string.Empty;

    public bool IsBedrockServer => _config.ServerType == ServerType.Bedrock;
    public bool IsPaperServer => _config.ServerType == ServerType.Paper;
    public bool IsModSupportedServer => _config.ServerType is ServerType.Forge or ServerType.Fabric;
    public bool IsBuildSelectionAvailable => IsPaperServer && AvailableBuildIdentifiers.Count > 0;

    private bool _isLoadingVersions;
    public bool IsLoadingVersions
    {
        get => _isLoadingVersions;
        private set => Set(ref _isLoadingVersions, value);
    }

    private bool _isDownloading;
    public bool IsDownloading
    {
        get => _isDownloading;
        private set
        {
            Set(ref _isDownloading, value);
            OnPropertyChanged(nameof(IsIdle));
            RefreshCommands();
        }
    }

    private double _downloadProgress;
    public double DownloadProgress
    {
        get => _downloadProgress;
        private set => Set(ref _downloadProgress, value);
    }

    private string _downloadStatusText = Texts.ServerItem_DownloadStatusCheckingServerFile;
    public string DownloadStatusText
    {
        get => _downloadStatusText;
        private set => Set(ref _downloadStatusText, value);
    }

    private string _cpuUsage = "0.0%";
    public string CpuUsage
    {
        get => _cpuUsage;
        private set => Set(ref _cpuUsage, value);
    }

    private string _memoryUsage = "0 MB";
    public string MemoryUsage
    {
        get => _memoryUsage;
        private set => Set(ref _memoryUsage, value);
    }

    public bool IsRunning => _process is not null;
    public bool IsNotRunning => !IsRunning;
    public bool IsIdle => !IsRunning && !IsDownloading;

    public int PlayerCount => Players.Count;

    public ObservableCollection<ColoredConsoleLine> ConsoleLines { get; }
    public ObservableCollection<PlayerViewModel> Players { get; } = new();

    public ActionCommand StartCommand { get; }
    public ActionCommand StopCommand { get; }
    public ActionCommand OpenFolderCommand { get; }
    public ActionCommand EditFilesCommand { get; }
    public ActionCommand UseLatestVersionCommand { get; }
    public ActionCommand SelectBuildCommand { get; }
    public ActionCommand ManagePluginsCommand { get; }
    public ActionCommand ManageModsCommand { get; }
    public ActionCommand LoadMoreVersionsCommand { get; }

    private string _lastAuthorizationDeniedReason = string.Empty;
    public string LastAuthorizationDeniedReason
    {
        get => _lastAuthorizationDeniedReason;
        private set => Set(ref _lastAuthorizationDeniedReason, value);
    }

    public string StartDeniedReason => GetDeniedReason(OperationType.StartServer);
    public string StopDeniedReason => GetDeniedReason(OperationType.StopServer);
    public string EditFilesDeniedReason => GetDeniedReason(OperationType.EditServerFiles);
    public string ManagePluginsDeniedReason => GetDeniedReason(OperationType.ManagePlugins);
    public string ManageModsDeniedReason => GetDeniedReason(OperationType.ManageMods);
    public string SendCommandDeniedReason => GetDeniedReason(OperationType.SendConsoleCommand);
    public string UpdateBuildDeniedReason => GetDeniedReason(OperationType.UpdateServerBuild);

    private string _currentCommandInput = string.Empty;
    public string CurrentCommandInput
    {
        get => _currentCommandInput;
        set => Set(ref _currentCommandInput, value);
    }

    public ActionCommand SendCommandCommand { get; }

    private string _eulaGuidanceMessage = string.Empty;
    public string EulaGuidanceMessage
    {
        get => _eulaGuidanceMessage;
        private set
        {
            Set(ref _eulaGuidanceMessage, value);
            OnPropertyChanged(nameof(HasEulaGuidance));
        }
    }

    public bool HasEulaGuidance => !string.IsNullOrWhiteSpace(EulaGuidanceMessage);

    public ServerItemViewModel(
        MinecraftServerConfig config,
        IServerManager serverManager,
        IJavaResolver javaResolver,
        IVersionResolver versionResolver,
        IServerProcessFactory serverProcessFactory,
        IServerMonitoringService serverMonitoringService)
        : this(
            config,
            serverManager,
            javaResolver,
            versionResolver,
            serverProcessFactory,
            serverMonitoringService,
            StructuredLogServiceProvider.Instance,
            AuditTrailServiceProvider.Instance,
            AuthorizationServiceProvider.Instance,
            JobOrchestratorServiceProvider.Instance)
    {
    }

    public ServerItemViewModel(
        MinecraftServerConfig config,
        IServerManager serverManager,
        IJavaResolver javaResolver,
        IVersionResolver versionResolver,
        IServerProcessFactory serverProcessFactory,
        IServerMonitoringService serverMonitoringService,
        IStructuredLogService structuredLogService,
        IAuditTrailService auditTrailService,
        IAuthorizationService authorizationService,
        IJobOrchestratorService jobOrchestratorService)
    {
        _config = config;
        _serverManager = serverManager;
        _javaResolver = javaResolver;
        _versionResolver = versionResolver;
        _serverProcessFactory = serverProcessFactory;
        _serverMonitoringService = serverMonitoringService;
        _structuredLogService = structuredLogService;
        _auditTrailService = auditTrailService;
        _authorizationService = authorizationService;
        _jobOrchestratorService = jobOrchestratorService;
        _globalIpResolver = new GlobalIpResolver(HttpClientProvider.Client);
        _portAvailabilityChecker = new PortAvailabilityChecker();
        _toastNotificationService = new ToastNotificationService();

        _playerRosterCoordinator = new PlayerRosterCoordinator(Players);
        _metricsCoordinator = new ServerMetricsCoordinator(() => _process, () => _monitor, (cpu, memory) =>
        {
            CpuUsage = cpu;
            MemoryUsage = memory;
        });
        ConsoleLines = new BoundedObservableCollection<ColoredConsoleLine>(Math.Clamp(MinecraftHostSettings.Default.MaxConsoleLines, 200, 20000));

        StartCommand = new ActionCommand(_ => IsIdle && CanExecute(OperationType.StartServer), _ => _ = StartServerAsync());
        StopCommand = new ActionCommand(_ => IsRunning && CanExecute(OperationType.StopServer), _ => StopServer());
        OpenFolderCommand = new ActionCommand(_ => true, _ => OpenFolder());
        EditFilesCommand = new ActionCommand(_ => CanExecute(OperationType.EditServerFiles), _ => EditFiles());
        SendCommandCommand = new ActionCommand(
            _ => IsRunning && !string.IsNullOrWhiteSpace(CurrentCommandInput) && CanExecute(OperationType.SendConsoleCommand),
            _ => SendConsoleCommandAsync());
        UseLatestVersionCommand = new ActionCommand(
            _ => IsUpdateAvailable && IsNotRunning && CanExecute(OperationType.UpdateServerBuild),
            _ => _ = UseLatestVersionAsync());
        SelectBuildCommand = new ActionCommand(
            _ => IsBuildSelectionAvailable && IsNotRunning && CanExecute(OperationType.UpdateServerBuild),
            _ => OpenBuildSelection());
        ManagePluginsCommand = new ActionCommand(
            _ => IsPaperServer && CanExecute(OperationType.ManagePlugins),
            _ => ManagePlugins());
        ManageModsCommand = new ActionCommand(
            _ => IsModSupportedServer && CanExecute(OperationType.ManageMods),
            _ => ManageMods());
        LoadMoreVersionsCommand = new ActionCommand(
            _ => HasMoreVersions && !_isLoadingMoreVersions,
            _ => _ = LoadMoreVersionsAsync());

        Players.CollectionChanged += (s, e) => OnPropertyChanged(nameof(PlayerCount));

        _ = LoadVersionsAsync();
    }

    private async Task UseLatestVersionAsync()
    {
        if (!EnsureAuthorized(OperationType.UpdateServerBuild))
            return;

        await _auditTrailService.RecordEventAsync(nameof(ServerItemViewModel), "UseLatestVersion", _config.Id, $"version={Version}");

        if (LatestVersion is not null)
            Version = LatestVersion;

        await LoadBuildIdentifiersAsync();

        if (!string.IsNullOrWhiteSpace(LatestBuildIdentifier))
            BuildIdentifier = LatestBuildIdentifier;

        OnPropertyChanged(nameof(IsUpdateAvailable));
        OnPropertyChanged(nameof(UpdateMessage));
        OnPropertyChanged(nameof(UpdateTargetDisplay));
        UseLatestVersionCommand.RaiseCanExecuteChanged();
    }

    private async Task LoadVersionsAsync()
    {
        IsLoadingVersions = true;
        try
        {
            var versions = await _versionResolver.GetAvailableVersionsAsync(_config.ServerType);
            _versionCatalog.Clear();
            _versionCatalog.AddRange(versions);
            AvailableVersions.Clear();
            LatestVersion = _versionCatalog.Count > 0 ? _versionCatalog[0] : null;
            OnPropertyChanged(nameof(HasMoreVersions));
            await LoadMoreVersionsAsync();

            if (LatestVersion != null && (string.IsNullOrWhiteSpace(_config.Version) || !_versionCatalog.Contains(_config.Version)))
            {
                Version = LatestVersion;
            }
            else
            {
                await LoadBuildIdentifiersAsync();
            }
        }
        catch (Exception ex)
        {
            _versionCatalog.Clear();
            AvailableVersions.Clear();
            OnPropertyChanged(nameof(HasMoreVersions));
            AddConsoleLine(string.Format(Texts.ServerItem_LoadVersionsFailedFormat, ex.Message));
        }
        finally
        {
            IsLoadingVersions = false;
            LoadMoreVersionsCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task LoadMoreVersionsAsync()
    {
        if (_isLoadingMoreVersions || !HasMoreVersions)
            return;

        _isLoadingMoreVersions = true;
        try
        {
            await Task.Yield();
            var start = AvailableVersions.Count;
            var count = Math.Min(VersionChunkSize, _versionCatalog.Count - start);
            for (var i = 0; i < count; i++)
                AvailableVersions.Add(_versionCatalog[start + i]);
            OnPropertyChanged(nameof(HasMoreVersions));
        }
        finally
        {
            _isLoadingMoreVersions = false;
            LoadMoreVersionsCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task StartServerAsync()
    {
        if (!EnsureAuthorized(OperationType.StartServer))
            return;

        if (!IsIdle) return;

        try
        {
            await _auditTrailService.RecordEventAsync(nameof(ServerItemViewModel), "StartRequested", _config.Id, $"version={Version},build={BuildIdentifier}");
            _manualStopRequested = false;
            _autoRestartCts?.Cancel();
            _autoRestartCts?.Dispose();
            _autoRestartCts = null;
            Players.Clear();
            _eulaAgreementRequired = false;
            _toastNotifiedForCurrentRun = false;
            EulaGuidanceMessage = string.Empty;

            if (_config.ServerType == ServerType.Bedrock && !MinecraftHostSettings.Default.IsGlobalEulaAgreed)
            {
                var window = new Views.EulaAgreementWindow
                {
                    Owner = Application.Current.MainWindow
                };
                if (window.ShowDialog() != true)
                {
                    OnPropertyChanged(nameof(IsRunning));
                    OnPropertyChanged(nameof(IsNotRunning));
                    OnPropertyChanged(nameof(IsIdle));
                    RefreshCommands();
                    return;
                }

                MinecraftHostSettings.Default.IsGlobalEulaAgreed = true;
                MinecraftHostSettings.Default.Save();
            }

            AddConsoleLine(Texts.ServerItem_CheckingJavaInstallation);
            _structuredLogService.Log(StructuredLogLevel.Information, nameof(ServerItemViewModel), Texts.ServerItem_LogStartSequenceBegin, "StartServer", _config.Id);
            var javaPath = await _jobOrchestratorService.ExecuteAsync("ResolveJava", _config.Id, _ => _javaResolver.ResolveJavaAsync(_config), 3);
            AddConsoleLine(string.Format(Texts.ServerItem_JavaPathFormat, javaPath));

            IsDownloading = true;
            DownloadProgress = 0;
            DownloadStatusText = Texts.ServerItem_DownloadStatusCheckingServerFile;
            AddConsoleLine(Texts.ServerItem_CheckingServerFile);

            var progress = new Progress<double>(UpdateDownloadProgress);
            var jarPath = await _jobOrchestratorService.ExecuteAsync("EnsureServerJar", _config.Id, _ => _serverManager.EnsureServerJarAsync(_config, javaPath, progress), 3);

            IsDownloading = false;
            DownloadProgress = 0;
            DownloadStatusText = string.Empty;

            await _jobOrchestratorService.ExecuteAsync("WriteServerProperties", _config.Id, _ => _serverManager.WriteServerPropertiesAsync(_config), 2);

            var dir = _serverManager.GetServerDirectory(_config);
            AddConsoleLine(string.Format(Texts.ServerItem_StartingServerFormat, jarPath));

            _process = _serverProcessFactory.Create(javaPath, jarPath, _config.MaxMemoryMB, dir, _config.Port);
            _process.OutputReceived += OnProcessOutput;
            _process.ErrorReceived += OnProcessOutput;
            _process.Exited += OnProcessExited;
            _process.Start();

            _monitor = _serverMonitoringService.CreateMonitor(_process);
            _monitor.PerformanceUpdated += (s, e) =>
            {
                _metricsCoordinator.Refresh();
            };
            _metricsCoordinator.Start();
            _metricsCoordinator.Refresh();

            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsNotRunning));
            OnPropertyChanged(nameof(IsIdle));
            RefreshCommands();
        }
        catch (Exception ex)
        {
            _structuredLogService.Log(StructuredLogLevel.Error, nameof(ServerItemViewModel), Texts.ServerItem_LogStartFailed, "StartServer", _config.Id, ex);
            AddConsoleLine(string.Format(Texts.ServerItem_StartErrorFormat, ex.Message));
            IsDownloading = false;
            DownloadStatusText = string.Empty;
            DownloadProgress = 0;
            _metricsCoordinator.Stop();
            _process?.Dispose();
            _process = null;
            CpuUsage = "0.0%";
            MemoryUsage = "0 MB";
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsNotRunning));
            OnPropertyChanged(nameof(IsIdle));
            RefreshCommands();
        }
    }

    private async void StopServer()
    {
        if (!EnsureAuthorized(OperationType.StopServer))
            return;

        if (_process is not null)
        {
            var jobName = string.Format(Texts.Job_Server_Stop, _config.Name);
            await _jobOrchestratorService.ExecuteAsync(jobName, _config.Id.ToString(), async (cancellationToken) =>
            {
                await _auditTrailService.RecordEventAsync(nameof(ServerItemViewModel), "StopRequested", _config.Id, $"version={Version},build={BuildIdentifier}");
                _manualStopRequested = true;
                _structuredLogService.Log(StructuredLogLevel.Information, nameof(ServerItemViewModel), Texts.ServerItem_LogStopRequested, "StopServer", _config.Id);
                _autoRestartCts?.Cancel();
                Application.Current.Dispatcher.Invoke(() => AddConsoleLine(Texts.ServerItem_StoppingServer));
                await _process.StopAsync();
            });
        }
    }

    private async void SendConsoleCommandAsync()
    {
        if (!EnsureAuthorized(OperationType.SendConsoleCommand))
            return;

        if (_process is null || string.IsNullOrWhiteSpace(CurrentCommandInput)) return;
        var cmd = CurrentCommandInput;
        CurrentCommandInput = string.Empty;
        try
        {
            await _process.SendCommandAsync(cmd);
        }
        catch (Exception ex)
        {
            AddConsoleLine(string.Format(Texts.ServerItem_SendCommandFailedFormat, ex.Message));
        }
    }

    private void OpenFolder()
    {
        var dir = _serverManager.GetServerDirectory(_config);
        if (Directory.Exists(dir))
            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
    }

    private void EditFiles()
    {
        if (!EnsureAuthorized(OperationType.EditServerFiles))
            return;

        var dir = _serverManager.GetServerDirectory(_config);
        var vm = new ServerFileEditorViewModel(dir, _config.ServerType);
        var window = new Views.ServerFileEditorWindow
        {
            DataContext = vm,
            Owner = Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    private void OpenBuildSelection()
    {
        if (!EnsureAuthorized(OperationType.UpdateServerBuild))
            return;

        if (!IsBuildSelectionAvailable)
            return;

        var window = new Views.BuildSelectionWindow(AvailableBuildIdentifiers, SelectedBuildIdentifier)
        {
            Owner = Application.Current.MainWindow
        };

        if (window.ShowDialog() == true && !string.IsNullOrWhiteSpace(window.SelectedBuild))
            SelectedBuildIdentifier = window.SelectedBuild;
    }

    private void ManagePlugins()
    {
        if (!EnsureAuthorized(OperationType.ManagePlugins))
            return;

        var dir = _serverManager.GetServerDirectory(_config);
        var vm = new PluginManagerViewModel(dir, _config.Id.ToString(), IsRunning, _jobOrchestratorService, _structuredLogService);
        var window = new Views.PluginManagerWindow
        {
            DataContext = vm,
            Owner = Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    private void ManageMods()
    {
        if (!EnsureAuthorized(OperationType.ManageMods))
            return;

        var dir = _serverManager.GetServerDirectory(_config);
        var vm = new ModManagerViewModel(dir, _config.Id.ToString(), IsRunning, _jobOrchestratorService, _structuredLogService);
        var window = new Views.ModManagerWindow
        {
            DataContext = vm,
            Owner = Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    private void OnProcessOutput(object? sender, string e)
    {
        UpdateEulaGuidance(e);
        AddConsoleLine(e);
        _playerRosterCoordinator.ProcessLine(e);
        CheckServerStarted(e);
    }

    private void CheckServerStarted(string line)
    {
        if (_toastNotifiedForCurrentRun)
            return;

        if (line.Contains("Done (") || line.Contains("Server started."))
        {
            _toastNotifiedForCurrentRun = true;
            _ = Task.Run(async () =>
            {
                var (ipv4, ipv6) = await _globalIpResolver.ResolveAsync();
                if (string.IsNullOrWhiteSpace(ipv4) && string.IsNullOrWhiteSpace(ipv6))
                    return;

                var ipToCheck = !string.IsNullOrWhiteSpace(ipv4) ? ipv4 : (ipv6 ?? "127.0.0.1");
                var isOpen = await _portAvailabilityChecker.IsAvailableAsync(ipToCheck, _config.Port, _config.ServerType);
                if (isOpen)
                {
                    _toastNotificationService.ShowServerStartedToast(ipv4, ipv6, _config.Port);
                }
            });
        }
    }

    private void UpdateEulaGuidance(string line)
    {
        if (!EulaGuidanceDetector.TryGetMessage(line, out var message))
            return;

        _eulaAgreementRequired = true;
        if (Application.Current.Dispatcher.CheckAccess())
            EulaGuidanceMessage = message;
        else
            Application.Current.Dispatcher.BeginInvoke(new Action(() => EulaGuidanceMessage = message));
    }

    private async Task LoadBuildIdentifiersAsync()
    {
        AvailableBuildIdentifiers.Clear();

        if (_config.ServerType != ServerType.Paper || string.IsNullOrWhiteSpace(_config.Version))
        {
            LatestBuildIdentifier = null;
            BuildIdentifier = string.Empty;
            OnPropertyChanged(nameof(IsBuildSelectionAvailable));
            return;
        }

        try
        {
            var buildIdentifiers = await _versionResolver.GetAvailableBuildIdentifiersAsync(_config.ServerType, _config.Version);
            foreach (var buildIdentifier in buildIdentifiers)
                AvailableBuildIdentifiers.Add(buildIdentifier);

            LatestBuildIdentifier = buildIdentifiers.Count > 0 ? buildIdentifiers[0] : null;

            if (!string.IsNullOrWhiteSpace(BuildIdentifier) && buildIdentifiers.Contains(BuildIdentifier))
            {
                SelectedBuildIdentifier = BuildIdentifier;
            }
            else if (!string.IsNullOrWhiteSpace(LatestBuildIdentifier))
            {
                SelectedBuildIdentifier = LatestBuildIdentifier;
            }
            else
            {
                SelectedBuildIdentifier = null;
            }
        }
        catch
        {
            LatestBuildIdentifier = null;
            SelectedBuildIdentifier = null;
        }

        OnPropertyChanged(nameof(IsBuildSelectionAvailable));
        OnPropertyChanged(nameof(SelectedBuildIdentifier));
        OnPropertyChanged(nameof(IsUpdateAvailable));
        OnPropertyChanged(nameof(UpdateMessage));
        OnPropertyChanged(nameof(UpdateTargetDisplay));
        UseLatestVersionCommand.RaiseCanExecuteChanged();
        SelectBuildCommand.RaiseCanExecuteChanged();
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            _ = _auditTrailService.RecordEventAsync(nameof(ServerItemViewModel), "ProcessExited", _config.Id, $"version={Version},build={BuildIdentifier}");
            Players.Clear();
            AddConsoleLine(Texts.ServerItem_ServerProcessExited);
            _monitor?.Dispose();
            _monitor = null;
            _process?.Dispose();
            _process = null;
            _metricsCoordinator.Stop();
            CpuUsage = "0.0%";
            MemoryUsage = "0 MB";
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsNotRunning));
            OnPropertyChanged(nameof(IsIdle));
            RefreshCommands();

            TryScheduleAutoRestart();
        }));
    }

    private void TryScheduleAutoRestart()
    {
        var settings = MinecraftHostSettings.Default;
        if (!settings.EnableAutoRestart || _manualStopRequested)
        {
            _restartAttempt = 0;
            return;
        }

        if (_eulaAgreementRequired)
        {
            _restartAttempt = 0;
            AddConsoleLine(Texts.ServerItem_AutoRestartSkippedWaitingEula);
            return;
        }

        if (_restartAttempt >= settings.AutoRestartMaxAttempts)
        {
            AddConsoleLine(Texts.ServerItem_AutoRestartLimitReached);
            _restartAttempt = 0;
            return;
        }

        _restartAttempt++;
        _autoRestartCts?.Cancel();
        _autoRestartCts?.Dispose();
        _autoRestartCts = new CancellationTokenSource();
        var token = _autoRestartCts.Token;
        var delay = TimeSpan.FromSeconds(Math.Clamp(settings.AutoRestartDelaySeconds, 1, 300));

        _ = Task.Run(async () =>
        {
            try
            {
                _structuredLogService.Log(StructuredLogLevel.Warning, nameof(ServerItemViewModel), string.Format(Texts.ServerItem_LogAutoRestartScheduledFormat, _restartAttempt, settings.AutoRestartMaxAttempts), "AutoRestartSchedule", _config.Id);
                AddConsoleLine(string.Format(Texts.ServerItem_AutoRestartScheduledFormat, delay.TotalSeconds, _restartAttempt, settings.AutoRestartMaxAttempts));
                await Task.Delay(delay, token);
                if (token.IsCancellationRequested) return;

                await Application.Current.Dispatcher.InvokeAsync(async () => await StartServerAsync());
            }
            catch (OperationCanceledException)
            {
            }
        }, token);
    }

    private void AddConsoleLine(string raw)
    {
        var colored = ConsoleLineColorizer.Colorize(raw);
        if (Application.Current.Dispatcher.CheckAccess())
        {
            ConsoleLines.Add(colored);
        }
        else
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() => AddConsoleLine(raw)));
        }
    }

    private void RefreshCommands()
    {
        StartCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        SendCommandCommand.RaiseCanExecuteChanged();
        UseLatestVersionCommand.RaiseCanExecuteChanged();
        SelectBuildCommand.RaiseCanExecuteChanged();
        ManagePluginsCommand.RaiseCanExecuteChanged();
        ManageModsCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(StartDeniedReason));
        OnPropertyChanged(nameof(StopDeniedReason));
        OnPropertyChanged(nameof(EditFilesDeniedReason));
        OnPropertyChanged(nameof(ManagePluginsDeniedReason));
        OnPropertyChanged(nameof(ManageModsDeniedReason));
        OnPropertyChanged(nameof(SendCommandDeniedReason));
        OnPropertyChanged(nameof(UpdateBuildDeniedReason));
    }

    public void RefreshAuthorizationState()
    {
        RefreshCommands();
    }

    public void Dispose()
    {
        _manualStopRequested = true;
        _autoRestartCts?.Cancel();
        _autoRestartCts?.Dispose();
        _metricsCoordinator.Dispose();
        _monitor?.Dispose();
        _process?.Dispose();
    }

    private void UpdateDownloadProgress(double progress)
    {
        var normalized = Math.Clamp(progress, 0, 100);
        DownloadProgress = normalized;

        if (normalized <= 0.1)
            DownloadStatusText = Texts.ServerItem_DownloadStatusCheckingServerFile;
        else if (normalized < 40)
            DownloadStatusText = Texts.ServerItem_DownloadStatusCreatingBackup;
        else if (normalized < 100)
            DownloadStatusText = Texts.ServerItem_DownloadStatusDownloading;
        else
            DownloadStatusText = Texts.ServerItem_DownloadStatusVerifyingIntegrity;
    }

    private void RecordConfigChange(string property, string before, string after)
    {
        if (string.Equals(before, after, StringComparison.Ordinal))
            return;

        _ = _auditTrailService.RecordChangeAsync(nameof(ServerItemViewModel), "ConfigChanged", _config.Id, property, before, after);
    }

    private bool CanExecute(OperationType operationType)
    {
        return _authorizationService.Authorize(operationType, _config.Id).Allowed;
    }

    private string GetDeniedReason(OperationType operationType)
    {
        var decision = _authorizationService.Authorize(operationType, _config.Id);
        return decision.Allowed ? string.Empty : AuthorizationUiText.ToInline(decision.Reason);
    }

    private bool EnsureAuthorized(OperationType operationType)
    {
        var decision = _authorizationService.Authorize(operationType, _config.Id);
        if (decision.Allowed)
        {
            LastAuthorizationDeniedReason = string.Empty;
            return true;
        }

        LastAuthorizationDeniedReason = AuthorizationUiText.ToInline(decision.Reason);
        MessageBox.Show(AuthorizationUiText.ToDialog(decision.Reason), "MinecraftHost", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }
}