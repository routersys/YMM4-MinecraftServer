using MinecraftHost.Localization;
using MinecraftHost.Models.Scheduler;
using MinecraftHost.Settings.Configuration;
using MinecraftHost.ViewModels.Items;
using System.Collections.ObjectModel;
using YukkuriMovieMaker.Commons;

namespace MinecraftHost.ViewModels.Windows;

public class TaskSchedulerWindowViewModel : Bindable
{
    public ObservableCollection<ScheduledTaskItemViewModel> Tasks { get; } = new();

    private ScheduledTaskItemViewModel? _selectedTask;
    public ScheduledTaskItemViewModel? SelectedTask
    {
        get => _selectedTask;
        set
        {
            Set(ref _selectedTask, value);
            DeleteTaskCommand.RaiseCanExecuteChanged();
        }
    }

    public ActionCommand AddTaskCommand { get; }
    public ActionCommand DeleteTaskCommand { get; }

    public IReadOnlyList<EnumOption<ScheduleMode>> ScheduleModes { get; } = new[]
    {
        new EnumOption<ScheduleMode>(ScheduleMode.Interval, Texts.Xaml_Scheduler_ModeInterval),
        new EnumOption<ScheduleMode>(ScheduleMode.Daily, Texts.Xaml_Scheduler_ModeDaily),
        new EnumOption<ScheduleMode>(ScheduleMode.Weekly, Texts.Xaml_Scheduler_ModeWeekly),
        new EnumOption<ScheduleMode>(ScheduleMode.SpecificDate, Texts.Xaml_Scheduler_ModeSpecific)
    };

    public IReadOnlyList<EnumOption<DayOfWeek>> DaysOfWeek { get; } = new[]
    {
        new EnumOption<DayOfWeek>(DayOfWeek.Sunday, "日曜 / Sunday"),
        new EnumOption<DayOfWeek>(DayOfWeek.Monday, "月曜 / Monday"),
        new EnumOption<DayOfWeek>(DayOfWeek.Tuesday, "火曜 / Tuesday"),
        new EnumOption<DayOfWeek>(DayOfWeek.Wednesday, "水曜 / Wednesday"),
        new EnumOption<DayOfWeek>(DayOfWeek.Thursday, "木曜 / Thursday"),
        new EnumOption<DayOfWeek>(DayOfWeek.Friday, "金曜 / Friday"),
        new EnumOption<DayOfWeek>(DayOfWeek.Saturday, "土曜 / Saturday")
    };

    public TaskSchedulerWindowViewModel()
    {
        AddTaskCommand = new ActionCommand(_ => true, _ => AddTask());
        DeleteTaskCommand = new ActionCommand(_ => SelectedTask != null, _ => DeleteTask());

        foreach (var task in MinecraftHostSettings.Default.ScheduledTasks)
        {
            Tasks.Add(new ScheduledTaskItemViewModel(task));
        }

        if (Tasks.Count > 0)
            SelectedTask = Tasks[0];
    }

    private void AddTask()
    {
        var config = new ScheduledTaskConfig { Name = "New Task" };
        var vm = new ScheduledTaskItemViewModel(config);
        MinecraftHostSettings.Default.ScheduledTasks.Add(config);
        Tasks.Add(vm);
        SelectedTask = vm;
        MinecraftHostSettings.Default.Save();
    }

    private void DeleteTask()
    {
        if (SelectedTask == null) return;
        var vm = SelectedTask;
        MinecraftHostSettings.Default.ScheduledTasks.Remove(vm.Config);
        Tasks.Remove(vm);
        SelectedTask = Tasks.Count > 0 ? Tasks[0] : null;
        MinecraftHostSettings.Default.Save();
    }
}

public class EnumOption<T>
{
    public T Value { get; }
    public string Display { get; }
    public EnumOption(T value, string display)
    {
        Value = value;
        Display = display;
    }

    public override string ToString() => Display;
}