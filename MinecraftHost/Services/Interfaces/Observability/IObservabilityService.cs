using MinecraftHost.Models.Jobs;
using MinecraftHost.Models.Server;

namespace MinecraftHost.Services.Interfaces.Observability;

public interface IObservabilityService
{
    void RecordJob(JobRecord jobRecord);
    ObservabilitySnapshot GetSnapshot();
}