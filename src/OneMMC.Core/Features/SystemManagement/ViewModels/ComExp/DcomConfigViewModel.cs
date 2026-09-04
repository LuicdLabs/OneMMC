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
/// ViewModel for the DCOM Config page (Component Services \ DCOM Config).
/// Lists {GUID} application identities from HKLM\SOFTWARE\Classes\AppID and
/// exposes the selected application's General / Location / Security / Endpoints / Identity details.
/// </summary>
public partial class DcomConfigViewModel : ObservableObject
{
    private readonly ComponentServicesManager _service;
    private readonly ILogger<DcomConfigViewModel> _logger;

    /// <summary>All loaded DCOM applications (unfiltered).</summary>
    public ObservableCollection<DcomApplicationInfo> Applications { get; } = new();

    /// <summary>Applications matching <see cref="SearchText"/>.</summary>
    public ObservableCollection<DcomApplicationInfo> FilteredApplications { get; } = new();

    [ObservableProperty]
    public partial DcomApplicationInfo? SelectedApplication { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    public bool HasSelection => SelectedApplication is not null;
    public bool HasNoResults => FilteredApplications.Count == 0;

    public DcomConfigViewModel(ComponentServicesManager service)
        : this(service, NullLogger<DcomConfigViewModel>.Instance)
    {
    }

    public DcomConfigViewModel(ComponentServicesManager service, ILogger<DcomConfigViewModel> logger)
    {
        _service = service;
        _logger = logger;
    }

    partial void OnSelectedApplicationChanged(DcomApplicationInfo? value)
    {
        OnPropertyChanged(nameof(HasSelection));
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    [RelayCommand]
    public async Task LoadApplicationsAsync()
    {
        _logger.LogInformation("Loading DCOM applications");
        var L = LocalizationProvider.Current;
        IsLoading = true;
        StatusMessage = L.GetString(ResourceFileNames.ComExp, ComExpKeys.LoadingDcomApps);

        try
        {
            var apps = await _service.GetDcomApplicationsAsync();
            Applications.ReplaceAll(apps);
            ApplyFilter();
            SelectedApplication = FilteredApplications.FirstOrDefault();
            StatusMessage = L.GetFormattedString(ResourceFileNames.ComExp, ComExpKeys.LoadedCount, Applications.Count);
        }
        catch (Exception ex)
        {
            StatusMessage = L.GetFormattedString(ResourceFileNames.ComExp, ComExpKeys.LoadFailed, ex.Message);
            _logger.LogError(ex, "Failed to load DCOM applications");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyFilter()
    {
        string filter = SearchText?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(filter))
        {
            FilteredApplications.ReplaceAll(Applications);
        }
        else
        {
            FilteredApplications.ReplaceAll(Applications.Where(app =>
                app.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || app.AppId.Contains(filter, StringComparison.OrdinalIgnoreCase)));
        }

        if (SelectedApplication is not null && !FilteredApplications.Contains(SelectedApplication))
        {
            SelectedApplication = FilteredApplications.FirstOrDefault();
        }
        else if (SelectedApplication is null)
        {
            SelectedApplication = FilteredApplications.FirstOrDefault();
        }

        OnPropertyChanged(nameof(HasNoResults));
    }
}
