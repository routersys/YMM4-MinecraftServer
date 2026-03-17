using MinecraftHost.Models.Scheduler;

namespace MinecraftHost.Services.Interfaces.Scheduler;

public interface ISchedulerService
{
    void Start();
    void Stop();
    void ReloadTasks();
    Task RunTaskAsync(ScheduledTaskConfig taskConfig);
}