using System;
using CommunityToolkit.Mvvm.ComponentModel;
using OneMMC.Core.Localization;

namespace OneMMC.Core.Features.PCManagement.Models.EventViewer;

/// <summary>
/// Represents a single event log entry read from the Windows Event Log.
/// </summary>
public partial class EventLogEntry : ObservableObject
{
    [ObservableProperty]
    public partial long RecordId { get; set; }

    [ObservableProperty]
    public partial string LogName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Source { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int EventId { get; set; }

    /// <summary>
    /// Event level value: 0=LogAlways, 1=Critical, 2=Error, 3=Warning, 4=Information, 5=Verbose.
    /// </summary>
    [ObservableProperty]
    public partial byte Level { get; set; }

    [ObservableProperty]
    public partial string LevelDisplayName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial DateTime TimeCreated { get; set; }

    [ObservableProperty]
    public partial string TaskCategory { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Keywords { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string User { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Computer { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string OpCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Message { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string XmlData { get; set; } = string.Empty;

    /// <summary>
    /// Returns a localized level display name.
    /// </summary>
    public string LocalizedLevel => Level switch
    {
        1 => LocalizationProvider.Current.GetString(ResourceFileNames.EventViewer, EventViewerKeys.LevelCritical),
        2 => LocalizationProvider.Current.GetString(ResourceFileNames.EventViewer, EventViewerKeys.LevelError),
        3 => LocalizationProvider.Current.GetString(ResourceFileNames.EventViewer, EventViewerKeys.LevelWarning),
        4 => LocalizationProvider.Current.GetString(ResourceFileNames.EventViewer, EventViewerKeys.LevelInformation),
        5 => LocalizationProvider.Current.GetString(ResourceFileNames.EventViewer, EventViewerKeys.LevelVerbose),
        _ => LocalizationProvider.Current.GetString(ResourceFileNames.EventViewer, EventViewerKeys.LevelInformation)
    };

    /// <summary>
    /// Summary line for the event list: "Source | Event ID: N | Category: Cat".
    /// </summary>
    public string SummaryLine => string.IsNullOrEmpty(TaskCategory) || TaskCategory is "0" or "None"
        ? $"{Source} | Event ID: {EventId}"
        : $"{Source} | Event ID: {EventId} | {TaskCategory}";
}


