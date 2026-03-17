using MinecraftHost.Models.Logging;
using MinecraftHost.Services;
using MinecraftHost.Services.Interfaces.Audit;
using System.Collections.ObjectModel;
using YukkuriMovieMaker.Commons;

namespace MinecraftHost.ViewModels.Windows;

public sealed class AuditLogViewerViewModel : Bindable
{
    private readonly IAuditTrailService _auditTrailService;

    public ObservableCollection<AuditLogEntry> Entries { get; } = [];

    private int _maxItems = 500;
    public int MaxItems
    {
        get => _maxItems;
        set => Set(ref _maxItems, Math.Clamp(value, 50, 5000));
    }

    public ActionCommand RefreshCommand { get; }

    public AuditLogViewerViewModel()
        : this(AuditTrailServiceProvider.Instance)
    {
    }

    public AuditLogViewerViewModel(IAuditTrailService auditTrailService)
    {
        _auditTrailService = auditTrailService;
        RefreshCommand = new ActionCommand(_ => true, _ => _ = RefreshAsync());
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var logs = await _auditTrailService.GetRecentAsync(MaxItems);
        Entries.Clear();
        foreach (var entry in logs.Reverse())
            Entries.Add(entry);
    }
}