using Microsoft.Win32;
using MinecraftHost.Localization;
using MinecraftHost.Models.Server;
using MinecraftHost.Services.Interfaces.Jobs;
using MinecraftHost.Services.Interfaces.Logging;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using YukkuriMovieMaker.Commons;

namespace MinecraftHost.ViewModels.Windows;

public class PluginManagerViewModel : Bindable
{
    private readonly string _pluginsDirectory;
    private readonly string _serverId;
    private readonly bool _isServerRunning;
    private readonly IJobOrchestratorService _jobOrchestratorService;
    private readonly IStructuredLogService _logService;

    public ObservableCollection<PluginInfo> Plugins { get; } = new();

    private PluginInfo? _selectedPlugin;
    public PluginInfo? SelectedPlugin
    {
        get => _selectedPlugin;
        set
        {
            Set(ref _selectedPlugin, value);
            UninstallCommand.RaiseCanExecuteChanged();
        }
    }

    private string _statusMessage = string.Empty;
    public string StatusMessage
    {
        get => _statusMessage;
        private set => Set(ref _statusMessage, value);
    }

    public bool IsServerRunning => _isServerRunning;
    public bool HasNoPlugins => Plugins.Count == 0;

    public ActionCommand InstallCommand { get; }
    public ActionCommand UninstallCommand { get; }
    public ActionCommand OpenFolderCommand { get; }

    public PluginManagerViewModel(string serverDirectory, string serverId, bool isServerRunning, IJobOrchestratorService jobOrchestratorService, IStructuredLogService logService)
    {
        _pluginsDirectory = Path.Combine(serverDirectory, "plugins");
        _serverId = serverId;
        _isServerRunning = isServerRunning;
        _jobOrchestratorService = jobOrchestratorService;
        _logService = logService;
        Directory.CreateDirectory(_pluginsDirectory);

        InstallCommand = new ActionCommand(_ => true, _ => InstallFromDialog());
        UninstallCommand = new ActionCommand(_ => SelectedPlugin != null, _ => Uninstall());
        OpenFolderCommand = new ActionCommand(_ => true, _ => OpenFolder());

        Refresh();
    }

    public void Refresh()
    {
        Plugins.Clear();
        if (!Directory.Exists(_pluginsDirectory))
        {
            UpdateStatus();
            return;
        }

        foreach (var path in Directory.GetFiles(_pluginsDirectory, "*.jar"))
        {
            var fi = new FileInfo(path);
            Plugins.Add(new PluginInfo
            {
                FileName = fi.Name,
                Name = Path.GetFileNameWithoutExtension(fi.Name),
                SizeKB = fi.Length / 1024,
                FullPath = path
            });
        }

        UpdateStatus();
    }

    private void UpdateStatus()
    {
        StatusMessage = string.Format(Texts.PluginManager_StatusPluginCountFormat, Plugins.Count);
        OnPropertyChanged(nameof(HasNoPlugins));
    }

    private async void InstallFromDialog()
    {
        var dlg = new OpenFileDialog
        {
            Title = Texts.PluginManager_SelectJarTitle,
            Filter = Texts.PluginManager_SelectJarFilter,
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;
        await InstallFilesAsync(dlg.FileNames);
    }

    public async Task InstallFilesAsync(string[] filePaths)
    {
        var jobName = string.Format(Texts.Job_Plugin_Install, filePaths.Length);
        await _jobOrchestratorService.ExecuteAsync(jobName, _serverId, async (cancellationToken) =>
        {
            await Task.Run(() =>
            {
                var count = 0;
                foreach (var src in filePaths)
                {
                    if (!src.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)) continue;
                    var dest = Path.Combine(_pluginsDirectory, Path.GetFileName(src));
                    try
                    {
                        File.Copy(src, dest, overwrite: true);
                        count++;
                        _logService.Log(Models.Logging.StructuredLogLevel.Information, "PluginManager", string.Format(Texts.Log_Plugin_Installed, Path.GetFileName(src)), "InstallPlugin", _serverId);
                    }
                    catch (Exception ex)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            MessageBox.Show(string.Format(Texts.PluginManager_InstallFailedFormat, Path.GetFileName(src), ex.Message),
                                Texts.Common_ErrorTitle, MessageBoxButton.OK, MessageBoxImage.Error);
                        });
                        _logService.Log(Models.Logging.StructuredLogLevel.Error, "PluginManager", string.Format(Texts.PluginManager_InstallFailedFormat, Path.GetFileName(src), ex.Message), "InstallPlugin", _serverId, ex);
                        throw;
                    }
                }
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Refresh();
                    if (count > 0)
                        StatusMessage = string.Format(Texts.PluginManager_InstallCompletedFormat, count, Plugins.Count);
                });
            });
        });
    }

    private async void Uninstall()
    {
        if (SelectedPlugin == null) return;
        var pluginName = SelectedPlugin.Name;
        var fullPath = SelectedPlugin.FullPath;

        var result = MessageBox.Show(
            string.Format(Texts.PluginManager_UninstallConfirmFormat, pluginName),
            Texts.Common_ConfirmTitle, MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        var jobName = string.Format(Texts.Job_Plugin_Uninstall, pluginName);
        await _jobOrchestratorService.ExecuteAsync(jobName, _serverId, async (cancellationToken) =>
        {
            await Task.Run(() =>
            {
                try
                {
                    File.Delete(fullPath);
                    _logService.Log(Models.Logging.StructuredLogLevel.Information, "PluginManager", string.Format(Texts.Log_Plugin_Uninstalled, pluginName), "UninstallPlugin", _serverId);
                    Application.Current.Dispatcher.Invoke(() => Refresh());
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show(string.Format(Texts.PluginManager_UninstallFailedFormat, ex.Message), Texts.Common_ErrorTitle, MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                    _logService.Log(Models.Logging.StructuredLogLevel.Error, "PluginManager", string.Format(Texts.PluginManager_UninstallFailedFormat, ex.Message), "UninstallPlugin", _serverId, ex);
                    throw;
                }
            });
        });
    }

    private void OpenFolder()
    {
        Directory.CreateDirectory(_pluginsDirectory);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = _pluginsDirectory,
            UseShellExecute = true
        });
    }
}