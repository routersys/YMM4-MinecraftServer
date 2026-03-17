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

public class ModManagerViewModel : Bindable
{
    private readonly string _modsDirectory;
    private readonly string _serverId;
    private readonly bool _isServerRunning;
    private readonly IJobOrchestratorService _jobOrchestratorService;
    private readonly IStructuredLogService _logService;
    private PluginInfo? _selectedMod;
    private string _statusMessage = string.Empty;

    public ObservableCollection<PluginInfo> Mods { get; } = [];

    public PluginInfo? SelectedMod
    {
        get => _selectedMod;
        set
        {
            Set(ref _selectedMod, value);
            UninstallCommand.RaiseCanExecuteChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => Set(ref _statusMessage, value);
    }

    public bool IsServerRunning => _isServerRunning;
    public bool HasNoMods => Mods.Count == 0;

    public ActionCommand InstallCommand { get; }
    public ActionCommand UninstallCommand { get; }
    public ActionCommand OpenFolderCommand { get; }

    public ModManagerViewModel(string serverDirectory, string serverId, bool isServerRunning, IJobOrchestratorService jobOrchestratorService, IStructuredLogService logService)
    {
        _modsDirectory = Path.Combine(serverDirectory, "mods");
        _serverId = serverId;
        _isServerRunning = isServerRunning;
        _jobOrchestratorService = jobOrchestratorService;
        _logService = logService;
        Directory.CreateDirectory(_modsDirectory);

        InstallCommand = new ActionCommand(_ => true, _ => InstallFromDialog());
        UninstallCommand = new ActionCommand(_ => SelectedMod is not null, _ => Uninstall());
        OpenFolderCommand = new ActionCommand(_ => true, _ => OpenFolder());

        Refresh();
    }

    public void Refresh()
    {
        Mods.Clear();
        if (!Directory.Exists(_modsDirectory))
        {
            UpdateStatus();
            return;
        }

        foreach (var path in Directory.GetFiles(_modsDirectory, "*.jar"))
        {
            var fi = new FileInfo(path);
            Mods.Add(new PluginInfo
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
        StatusMessage = string.Format(Texts.ModManager_StatusModCountFormat, Mods.Count);
        OnPropertyChanged(nameof(HasNoMods));
    }

    private async void InstallFromDialog()
    {
        var dlg = new OpenFileDialog
        {
            Title = Texts.ModManager_SelectJarTitle,
            Filter = Texts.ModManager_SelectJarFilter,
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;
        await InstallFilesAsync(dlg.FileNames);
    }

    public async Task InstallFilesAsync(string[] filePaths)
    {
        var jobName = string.Format(Texts.Job_Mod_Install, filePaths.Length);
        await _jobOrchestratorService.ExecuteAsync(jobName, _serverId, async (cancellationToken) =>
        {
            await Task.Run(() =>
            {
                var count = 0;
                foreach (var src in filePaths)
                {
                    if (!src.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)) continue;
                    var dest = Path.Combine(_modsDirectory, Path.GetFileName(src));
                    try
                    {
                        File.Copy(src, dest, overwrite: true);
                        count++;
                        _logService.Log(Models.Logging.StructuredLogLevel.Information, "ModManager", string.Format(Texts.Log_Mod_Installed, Path.GetFileName(src)), "InstallMod", _serverId);
                    }
                    catch (Exception ex)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            MessageBox.Show(string.Format(Texts.ModManager_InstallFailedFormat, Path.GetFileName(src), ex.Message),
                                Texts.Common_ErrorTitle, MessageBoxButton.OK, MessageBoxImage.Error);
                        });
                        _logService.Log(Models.Logging.StructuredLogLevel.Error, "ModManager", string.Format(Texts.ModManager_InstallFailedFormat, Path.GetFileName(src), ex.Message), "InstallMod", _serverId, ex);
                        throw;
                    }
                }
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Refresh();
                    if (count > 0)
                        StatusMessage = string.Format(Texts.ModManager_InstallCompletedFormat, count, Mods.Count);
                });
            });
        });
    }

    private async void Uninstall()
    {
        if (SelectedMod is null) return;
        var modName = SelectedMod.Name;
        var fullPath = SelectedMod.FullPath;

        var result = MessageBox.Show(
            string.Format(Texts.ModManager_UninstallConfirmFormat, modName),
            Texts.Common_ConfirmTitle, MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        var jobName = string.Format(Texts.Job_Mod_Uninstall, modName);
        await _jobOrchestratorService.ExecuteAsync(jobName, _serverId, async (cancellationToken) =>
        {
            await Task.Run(() =>
            {
                try
                {
                    File.Delete(fullPath);
                    _logService.Log(Models.Logging.StructuredLogLevel.Information, "ModManager", string.Format(Texts.Log_Mod_Uninstalled, modName), "UninstallMod", _serverId);
                    Application.Current.Dispatcher.Invoke(() => Refresh());
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show(string.Format(Texts.ModManager_UninstallFailedFormat, ex.Message), Texts.Common_ErrorTitle, MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                    _logService.Log(Models.Logging.StructuredLogLevel.Error, "ModManager", string.Format(Texts.ModManager_UninstallFailedFormat, ex.Message), "UninstallMod", _serverId, ex);
                    throw;
                }
            });
        });
    }

    private void OpenFolder()
    {
        Directory.CreateDirectory(_modsDirectory);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = _modsDirectory,
            UseShellExecute = true
        });
    }
}