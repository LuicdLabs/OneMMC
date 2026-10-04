using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OneMMC.Core.Features.SystemManagement.Models.ComExp;
using OneMMC.Core.Features.SystemManagement.Services.ComExp;
using OneMMC.Core.Infrastructure.Collections;
using OneMMC.Core.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace OneMMC.Core.Features.SystemManagement.ViewModels.ComExp;

/// <summary>
/// ViewModel for the Running Processes page (Component Services \ Running Processes).
/// Owns the running-process data and the current tree selection; the view mirrors
/// <see cref="FilteredProcesses"/> into explicit TreeView nodes (see TaskSchedulerPage).
/// </summary>
public partial class RunningProcessesViewModel : ObservableObject
{
    private readonly ComponentServicesManager _service;
    private readonly ILogger<RunningProcessesViewModel> _logger;

    /// <summary>All running COM+ server processes (unfiltered).</summary>
    public ObservableCollection<ComPlusRunningProcess> Processes { get; } = new();

    /// <summary>Processes matching the current search text.</summary>
    public ObservableCollection<ComPlusRunningProcess> FilteredProcesses { get; } = new();

    [ObservableProperty]
    public partial ComPlusTreeItem? SelectedItem { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SummaryText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasNoProcesses { get; set; } = true;

    public bool IsRootSelected => SelectedItem?.Kind == ComPlusTreeNodeKind.Root;
    public bool IsProcessSelected => SelectedItem?.Kind == ComPlusTreeNodeKind.Process;
    public bool IsInstanceSelected => SelectedItem?.Kind == ComPlusTreeNodeKind.Application;
    public bool IsComponentSelected => SelectedItem?.Kind == ComPlusTreeNodeKind.Component;

    public ComPlusRunningProcess? SelectedProcess =>
        SelectedItem?.Kind == ComPlusTreeNodeKind.Process ? SelectedItem.Process : null;

    public ComPlusApplicationInstance? SelectedInstance =>
        SelectedItem?.Kind == ComPlusTreeNodeKind.Application ? SelectedItem.Instance : null;

    public ComPlusComponentInfo? SelectedComponent =>
        SelectedItem?.Kind == ComPlusTreeNodeKind.Component ? SelectedItem.Component : null;

    /// <summary>Selected process ID rendered as text (x:Bind cannot bind int to Text).</summary>
    public string SelectedProcessIdText =>
        SelectedProcess?.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

    public RunningProcessesViewModel(ComponentServicesManager service)
        : this(service, NullLogger<RunningProcessesViewModel>.Instance)
    {
    }

    public RunningProcessesViewModel(ComponentServicesManager service, ILogger<RunningProcessesViewModel> logger)
    {
        _service = service;
        _logger = logger;
    }

    partial void OnSelectedItemChanged(ComPlusTreeItem? value)
    {
        OnPropertyChanged(nameof(IsRootSelected));
        OnPropertyChanged(nameof(IsProcessSelected));
        OnPropertyChanged(nameof(IsInstanceSelected));
        OnPropertyChanged(nameof(IsComponentSelected));
        OnPropertyChanged(nameof(SelectedProcess));
        OnPropertyChanged(nameof(SelectedProcessIdText));
        OnPropertyChanged(nameof(SelectedInstance));
        OnPropertyChanged(nameof(SelectedComponent));
    }

    [RelayCommand]
    public async Task LoadProcessesAsync()
    {
        _logger.LogInformation("Loading COM+ running processes");
        var L = LocalizationProvider.Current;
        IsLoading = true;
        StatusMessage = L.GetString(ResourceFileNames.ComExp, ComExpKeys.LoadingProcesses);

        try
        {
            var processes = await _service.GetComPlusRunningProcessesAsync();
            Processes.ReplaceAll(processes);
            HasNoProcesses = Processes.Count == 0;
            ApplyFilter(null);
            StatusMessage = L.GetFormattedString(ResourceFileNames.ComExp, ComExpKeys.LoadedCount, Processes.Count);
        }
        catch (Exception ex)
        {
            StatusMessage = L.GetFormattedString(ResourceFileNames.ComExp, ComExpKeys.LoadFailed, ex.Message);
            _logger.LogError(ex, "Failed to load COM+ running processes");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Filters <see cref="FilteredProcesses"/> by process name, PID, executable,
    /// or hosted application name. A <see langword="null"/> filter clears the search.
    /// </summary>
    /// <param name="filter">The search text, or <see langword="null"/> to show all processes.</param>
    public void ApplyFilter(string? filter)
    {
        string text = filter?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(text))
        {
            FilteredProcesses.ReplaceAll(Processes);
        }
        else
        {
            FilteredProcesses.ReplaceAll(Processes.Where(process =>
                process.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                || process.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture).Contains(text, StringComparison.OrdinalIgnoreCase)
                || (process.ExecutableName?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false)
                || process.Instance.ApplicationName.Contains(text, StringComparison.OrdinalIgnoreCase)));
        }

        SummaryText = LocalizationProvider.Current.GetFormattedString(
            ResourceFileNames.ComExp, ComExpKeys.RunSummaryFormat, FilteredProcesses.Count);
        SelectedItem = null;
    }
}
