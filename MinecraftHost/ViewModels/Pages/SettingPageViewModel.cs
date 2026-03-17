using Microsoft.Win32;
using MinecraftHost.Localization;
using MinecraftHost.Models.Authorization;
using MinecraftHost.Services.Authorization;
using MinecraftHost.Services.Interfaces.Authorization;
using MinecraftHost.Settings.Configuration;
using MinecraftHost.ViewModels.Windows;
using MinecraftHost.Views;
using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using Windows.Security.Credentials.UI;
using YukkuriMovieMaker.Commons;

namespace MinecraftHost.ViewModels.Pages;

public class SettingPageViewModel : Bindable
{
    public sealed class ServerChoiceItem
    {
        public string Name { get; }
        public string Id { get; }

        public ServerChoiceItem(string name, string id)
        {
            Name = string.IsNullOrWhiteSpace(name) ? Texts.SettingPage_UnnamedServer : name;
            Id = id;
        }
    }

    private readonly IPolicyService _policyService;
    private readonly IAuthorizationService _authorizationService;
    private readonly IAuthorizationStateNotifier _authorizationStateNotifier;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private CancellationTokenSource? _autoSaveCts;
    private readonly object _autoSaveSync = new();
    private bool _isInitializing;
    private bool _suppressAutoSave;
    private const string PinHashPrefix = "PBKDF2-SHA256";
    private const int PinSaltSize = 16;
    private const int PinHashSize = 32;
    private const int PinIterations = 120000;

    private static readonly HashSet<string> AutoSaveTargets =
    [
        nameof(JavaPath),
        nameof(EnablePortForwarding),
        nameof(MaxConsoleLines),
        nameof(EnableAutoRestart),
        nameof(AutoRestartMaxAttempts),
        nameof(AutoRestartDelaySeconds),
        nameof(StructuredLogsDirectory),
        nameof(StructuredLogMaxFiles),
        nameof(StructuredLogMaxFileSizeMB),
        nameof(EnableSafeUpdateBackup),
        nameof(SafeUpdateBackupRetention),
        nameof(OperatorRole),
        nameof(MaintenanceMode),
        nameof(RequireRoleElevationAuthentication),
        nameof(PreferWindowsHelloForRoleElevation),
        nameof(CurrentRoleElevationPin),
        nameof(NewRoleElevationPin),
        nameof(ConfirmRoleElevationPin)
    ];

    private string _javaPath = string.Empty;
    public string JavaPath
    {
        get => _javaPath;
        set
        {
            Set(ref _javaPath, value);
            OnPropertyChanged(nameof(JavaPathStatus));
            OnPropertyChanged(nameof(IsJavaPathValid));
            ClearCommand?.RaiseCanExecuteChanged();
        }
    }

    public string JavaPathStatus
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_javaPath))
                return Texts.SettingPage_JavaPathStatusNotConfigured;
            if (File.Exists(_javaPath))
                return Texts.SettingPage_JavaPathStatusValid;
            return Texts.SettingPage_JavaPathStatusNotFound;
        }
    }

    public bool IsJavaPathValid => string.IsNullOrWhiteSpace(_javaPath) || File.Exists(_javaPath);

    private bool _enablePortForwarding;
    public bool EnablePortForwarding
    {
        get => _enablePortForwarding;
        set => Set(ref _enablePortForwarding, value);
    }

    private int _maxConsoleLines;
    public int MaxConsoleLines
    {
        get => _maxConsoleLines;
        set
        {
            var normalized = Math.Clamp(value, 200, 20000);
            Set(ref _maxConsoleLines, normalized);
        }
    }

    private bool _enableAutoRestart;
    public bool EnableAutoRestart
    {
        get => _enableAutoRestart;
        set => Set(ref _enableAutoRestart, value);
    }

    private int _autoRestartMaxAttempts;
    public int AutoRestartMaxAttempts
    {
        get => _autoRestartMaxAttempts;
        set
        {
            var normalized = Math.Clamp(value, 1, 20);
            Set(ref _autoRestartMaxAttempts, normalized);
        }
    }

    private int _autoRestartDelaySeconds;
    public int AutoRestartDelaySeconds
    {
        get => _autoRestartDelaySeconds;
        set
        {
            var normalized = Math.Clamp(value, 1, 300);
            Set(ref _autoRestartDelaySeconds, normalized);
        }
    }

    private string _structuredLogsDirectory = string.Empty;
    public string StructuredLogsDirectory
    {
        get => _structuredLogsDirectory;
        set => Set(ref _structuredLogsDirectory, value);
    }

    private int _structuredLogMaxFiles;
    public int StructuredLogMaxFiles
    {
        get => _structuredLogMaxFiles;
        set
        {
            var normalized = Math.Clamp(value, 1, 365);
            Set(ref _structuredLogMaxFiles, normalized);
        }
    }

    private int _structuredLogMaxFileSizeMB;
    public int StructuredLogMaxFileSizeMB
    {
        get => _structuredLogMaxFileSizeMB;
        set
        {
            var normalized = Math.Clamp(value, 1, 200);
            Set(ref _structuredLogMaxFileSizeMB, normalized);
        }
    }

    private bool _enableSafeUpdateBackup;
    public bool EnableSafeUpdateBackup
    {
        get => _enableSafeUpdateBackup;
        set => Set(ref _enableSafeUpdateBackup, value);
    }

    private int _safeUpdateBackupRetention;
    public int SafeUpdateBackupRetention
    {
        get => _safeUpdateBackupRetention;
        set
        {
            var normalized = Math.Clamp(value, 1, 30);
            Set(ref _safeUpdateBackupRetention, normalized);
        }
    }

    private string _operatorRole = "Administrator";
    public string OperatorRole
    {
        get => _operatorRole;
        set => Set(ref _operatorRole, value);
    }

    public string[] AvailableOperatorRoles { get; } = Enum.GetNames<OperatorRole>();

    public ObservableCollection<ServerChoiceItem> AvailableServers { get; } = [];
    public ObservableCollection<ServerChoiceItem> LockedServers { get; } = [];

    private ServerChoiceItem? _selectedServerToLock;
    public ServerChoiceItem? SelectedServerToLock
    {
        get => _selectedServerToLock;
        set
        {
            Set(ref _selectedServerToLock, value);
            AddLockedServerCommand.RaiseCanExecuteChanged();
        }
    }

    private ServerChoiceItem? _selectedLockedServer;
    public ServerChoiceItem? SelectedLockedServer
    {
        get => _selectedLockedServer;
        set
        {
            Set(ref _selectedLockedServer, value);
            RemoveLockedServerCommand.RaiseCanExecuteChanged();
        }
    }

    private bool _maintenanceMode;
    public bool MaintenanceMode
    {
        get => _maintenanceMode;
        set => Set(ref _maintenanceMode, value);
    }

    private bool _requireRoleElevationAuthentication;
    public bool RequireRoleElevationAuthentication
    {
        get => _requireRoleElevationAuthentication;
        set => Set(ref _requireRoleElevationAuthentication, value);
    }

    private bool _preferWindowsHelloForRoleElevation;
    public bool PreferWindowsHelloForRoleElevation
    {
        get => _preferWindowsHelloForRoleElevation;
        set => Set(ref _preferWindowsHelloForRoleElevation, value);
    }

    private string _currentRoleElevationPin = string.Empty;
    public string CurrentRoleElevationPin
    {
        get => _currentRoleElevationPin;
        set => Set(ref _currentRoleElevationPin, value);
    }

    private string _newRoleElevationPin = string.Empty;
    public string NewRoleElevationPin
    {
        get => _newRoleElevationPin;
        set => Set(ref _newRoleElevationPin, value);
    }

    private string _confirmRoleElevationPin = string.Empty;
    public string ConfirmRoleElevationPin
    {
        get => _confirmRoleElevationPin;
        set => Set(ref _confirmRoleElevationPin, value);
    }

    private string _authorizationStatus = string.Empty;
    public string AuthorizationStatus
    {
        get => _authorizationStatus;
        private set => Set(ref _authorizationStatus, value);
    }

    public string OpenAuditLogDeniedReason => GetDeniedReason(OperationType.ViewAuditLog);
    public string OpenOperationsCenterDeniedReason => GetDeniedReason(OperationType.ViewOperationsCenter);

    public ActionCommand BrowseCommand { get; }
    public ActionCommand ClearCommand { get; }
    public ActionCommand OpenStructuredLogViewerCommand { get; }
    public ActionCommand OpenAuditLogViewerCommand { get; }
    public ActionCommand OpenOperationsCenterCommand { get; }
    public ActionCommand AddLockedServerCommand { get; }
    public ActionCommand RemoveLockedServerCommand { get; }

    public SettingPageViewModel()
        : this(PolicyServiceProvider.Instance, AuthorizationServiceProvider.Instance, AuthorizationStateNotifierProvider.Instance)
    {
    }

    public SettingPageViewModel(IPolicyService policyService, IAuthorizationService authorizationService, IAuthorizationStateNotifier authorizationStateNotifier)
    {
        _isInitializing = true;
        _policyService = policyService;
        _authorizationService = authorizationService;
        _authorizationStateNotifier = authorizationStateNotifier;

        BrowseCommand = new ActionCommand(_ => true, _ => Browse());
        ClearCommand = new ActionCommand(_ => !string.IsNullOrWhiteSpace(JavaPath), _ => Clear());
        OpenStructuredLogViewerCommand = new ActionCommand(_ => true, _ => OpenStructuredLogViewer());
        OpenAuditLogViewerCommand = new ActionCommand(_ => _authorizationService.Authorize(OperationType.ViewAuditLog).Allowed, _ => OpenAuditLogViewer());
        OpenOperationsCenterCommand = new ActionCommand(_ => _authorizationService.Authorize(OperationType.ViewOperationsCenter).Allowed, _ => OpenOperationsCenter());
        AddLockedServerCommand = new ActionCommand(_ => SelectedServerToLock is not null, _ => AddLockedServer());
        RemoveLockedServerCommand = new ActionCommand(_ => SelectedLockedServer is not null, _ => RemoveLockedServer());

        JavaPath = MinecraftHostSettings.Default.JavaPath ?? string.Empty;
        _enablePortForwarding = MinecraftHostSettings.Default.EnablePortForwarding;
        _maxConsoleLines = Math.Clamp(MinecraftHostSettings.Default.MaxConsoleLines, 200, 20000);
        _enableAutoRestart = MinecraftHostSettings.Default.EnableAutoRestart;
        _autoRestartMaxAttempts = Math.Clamp(MinecraftHostSettings.Default.AutoRestartMaxAttempts, 1, 20);
        _autoRestartDelaySeconds = Math.Clamp(MinecraftHostSettings.Default.AutoRestartDelaySeconds, 1, 300);
        _structuredLogsDirectory = MinecraftHostSettings.Default.StructuredLogsDirectory ?? string.Empty;
        _structuredLogMaxFiles = Math.Clamp(MinecraftHostSettings.Default.StructuredLogMaxFiles, 1, 365);
        _structuredLogMaxFileSizeMB = Math.Clamp(MinecraftHostSettings.Default.StructuredLogMaxFileSizeMB, 1, 200);
        _enableSafeUpdateBackup = MinecraftHostSettings.Default.EnableSafeUpdateBackup;
        _safeUpdateBackupRetention = Math.Clamp(MinecraftHostSettings.Default.SafeUpdateBackupRetention, 1, 30);
        _operatorRole = MinecraftHostSettings.Default.OperatorRole;
        _maintenanceMode = _policyService.Current.MaintenanceMode;
        _requireRoleElevationAuthentication = MinecraftHostSettings.Default.RequireRoleElevationAuthentication;
        _preferWindowsHelloForRoleElevation = MinecraftHostSettings.Default.PreferWindowsHelloForRoleElevation;

        LoadServerChoices();
        LoadLockedServers();

        OnPropertyChanged(nameof(OpenAuditLogDeniedReason));
        OnPropertyChanged(nameof(OpenOperationsCenterDeniedReason));

        PropertyChanged += (_, e) => OnSettingPropertyChanged(e.PropertyName);
        _isInitializing = false;
    }

    private void Browse()
    {
        var dialog = new OpenFileDialog
        {
            Title = Texts.SettingPage_SelectJavaExeTitle,
            Filter = Texts.SettingPage_SelectJavaExeFilter,
            FileName = "java.exe"
        };

        if (dialog.ShowDialog() == true)
            JavaPath = dialog.FileName;
    }

    private void Clear()
    {
        JavaPath = string.Empty;
    }

    private void AddLockedServer()
    {
        if (SelectedServerToLock is null)
            return;

        if (LockedServers.All(x => !string.Equals(x.Id, SelectedServerToLock.Id, StringComparison.OrdinalIgnoreCase)))
            LockedServers.Add(new ServerChoiceItem(SelectedServerToLock.Name, SelectedServerToLock.Id));

        ScheduleAutoSave();
    }

    private void RemoveLockedServer()
    {
        if (SelectedLockedServer is null)
            return;

        LockedServers.Remove(SelectedLockedServer);
        SelectedLockedServer = null;

        ScheduleAutoSave();
    }

    private void OnSettingPropertyChanged(string? propertyName)
    {
        if (_isInitializing || _suppressAutoSave || string.IsNullOrWhiteSpace(propertyName))
            return;

        if (!AutoSaveTargets.Contains(propertyName))
            return;

        ScheduleAutoSave();
    }

    private async void ScheduleAutoSave()
    {
        CancellationTokenSource cts;
        lock (_autoSaveSync)
        {
            _autoSaveCts?.Cancel();
            _autoSaveCts?.Dispose();
            cts = new CancellationTokenSource();
            _autoSaveCts = cts;
        }

        try
        {
            await Task.Delay(250, cts.Token);
            await SaveAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task SaveAsync()
    {
        await _saveGate.WaitAsync();
        try
        {
            AuthorizationStatus = string.Empty;
            var currentRoleRaw = MinecraftHostSettings.Default.OperatorRole;
            var roleChanged = !string.Equals(currentRoleRaw, OperatorRole, StringComparison.OrdinalIgnoreCase);

            if (!ValidatePinUpdate(out var pinValidationError))
            {
                AuthorizationStatus = pinValidationError;
                if (roleChanged)
                    RevertRoleSelection(currentRoleRaw);
                return;
            }

            var currentRole = ParseRole(currentRoleRaw);
            var targetRole = ParseRole(OperatorRole);
            var requiresElevationAuth = targetRole > currentRole && RequireRoleElevationAuthentication;

            if (requiresElevationAuth)
            {
                var verified = await VerifyRoleElevation();
                if (!verified.Allowed)
                {
                    AuthorizationStatus = AuthorizationUiText.ToInline(verified.Reason);
                    if (roleChanged)
                        RevertRoleSelection(currentRoleRaw);
                    return;
                }
            }

            MinecraftHostSettings.Default.JavaPath = JavaPath;
            MinecraftHostSettings.Default.EnablePortForwarding = EnablePortForwarding;
            MinecraftHostSettings.Default.MaxConsoleLines = MaxConsoleLines;
            MinecraftHostSettings.Default.EnableAutoRestart = EnableAutoRestart;
            MinecraftHostSettings.Default.AutoRestartMaxAttempts = AutoRestartMaxAttempts;
            MinecraftHostSettings.Default.AutoRestartDelaySeconds = AutoRestartDelaySeconds;
            MinecraftHostSettings.Default.StructuredLogsDirectory = StructuredLogsDirectory;
            MinecraftHostSettings.Default.StructuredLogMaxFiles = StructuredLogMaxFiles;
            MinecraftHostSettings.Default.StructuredLogMaxFileSizeMB = StructuredLogMaxFileSizeMB;
            MinecraftHostSettings.Default.EnableSafeUpdateBackup = EnableSafeUpdateBackup;
            MinecraftHostSettings.Default.SafeUpdateBackupRetention = SafeUpdateBackupRetention;
            MinecraftHostSettings.Default.OperatorRole = OperatorRole;
            MinecraftHostSettings.Default.RequireRoleElevationAuthentication = RequireRoleElevationAuthentication;
            MinecraftHostSettings.Default.PreferWindowsHelloForRoleElevation = PreferWindowsHelloForRoleElevation;

            if (!string.IsNullOrWhiteSpace(NewRoleElevationPin))
                MinecraftHostSettings.Default.RoleElevationPinHash = HashPin(NewRoleElevationPin);

            MinecraftHostSettings.Default.Save();

            var state = new PolicyState
            {
                MaintenanceMode = MaintenanceMode,
                LockedServerIds = LockedServers
                    .Select(x => x.Id)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
            };
            await _policyService.SaveAsync(state);

            _suppressAutoSave = true;
            try
            {
                CurrentRoleElevationPin = string.Empty;
                NewRoleElevationPin = string.Empty;
                ConfirmRoleElevationPin = string.Empty;
            }
            finally
            {
                _suppressAutoSave = false;
            }

            AuthorizationStatus = string.Empty;
            OnPropertyChanged(nameof(JavaPathStatus));
            OnPropertyChanged(nameof(OpenAuditLogDeniedReason));
            OnPropertyChanged(nameof(OpenOperationsCenterDeniedReason));
            _authorizationStateNotifier.NotifyChanged();
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private void RevertRoleSelection(string role)
    {
        _suppressAutoSave = true;
        try
        {
            OperatorRole = role;
        }
        finally
        {
            _suppressAutoSave = false;
        }
    }

    private async void OpenStructuredLogViewer()
    {
        await SaveAsync();
        var window = new StructuredLogWindow
        {
            Owner = Application.Current.MainWindow,
            DataContext = new StructuredLogViewerViewModel()
        };
        window.ShowDialog();
    }

    private async void OpenAuditLogViewer()
    {
        var decision = _authorizationService.Authorize(OperationType.ViewAuditLog);
        if (!decision.Allowed)
        {
            AuthorizationStatus = AuthorizationUiText.ToInline(decision.Reason);
            MessageBox.Show(AuthorizationUiText.ToDialog(decision.Reason), "MinecraftHost", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await SaveAsync();
        var window = new AuditLogWindow
        {
            Owner = Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    private async void OpenOperationsCenter()
    {
        var decision = _authorizationService.Authorize(OperationType.ViewOperationsCenter);
        if (!decision.Allowed)
        {
            AuthorizationStatus = AuthorizationUiText.ToInline(decision.Reason);
            MessageBox.Show(AuthorizationUiText.ToDialog(decision.Reason), "MinecraftHost", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await SaveAsync();
        var window = new OperationsCenterWindow
        {
            Owner = Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    private void LoadServerChoices()
    {
        AvailableServers.Clear();
        foreach (var server in MinecraftHostSettings.Default.Servers)
            AvailableServers.Add(new ServerChoiceItem(server.Name, server.Id));
    }

    private void LoadLockedServers()
    {
        LockedServers.Clear();
        var map = AvailableServers.ToDictionary(x => x.Id, x => x, StringComparer.OrdinalIgnoreCase);
        foreach (var id in _policyService.Current.LockedServerIds.OrderBy(x => x))
        {
            if (map.TryGetValue(id, out var found))
                LockedServers.Add(new ServerChoiceItem(found.Name, found.Id));
            else
                LockedServers.Add(new ServerChoiceItem(Texts.SettingPage_UnregisteredServer, id));
        }
    }

    private static OperatorRole ParseRole(string raw)
    {
        return Enum.TryParse<OperatorRole>(raw, true, out var role) ? role : Models.Authorization.OperatorRole.Administrator;
    }

    private bool ValidatePinUpdate(out string reason)
    {
        reason = string.Empty;

        var hasAny = !string.IsNullOrWhiteSpace(NewRoleElevationPin) || !string.IsNullOrWhiteSpace(ConfirmRoleElevationPin);
        if (!hasAny)
            return true;

        if (!string.Equals(NewRoleElevationPin, ConfirmRoleElevationPin, StringComparison.Ordinal))
        {
            reason = Texts.SettingPage_PinMismatch;
            return false;
        }

        if (string.IsNullOrWhiteSpace(NewRoleElevationPin) || NewRoleElevationPin.Length < 4 || NewRoleElevationPin.Length > 32)
        {
            reason = Texts.SettingPage_PinLengthInvalid;
            return false;
        }

        return true;
    }

    private async Task<AuthorizationDecision> VerifyRoleElevation()
    {
        if (!RequireRoleElevationAuthentication)
            return AuthorizationDecision.Allow();

        if (PreferWindowsHelloForRoleElevation)
        {
            var hello = await TryWindowsHelloVerification().ConfigureAwait(false);
            if (hello is not null)
                return hello.Value;
        }

        var storedHash = MinecraftHostSettings.Default.RoleElevationPinHash ?? string.Empty;
        if (string.IsNullOrWhiteSpace(storedHash))
            return AuthorizationDecision.Deny(Texts.SettingPage_RoleElevationPinRequired);

        if (string.IsNullOrWhiteSpace(CurrentRoleElevationPin))
            return AuthorizationDecision.Deny(Texts.SettingPage_CurrentPinRequired);

        var verified = VerifyPin(CurrentRoleElevationPin, storedHash, out var shouldUpgradeHash);
        if (!verified)
            return AuthorizationDecision.Deny(Texts.SettingPage_PinVerificationFailed);

        if (shouldUpgradeHash)
        {
            MinecraftHostSettings.Default.RoleElevationPinHash = HashPin(CurrentRoleElevationPin);
            MinecraftHostSettings.Default.Save();
        }

        return AuthorizationDecision.Allow();
    }

    private static async Task<AuthorizationDecision?> TryWindowsHelloVerification()
    {
        try
        {
            var availability = await UserConsentVerifier.CheckAvailabilityAsync();
            if (availability != UserConsentVerifierAvailability.Available)
                return null;

            var result = await UserConsentVerifier.RequestVerificationAsync(Texts.SettingPage_WindowsHelloPrompt);

            return result switch
            {
                UserConsentVerificationResult.Verified => AuthorizationDecision.Allow(),
                UserConsentVerificationResult.Canceled => AuthorizationDecision.Deny(Texts.SettingPage_WindowsHelloCanceled),
                UserConsentVerificationResult.RetriesExhausted => AuthorizationDecision.Deny(Texts.SettingPage_WindowsHelloRetriesExhausted),
                UserConsentVerificationResult.DeviceBusy => AuthorizationDecision.Deny(Texts.SettingPage_WindowsHelloDeviceBusy),
                UserConsentVerificationResult.DisabledByPolicy => AuthorizationDecision.Deny(Texts.SettingPage_WindowsHelloDisabledByPolicy),
                UserConsentVerificationResult.NotConfiguredForUser => AuthorizationDecision.Deny(Texts.SettingPage_WindowsHelloNotConfigured),
                UserConsentVerificationResult.DeviceNotPresent => AuthorizationDecision.Deny(Texts.SettingPage_WindowsHelloDeviceNotPresent),
                _ => AuthorizationDecision.Deny(Texts.SettingPage_WindowsHelloFailed)
            };
        }
        catch
        {
            return null;
        }
    }

    private static string HashPin(string pin)
    {
        Span<byte> salt = stackalloc byte[PinSaltSize];
        RandomNumberGenerator.Fill(salt);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(pin), salt, PinIterations, HashAlgorithmName.SHA256, PinHashSize);
        return string.Concat(
            PinHashPrefix,
            "$",
            PinIterations.ToString(),
            "$",
            Convert.ToHexString(salt),
            "$",
            Convert.ToHexString(hash));
    }

    private static bool VerifyPin(string pin, string hashData, out bool shouldUpgradeHash)
    {
        shouldUpgradeHash = false;
        try
        {
            if (TryParsePbkdf2Hash(hashData, out var iterations, out var salt, out var expected))
            {
                var actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(pin), salt, iterations, HashAlgorithmName.SHA256, expected.Length);
                return CryptographicOperations.FixedTimeEquals(expected, actual);
            }

            var legacyExpected = Convert.FromHexString(hashData);
            var legacyActual = SHA256.HashData(Encoding.UTF8.GetBytes(pin));
            shouldUpgradeHash = legacyExpected.Length == legacyActual.Length && CryptographicOperations.FixedTimeEquals(legacyExpected, legacyActual);
            return shouldUpgradeHash;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParsePbkdf2Hash(string hashData, out int iterations, out byte[] salt, out byte[] hash)
    {
        iterations = 0;
        salt = [];
        hash = [];

        var parts = hashData.Split('$', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4)
            return false;

        if (!string.Equals(parts[0], PinHashPrefix, StringComparison.Ordinal))
            return false;

        if (!int.TryParse(parts[1], out iterations) || iterations <= 0)
            return false;

        salt = Convert.FromHexString(parts[2]);
        hash = Convert.FromHexString(parts[3]);
        return salt.Length > 0 && hash.Length > 0;
    }

    private string GetDeniedReason(OperationType operationType)
    {
        var decision = _authorizationService.Authorize(operationType);
        return decision.Allowed ? string.Empty : AuthorizationUiText.ToInline(decision.Reason);
    }
}