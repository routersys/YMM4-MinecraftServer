using Microsoft.Win32;
using MinecraftHost.Models.Scheduler;
using MinecraftHost.Settings.Configuration;
using System.Collections.ObjectModel;
using YukkuriMovieMaker.Commons;

namespace MinecraftHost.ViewModels.Items;

public class ScheduledTaskItemViewModel : Bindable
{
    private readonly ScheduledTaskConfig _config;

    public ScheduledTaskItemViewModel(ScheduledTaskConfig config)
    {
        _config = config;

        foreach (var server in MinecraftHostSettings.Default.Servers)
        {
            var isChecked = _config.TargetAllServers || _config.TargetServerIds.Contains(server.Id.ToString());
            var serverSelection = new ServerSelectionItem
            {
                Id = server.Id.ToString(),
                Name = server.Name,
                IsSelected = isChecked
            };
            serverSelection.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ServerSelectionItem.IsSelected))
                {
                    UpdateTargetServerIds();
                }
            };
            AvailableServers.Add(serverSelection);
        }

        SelectFileCommand = new ActionCommand(_ => true, _ => SelectFile());
    }

    public ActionCommand SelectFileCommand { get; }

    private void SelectFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Script File",
            Filter = "Scripts (*.bat;*.cmd;*.ps1;*.ps2;*.psm1)|*.bat;*.cmd;*.ps1;*.ps2;*.psm1|All Files (*.*)|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            FilePath = dialog.FileName;
        }
    }

    public ScheduledTaskConfig Config => _config;

    public string Name
    {
        get => _config.Name;
        set { _config.Name = value; OnPropertyChanged(); }
    }

    public bool IsEnabled
    {
        get => _config.IsEnabled;
        set { _config.IsEnabled = value; OnPropertyChanged(); }
    }

    public ScheduleMode Mode
    {
        get => _config.Mode;
        set { _config.Mode = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayMode)); }
    }

    public string DisplayMode
    {
        get
        {
            return _config.Mode switch
            {
                ScheduleMode.Interval => MinecraftHost.Localization.Texts.Xaml_Scheduler_ModeInterval,
                ScheduleMode.Daily => MinecraftHost.Localization.Texts.Xaml_Scheduler_ModeDaily,
                ScheduleMode.Weekly => MinecraftHost.Localization.Texts.Xaml_Scheduler_ModeWeekly,
                ScheduleMode.SpecificDate => MinecraftHost.Localization.Texts.Xaml_Scheduler_ModeSpecific,
                _ => _config.Mode.ToString()
            };
        }
    }

    public int IntervalSeconds
    {
        get => _config.IntervalSeconds;
        set { _config.IntervalSeconds = value; OnPropertyChanged(); }
    }

    public TimeSpan TimeOfDay
    {
        get => _config.TimeOfDay;
        set { _config.TimeOfDay = value; OnPropertyChanged(); }
    }

    public DayOfWeek DayOfWeek
    {
        get => _config.DayOfWeek;
        set { _config.DayOfWeek = value; OnPropertyChanged(); }
    }

    public DateTime SpecificDate
    {
        get => _config.SpecificDate;
        set { _config.SpecificDate = value; OnPropertyChanged(); }
    }

    public string FilePath
    {
        get => _config.FilePath;
        set { _config.FilePath = value; OnPropertyChanged(); }
    }

    public string Arguments
    {
        get => _config.Arguments;
        set { _config.Arguments = value; OnPropertyChanged(); }
    }

    public bool TargetAllServers
    {
        get => _config.TargetAllServers;
        set
        {
            _config.TargetAllServers = value;
            OnPropertyChanged();
            if (value)
            {
                foreach (var s in AvailableServers) s.IsSelected = true;
            }
        }
    }

    public ObservableCollection<ServerSelectionItem> AvailableServers { get; } = new();

    private void UpdateTargetServerIds()
    {
        if (TargetAllServers) return;

        _config.TargetServerIds.Clear();
        foreach (var s in AvailableServers.Where(x => x.IsSelected))
        {
            _config.TargetServerIds.Add(s.Id);
        }
    }
}

public class ServerSelectionItem : Bindable
{
    private string _id = string.Empty;
    public string Id
    {
        get => _id;
        set => Set(ref _id, value);
    }

    private string _name = string.Empty;
    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }
}