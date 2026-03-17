namespace MinecraftHost.Models.Scheduler;

public class ScheduledTaskConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = false;
    public ScheduleMode Mode { get; set; } = ScheduleMode.Interval;
    public int IntervalSeconds { get; set; } = 3600;
    public TimeSpan TimeOfDay { get; set; } = TimeSpan.Zero;
    public DayOfWeek DayOfWeek { get; set; } = DayOfWeek.Sunday;
    public DateTime SpecificDate { get; set; } = DateTime.Now.Date;
    public string FilePath { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public List<string> TargetServerIds { get; set; } = new();
    public bool TargetAllServers { get; set; } = true;
    public DateTime? LastRunTime { get; set; }
}