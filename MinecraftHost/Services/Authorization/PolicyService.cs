using MinecraftHost.Models.Authorization;
using MinecraftHost.Services.Interfaces.Authorization;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace MinecraftHost.Services.Authorization;

public sealed class PolicyService : IPolicyService
{
    private readonly string _policyFilePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly PolicyState _current;

    public PolicyState Current => _current;

    public PolicyService()
    {
        var basePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppDomain.CurrentDomain.BaseDirectory;
        _policyFilePath = Path.Combine(basePath, "policy", "policy-state.json");
        _current = Load(_policyFilePath);
    }

    public async Task SaveAsync(PolicyState state, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _current.MaintenanceMode = state.MaintenanceMode;
            _current.LockedServerIds = state.LockedServerIds;

            var directory = Path.GetDirectoryName(_policyFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(_current);
            await File.WriteAllTextAsync(_policyFilePath, json, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static PolicyState Load(string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            if (!File.Exists(path))
                return new PolicyState();

            var json = File.ReadAllText(path);
            var state = JsonSerializer.Deserialize<PolicyState>(json);
            return state ?? new PolicyState();
        }
        catch
        {
            return new PolicyState();
        }
    }
}