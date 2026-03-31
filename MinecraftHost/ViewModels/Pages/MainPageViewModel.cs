using MinecraftHost.Localization;
using MinecraftHost.Models.Authorization;
using MinecraftHost.Models.Server;
using MinecraftHost.Services;
using MinecraftHost.Services.Authorization;
using MinecraftHost.Services.Interfaces.Audit;
using MinecraftHost.Services.Interfaces.Authorization;
using MinecraftHost.Services.Interfaces.Jobs;
using MinecraftHost.Services.Interfaces.Logging;
using MinecraftHost.Services.Interfaces.Observability;
using MinecraftHost.Services.Interfaces.Scheduler;
using MinecraftHost.Services.Interfaces.Server;
using MinecraftHost.Services.Jobs;
using MinecraftHost.Services.Logging;
using MinecraftHost.Services.Observability;
using MinecraftHost.Services.Server;
using MinecraftHost.Settings.Configuration;
using MinecraftHost.ViewModels.Items;
using MinecraftHost.Views;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Plugin;

namespace MinecraftHost.ViewModels.Pages;

public class MainPageViewModel : Bindable, IToolViewModel, IDisposable
{
    private readonly IServerManager _serverManager;
    private readonly IJavaResolver _javaResolver;
    private readonly IVersionResolver _versionResolver;
    private readonly IServerProcessFactory _serverProcessFactory;
    private readonly IServerMonitoringService _serverMonitoringService;
    private readonly IStructuredLogService _structuredLogService;
    private readonly IAuditTrailService _auditTrailService;
    private readonly IAuthorizationService _authorizationService;
    private readonly IAuthorizationStateNotifier _authorizationStateNotifier;
    private readonly IJobOrchestratorService _jobOrchestratorService;
    private readonly ISchedulerService _schedulerService;
    private bool _isLoadingSettings;
    private static readonly HashSet<string> ServerConfigAutoSaveProperties =
    [
        nameof(ServerItemViewModel.Name),
        nameof(ServerItemViewModel.ServerType),
        nameof(ServerItemViewModel.Version),
        nameof(ServerItemViewModel.BuildIdentifier),
        nameof(ServerItemViewModel.MaxMemoryMB),
        nameof(ServerItemViewModel.Port)
    ];

    private string _createServerDeniedReason = string.Empty;
    public string CreateServerDeniedReason
    {
        get => _createServerDeniedReason;
        private set => Set(ref _createServerDeniedReason, value);
    }

    private string _deleteServerDeniedReason = string.Empty;
    public string DeleteServerDeniedReason
    {
        get => _deleteServerDeniedReason;
        private set => Set(ref _deleteServerDeniedReason, value);
    }

    private string _openOperationsCenterDeniedReason = string.Empty;
    public string OpenOperationsCenterDeniedReason
    {
        get => _openOperationsCenterDeniedReason;
        private set => Set(ref _openOperationsCenterDeniedReason, value);
    }

    private string _authorizationStatusMessage = string.Empty;
    public string AuthorizationStatusMessage
    {
        get => _authorizationStatusMessage;
        private set => Set(ref _authorizationStatusMessage, value);
    }

    public string Title => "MCHost";

    private bool _canSuspend = true;
    public bool CanSuspend
    {
        get => _canSuspend;
        set => Set(ref _canSuspend, value);
    }

    public ObservableCollection<ServerItemViewModel> Servers { get; } = new();

    private ServerItemViewModel? _selectedServer;
    public ServerItemViewModel? SelectedServer
    {
        get => _selectedServer;
        set
        {
            Set(ref _selectedServer, value);
            DeleteServerCommand.RaiseCanExecuteChanged();
            UpdateAuthorizationState();
        }
    }

    public ActionCommand CreateServerCommand { get; }
    public ActionCommand DeleteServerCommand { get; }
    public ActionCommand OpenOperationsCenterCommand { get; }
    public ActionCommand OpenSchedulerCommand { get; }

    public IEnumerable<ActionCommand> Commands => [
        CreateServerCommand,
        DeleteServerCommand,
        OpenOperationsCenterCommand,
        OpenSchedulerCommand
    ];

    public event EventHandler<CreateNewToolViewRequestedEventArgs>? CreateNewToolViewRequested { add { } remove { } }

    public MainPageViewModel()
        : this(
            new ServerManager(),
            new JavaResolver(),
            new VersionResolver(),
            new ServerProcessFactory(),
            new ServerMonitoringService(),
            StructuredLogServiceProvider.Instance,
            AuditTrailServiceProvider.Instance,
            AuthorizationServiceProvider.Instance,
            AuthorizationStateNotifierProvider.Instance,
            JobOrchestratorServiceProvider.Instance,
            MinecraftHost.Services.Scheduler.SchedulerServiceProvider.Instance)
    {
    }

    public MainPageViewModel(
        IServerManager serverManager,
        IJavaResolver javaResolver,
        IVersionResolver versionResolver,
        IServerProcessFactory serverProcessFactory,
        IServerMonitoringService serverMonitoringService,
        IStructuredLogService structuredLogService,
        IAuditTrailService auditTrailService,
        IAuthorizationService authorizationService,
        IAuthorizationStateNotifier authorizationStateNotifier,
        IJobOrchestratorService jobOrchestratorService,
        ISchedulerService schedulerService)
    {
        _serverManager = serverManager;
        _javaResolver = javaResolver;
        _versionResolver = versionResolver;
        _serverProcessFactory = serverProcessFactory;
        _serverMonitoringService = serverMonitoringService;
        _structuredLogService = structuredLogService;
        _auditTrailService = auditTrailService;
        _authorizationService = authorizationService;
        _authorizationStateNotifier = authorizationStateNotifier;
        _jobOrchestratorService = jobOrchestratorService;
        _schedulerService = schedulerService;

        _schedulerService.Start();

        CreateServerCommand = new ActionCommand(_ => CanCreateServer(), _ => CreateServer());
        DeleteServerCommand = new ActionCommand(_ => CanDeleteServer(), _ => DeleteServer());
        OpenOperationsCenterCommand = new ActionCommand(_ => CanOpenOperationsCenter(), _ => OpenOperationsCenter());
        OpenSchedulerCommand = new ActionCommand(_ => true, _ => OpenScheduler());

        Servers.CollectionChanged += Servers_CollectionChanged;
        _authorizationStateNotifier.StateChanged += OnAuthorizationStateChanged;

        LoadServersFromSettings();
        UpdateAuthorizationState();
    }

    private void OnAuthorizationStateChanged()
    {
        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            CreateServerCommand.RaiseCanExecuteChanged();
            DeleteServerCommand.RaiseCanExecuteChanged();
            OpenOperationsCenterCommand.RaiseCanExecuteChanged();

            foreach (var server in Servers)
                server.RefreshAuthorizationState();

            UpdateAuthorizationState();
        }));
    }

    private void Servers_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (ServerItemViewModel item in e.OldItems)
                item.PropertyChanged -= Server_PropertyChanged;
        }

        if (e.NewItems is not null)
        {
            foreach (ServerItemViewModel item in e.NewItems)
                item.PropertyChanged += Server_PropertyChanged;
        }

        UpdateCanSuspend();
        CreateServerCommand.RaiseCanExecuteChanged();
        DeleteServerCommand.RaiseCanExecuteChanged();
        OpenOperationsCenterCommand.RaiseCanExecuteChanged();
        UpdateAuthorizationState();
        if (!_isLoadingSettings)
            SaveServersToSettings();
    }

    private void Server_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ServerItemViewModel.IsRunning))
            UpdateCanSuspend();

        if (!_isLoadingSettings && !string.IsNullOrWhiteSpace(e.PropertyName) && ServerConfigAutoSaveProperties.Contains(e.PropertyName))
            SaveServersToSettings();

        DeleteServerCommand.RaiseCanExecuteChanged();
        UpdateAuthorizationState();
    }

    private void UpdateCanSuspend()
    {
        CanSuspend = Servers.All(s => !s.IsRunning);
    }

    private bool CanCreateServer()
    {
        return _authorizationService.Authorize(OperationType.CreateServer).Allowed;
    }

    private bool CanDeleteServer()
    {
        if (SelectedServer is null)
            return false;

        return _authorizationService.Authorize(OperationType.DeleteServer, SelectedServer.Config.Id).Allowed;
    }

    private bool CanOpenOperationsCenter()
    {
        return _authorizationService.Authorize(OperationType.ViewOperationsCenter).Allowed;
    }

    private void UpdateAuthorizationState()
    {
        var create = _authorizationService.Authorize(OperationType.CreateServer);
        CreateServerDeniedReason = create.Allowed ? string.Empty : AuthorizationUiText.ToInline(create.Reason);

        if (SelectedServer is null)
        {
            DeleteServerDeniedReason = Texts.MainPage_ServerNotSelected;
        }
        else
        {
            var delete = _authorizationService.Authorize(OperationType.DeleteServer, SelectedServer.Config.Id);
            DeleteServerDeniedReason = delete.Allowed ? string.Empty : AuthorizationUiText.ToInline(delete.Reason);
        }

        var operations = _authorizationService.Authorize(OperationType.ViewOperationsCenter);
        OpenOperationsCenterDeniedReason = operations.Allowed ? string.Empty : AuthorizationUiText.ToInline(operations.Reason);

        AuthorizationStatusMessage = string.Join(" / ", new[] { CreateServerDeniedReason, DeleteServerDeniedReason, OpenOperationsCenterDeniedReason }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private void LoadServersFromSettings()
    {
        var settings = MinecraftHostSettings.Default;
        settings.Servers ??= new ObservableCollection<MinecraftServerConfig>();

        _isLoadingSettings = true;
        try
        {
            foreach (var config in settings.Servers)
                Servers.Add(new ServerItemViewModel(config, _serverManager, _javaResolver, _versionResolver, _serverProcessFactory, _serverMonitoringService, _structuredLogService, _auditTrailService, _authorizationService, _jobOrchestratorService));

            if (Servers.Any())
                SelectedServer = Servers.First();
        }
        finally
        {
            _isLoadingSettings = false;
        }
    }

    private void SaveServersToSettings()
    {
        var settings = MinecraftHostSettings.Default;
        settings.Servers.Clear();
        foreach (var serverVm in Servers)
            settings.Servers.Add(serverVm.Config);
        settings.Save();
    }

    private void CreateServer()
    {
        var decision = _authorizationService.Authorize(OperationType.CreateServer);
        if (!decision.Allowed)
        {
            UpdateAuthorizationState();
            MessageBox.Show(AuthorizationUiText.ToDialog(decision.Reason), "MCHost", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var config = new MinecraftServerConfig { Name = Texts.MainPage_NewServerName, ServerType = ServerType.Paper };
        var vm = new ServerItemViewModel(config, _serverManager, _javaResolver, _versionResolver, _serverProcessFactory, _serverMonitoringService, _structuredLogService, _auditTrailService, _authorizationService, _jobOrchestratorService);
        Servers.Add(vm);
        SelectedServer = vm;

        _structuredLogService.Log(Models.Logging.StructuredLogLevel.Information, "MainPageViewModel", string.Format(Texts.Log_Server_Created, config.Name), "CreateServer", config.Id.ToString());
    }

    private void OpenOperationsCenter()
    {
        var decision = _authorizationService.Authorize(OperationType.ViewOperationsCenter);
        if (!decision.Allowed)
        {
            UpdateAuthorizationState();
            MessageBox.Show(AuthorizationUiText.ToDialog(decision.Reason), "MCHost", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var window = new OperationsCenterWindow
        {
            Owner = Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    private void OpenScheduler()
    {
        var window = new TaskSchedulerWindow
        {
            Owner = Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    private async void DeleteServer()
    {
        var target = SelectedServer;
        if (target is null) return;

        var decision = _authorizationService.Authorize(OperationType.DeleteServer, target.Config.Id);
        if (!decision.Allowed)
        {
            UpdateAuthorizationState();
            MessageBox.Show(AuthorizationUiText.ToDialog(decision.Reason), "MCHost", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (target.IsRunning)
        {
            MessageBox.Show(Texts.MainPage_DeleteAfterStopRequired, "MCHost", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            string.Format(Texts.MainPage_DeleteServerConfirmFormat, target.Name),
            "MCHost",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (confirm != MessageBoxResult.Yes)
            return;

        var directory = _serverManager.GetServerDirectory(target.Config);

        Servers.Remove(target);
        var targetId = target.Config.Id;
        var targetName = target.Name;
        target.Dispose();

        var jobName = string.Format(Texts.Job_Server_Delete, targetName);
        await _jobOrchestratorService.ExecuteAsync(jobName, targetId.ToString(), async (cancellationToken) =>
        {
            try
            {
                await Task.Run(() =>
                {
                    if (Directory.Exists(directory))
                        Directory.Delete(directory, recursive: true);
                });
                _structuredLogService.Log(Models.Logging.StructuredLogLevel.Information, "MainPageViewModel", string.Format(Texts.Log_Server_Deleted, targetName), "DeleteServer", targetId.ToString());
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show(string.Format(Texts.MainPage_DeleteServerDirectoryFailedFormat, ex.Message), "MCHost", MessageBoxButton.OK, MessageBoxImage.Warning);
                });
                _structuredLogService.Log(Models.Logging.StructuredLogLevel.Error, "MainPageViewModel", string.Format(Texts.MainPage_DeleteServerDirectoryFailedFormat, ex.Message), "DeleteServer", targetId.ToString(), ex);
                throw;
            }
        });
        SelectedServer = Servers.FirstOrDefault();
    }

    public void LoadState(ToolState stateData) { }

    public ToolState SaveState()
    {
        SaveServersToSettings();
        return new ToolState();
    }

    public void Dispose()
    {
        _authorizationStateNotifier.StateChanged -= OnAuthorizationStateChanged;
        _schedulerService.Stop();
        foreach (var server in Servers)
            server.Dispose();
    }
}