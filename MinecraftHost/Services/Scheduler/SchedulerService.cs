using MinecraftHost.Models.Scheduler;
using MinecraftHost.Services.Interfaces.Jobs;
using MinecraftHost.Services.Interfaces.Logging;
using MinecraftHost.Services.Interfaces.Server;
using MinecraftHost.Settings.Configuration;
using System.Diagnostics;
using System.IO;

namespace MinecraftHost.Services.Scheduler;

public class SchedulerService : Interfaces.Scheduler.ISchedulerService, IDisposable
{
    private readonly IServerManager _serverManager;
    private readonly IJobOrchestratorService _jobOrchestratorService;
    private readonly IStructuredLogService _logService;
    private PeriodicTimer? _timer;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private bool _disposed;

    public SchedulerService(IServerManager serverManager, IJobOrchestratorService jobOrchestratorService, IStructuredLogService logService)
    {
        _serverManager = serverManager;
        _jobOrchestratorService = jobOrchestratorService;
        _logService = logService;
    }

    public void Start()
    {
        if (_loopTask != null || _disposed) return;
        _cts = new CancellationTokenSource();
        _timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        _loopTask = LoopAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        try
        {
            _loopTask?.Wait(2000);
        }
        catch { }
        _timer?.Dispose();
        _cts?.Dispose();
        _loopTask = null;
    }

    public void ReloadTasks()
    {
    }

    private async Task LoopAsync(CancellationToken token)
    {
        try
        {
            while (await _timer!.WaitForNextTickAsync(token))
            {
                var now = DateTime.Now;
                var tasks = MinecraftHostSettings.Default.ScheduledTasks;
                foreach (var task in tasks)
                {
                    if (!task.IsEnabled) continue;

                    if (ShouldRun(task, now))
                    {
                        task.LastRunTime = now;
                        _ = RunTaskAsync(task);
                        MinecraftHostSettings.Default.Save();
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private bool ShouldRun(ScheduledTaskConfig task, DateTime now)
    {
        if (task.LastRunTime == null)
        {
            if (task.Mode == ScheduleMode.SpecificDate)
                return now >= task.SpecificDate.Date.Add(task.TimeOfDay);

            return true;
        }

        var last = task.LastRunTime.Value;

        switch (task.Mode)
        {
            case ScheduleMode.Interval:
                return (now - last).TotalSeconds >= task.IntervalSeconds;
            case ScheduleMode.Daily:
                {
                    var nextRun = last.Date.Add(task.TimeOfDay);
                    if (nextRun <= last) nextRun = nextRun.AddDays(1);
                    return now >= nextRun;
                }
            case ScheduleMode.Weekly:
                {
                    var nextRun = last.Date.Add(task.TimeOfDay);
                    while (nextRun <= last || nextRun.DayOfWeek != task.DayOfWeek)
                    {
                        nextRun = nextRun.AddDays(1);
                    }
                    return now >= nextRun;
                }
            case ScheduleMode.SpecificDate:
                return false;
            default:
                return false;
        }
    }

    public async Task RunTaskAsync(ScheduledTaskConfig taskConfig)
    {
        if (taskConfig.TargetAllServers)
        {
            foreach (var server in MinecraftHostSettings.Default.Servers)
                await ExecuteScriptAsync(taskConfig, server.Id.ToString(), server.Name, _serverManager.GetServerDirectory(server));
        }
        else if (taskConfig.TargetServerIds.Count > 0)
        {
            foreach (var id in taskConfig.TargetServerIds)
            {
                var server = System.Linq.Enumerable.FirstOrDefault(MinecraftHostSettings.Default.Servers, s => s.Id == id);
                if (server != null)
                    await ExecuteScriptAsync(taskConfig, server.Id, server.Name, _serverManager.GetServerDirectory(server));
            }
        }
        else
        {
            await ExecuteScriptAsync(taskConfig, string.Empty, string.Empty, string.Empty);
        }
    }

    private async Task ExecuteScriptAsync(ScheduledTaskConfig taskConfig, string serverId, string serverName, string serverDir)
    {
        if (string.IsNullOrWhiteSpace(taskConfig.FilePath) || !File.Exists(taskConfig.FilePath))
            return;

        var ext = Path.GetExtension(taskConfig.FilePath).ToLowerInvariant();
        var isPs = ext is ".ps1" or ".ps2" or ".psm1";
        var isBat = ext is ".bat" or ".cmd";

        if (!isPs && !isBat)
            return;

        var startInfo = new ProcessStartInfo
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = string.IsNullOrWhiteSpace(serverDir) ? AppDomain.CurrentDomain.BaseDirectory : serverDir
        };

        if (isPs)
        {
            startInfo.FileName = "powershell.exe";
            startInfo.Arguments = $"-ExecutionPolicy Bypass -NoProfile -NonInteractive -File \"{taskConfig.FilePath}\" {taskConfig.Arguments}";
        }
        else
        {
            startInfo.FileName = "cmd.exe";
            startInfo.Arguments = $"/c \"\"{taskConfig.FilePath}\" {taskConfig.Arguments}\"";
        }

        startInfo.EnvironmentVariables["TARGET_SERVER_ID"] = serverId;
        startInfo.EnvironmentVariables["TARGET_SERVER_NAME"] = serverName;
        startInfo.EnvironmentVariables["TARGET_SERVER_DIR"] = serverDir;

        var jobName = string.Format(Localization.Texts.Job_Scheduler_TaskRun, taskConfig.Name);
        var safeServerId = string.IsNullOrEmpty(serverId) ? Guid.Empty.ToString() : serverId;

        await _jobOrchestratorService.ExecuteAsync(jobName, safeServerId, async (cancellationToken) =>
        {
            _logService.Log(Models.Logging.StructuredLogLevel.Information, "SchedulerService", string.Format(Localization.Texts.Log_Scheduler_TaskStarted, taskConfig.Name), "ExecuteScript", safeServerId);

            try
            {
                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    await process.WaitForExitAsync(cancellationToken);
                    _logService.Log(Models.Logging.StructuredLogLevel.Information, "SchedulerService", string.Format(Localization.Texts.Log_Scheduler_TaskCompleted, taskConfig.Name), "ExecuteScript", safeServerId);
                }
            }
            catch (Exception ex)
            {
                _logService.Log(Models.Logging.StructuredLogLevel.Error, "SchedulerService", ex.Message, "ExecuteScript", safeServerId, ex);
                throw;
            }
        }, 1);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}