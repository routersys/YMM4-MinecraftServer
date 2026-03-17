using MinecraftHost.Localization;
using MinecraftHost.Models.Logging;
using MinecraftHost.Services.Interfaces.Logging;
using MinecraftHost.Services.Logging;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Data;
using YukkuriMovieMaker.Commons;

namespace MinecraftHost.ViewModels.Windows;

public sealed class StructuredLogViewerViewModel : Bindable
{
    private static string AllFilterValue => Texts.StructuredLog_AllFilter;
    private readonly IStructuredLogService _structuredLogService;
    private int _totalCount;

    public ObservableCollection<StructuredLogEntry> Entries { get; } = [];
    public ObservableCollection<string> AvailableLevels { get; } = [AllFilterValue];
    public ObservableCollection<string> AvailableCategories { get; } = [AllFilterValue];
    public ObservableCollection<string> AvailableOperations { get; } = [AllFilterValue];
    public ObservableCollection<string> AvailableServerIds { get; } = [AllFilterValue];

    public ICollectionView EntriesView { get; }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        private set => Set(ref _isLoading, value);
    }

    private int _maxItems = 1000;
    public int MaxItems
    {
        get => _maxItems;
        set
        {
            var normalized = Math.Clamp(value, 100, 10000);
            Set(ref _maxItems, normalized);
        }
    }

    private string _status = string.Empty;
    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public ActionCommand RefreshCommand { get; }
    public ActionCommand ClearCommand { get; }
    public ActionCommand OpenFolderCommand { get; }
    public ActionCommand ResetFiltersCommand { get; }

    private string _selectedLevel = AllFilterValue;
    public string SelectedLevel
    {
        get => _selectedLevel;
        set
        {
            Set(ref _selectedLevel, value);
            ApplyFilter();
        }
    }

    private string _selectedCategory = AllFilterValue;
    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            Set(ref _selectedCategory, value);
            ApplyFilter();
        }
    }

    private string _selectedOperation = AllFilterValue;
    public string SelectedOperation
    {
        get => _selectedOperation;
        set
        {
            Set(ref _selectedOperation, value);
            ApplyFilter();
        }
    }

    private string _selectedServerId = AllFilterValue;
    public string SelectedServerId
    {
        get => _selectedServerId;
        set
        {
            Set(ref _selectedServerId, value);
            ApplyFilter();
        }
    }

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set
        {
            Set(ref _searchText, value);
            ApplyFilter();
        }
    }

    public StructuredLogViewerViewModel()
        : this(StructuredLogServiceProvider.Instance)
    {
    }

    public StructuredLogViewerViewModel(IStructuredLogService structuredLogService)
    {
        _structuredLogService = structuredLogService;
        EntriesView = CollectionViewSource.GetDefaultView(Entries);
        EntriesView.Filter = FilterEntry;
        RefreshCommand = new ActionCommand(_ => !IsLoading, _ => _ = LoadAsync());
        ClearCommand = new ActionCommand(_ => !IsLoading, _ => _ = ClearAsync());
        OpenFolderCommand = new ActionCommand(_ => true, _ => OpenFolder());
        ResetFiltersCommand = new ActionCommand(_ => true, _ => ResetFilters());

        foreach (var level in Enum.GetNames<StructuredLogLevel>())
            AvailableLevels.Add(level);

        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        IsLoading = true;
        RefreshCommand.RaiseCanExecuteChanged();
        ClearCommand.RaiseCanExecuteChanged();

        try
        {
            Entries.Clear();
            var items = await _structuredLogService.GetRecentEntriesAsync(MaxItems);
            foreach (var item in items.OrderByDescending(x => x.TimestampUtc))
                Entries.Add(item);
            _totalCount = Entries.Count;
            RefreshFilterSources();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            Status = string.Format(Texts.StructuredLog_LoadFailedFormat, ex.Message);
        }
        finally
        {
            IsLoading = false;
            RefreshCommand.RaiseCanExecuteChanged();
            ClearCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task ClearAsync()
    {
        IsLoading = true;
        RefreshCommand.RaiseCanExecuteChanged();
        ClearCommand.RaiseCanExecuteChanged();

        try
        {
            await _structuredLogService.ClearAsync();
            Entries.Clear();
            _totalCount = 0;
            RefreshFilterSources();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            Status = string.Format(Texts.StructuredLog_ClearFailedFormat, ex.Message);
        }
        finally
        {
            IsLoading = false;
            RefreshCommand.RaiseCanExecuteChanged();
            ClearCommand.RaiseCanExecuteChanged();
        }
    }

    private void OpenFolder()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = _structuredLogService.GetLogsDirectory(),
            UseShellExecute = true
        });
    }

    private bool FilterEntry(object obj)
    {
        if (obj is not StructuredLogEntry entry)
            return false;

        if (SelectedLevel != AllFilterValue && !string.Equals(entry.Level.ToString(), SelectedLevel, StringComparison.OrdinalIgnoreCase))
            return false;

        if (SelectedCategory != AllFilterValue && !string.Equals(entry.Category, SelectedCategory, StringComparison.OrdinalIgnoreCase))
            return false;

        if (SelectedOperation != AllFilterValue && !string.Equals(entry.Operation, SelectedOperation, StringComparison.OrdinalIgnoreCase))
            return false;

        if (SelectedServerId != AllFilterValue && !string.Equals(entry.ServerId, SelectedServerId, StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.IsNullOrWhiteSpace(SearchText))
            return true;

        var term = SearchText.Trim();
        return entry.Message.Contains(term, StringComparison.OrdinalIgnoreCase)
               || entry.Exception.Contains(term, StringComparison.OrdinalIgnoreCase)
               || entry.CorrelationId.Contains(term, StringComparison.OrdinalIgnoreCase)
               || entry.Category.Contains(term, StringComparison.OrdinalIgnoreCase)
               || entry.Operation.Contains(term, StringComparison.OrdinalIgnoreCase)
               || entry.ServerId.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyFilter()
    {
        EntriesView.Refresh();
        var visibleCount = EntriesView is CollectionView view ? view.Count : EntriesView.Cast<object>().Count();
        Status = string.Format(Texts.StructuredLog_StatusFormat, visibleCount, _totalCount);
    }

    private void ResetFilters()
    {
        SelectedLevel = AllFilterValue;
        SelectedCategory = AllFilterValue;
        SelectedOperation = AllFilterValue;
        SelectedServerId = AllFilterValue;
        SearchText = string.Empty;
        ApplyFilter();
    }

    private void RefreshFilterSources()
    {
        RebuildFilterSource(AvailableCategories, Entries.Select(x => x.Category));
        RebuildFilterSource(AvailableOperations, Entries.Select(x => x.Operation));
        RebuildFilterSource(AvailableServerIds, Entries.Select(x => x.ServerId));

        if (!AvailableCategories.Contains(SelectedCategory))
            SelectedCategory = AllFilterValue;
        if (!AvailableOperations.Contains(SelectedOperation))
            SelectedOperation = AllFilterValue;
        if (!AvailableServerIds.Contains(SelectedServerId))
            SelectedServerId = AllFilterValue;
    }

    private static void RebuildFilterSource(ObservableCollection<string> target, IEnumerable<string> values)
    {
        target.Clear();
        target.Add(AllFilterValue);
        foreach (var value in values.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            target.Add(value);
    }
}