using MinecraftHost.Localization;
using MinecraftHost.Models.Jobs;
using MinecraftHost.Services.Interfaces.Jobs;
using MinecraftHost.Services.Interfaces.Observability;
using MinecraftHost.Services.Jobs;
using MinecraftHost.Services.Observability;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using YukkuriMovieMaker.Commons;

namespace MinecraftHost.ViewModels.Windows;

public sealed class OperationsCenterViewModel : Bindable, IDisposable
{
    private readonly IJobOrchestratorService _jobOrchestratorService;
    private readonly IObservabilityService _observabilityService;
    private readonly DispatcherTimer _timer;

    public ObservableCollection<JobRecord> Jobs { get; } = [];

    private string _summary = string.Empty;
    public string Summary
    {
        get => _summary;
        private set => Set(ref _summary, value);
    }

    public ActionCommand RefreshCommand { get; }

    public OperationsCenterViewModel()
        : this(JobOrchestratorServiceProvider.Instance, ObservabilityServiceProvider.Instance)
    {
    }

    public OperationsCenterViewModel(IJobOrchestratorService jobOrchestratorService, IObservabilityService observabilityService)
    {
        _jobOrchestratorService = jobOrchestratorService;
        _observabilityService = observabilityService;
        RefreshCommand = new ActionCommand(_ => true, _ => Refresh());
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Refresh();
    }

    public void Dispose()
    {
        _timer.Stop();
    }

    private void Refresh()
    {
        var jobs = _jobOrchestratorService.GetRecentJobs(300).ToArray();
        Jobs.Clear();
        foreach (var job in jobs)
            Jobs.Add(job);

        var snapshot = _observabilityService.GetSnapshot();
        Summary = string.Format(
            Texts.OperationsCenter_SummaryFormat,
            snapshot.TotalJobs,
            snapshot.SucceededJobs,
            snapshot.FailedJobs,
            snapshot.RunningJobs,
            snapshot.AverageJobDurationMs);
    }
}