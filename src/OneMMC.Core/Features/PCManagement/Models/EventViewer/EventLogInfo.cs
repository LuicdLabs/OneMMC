using CommunityToolkit.Mvvm.ComponentModel;

namespace OneMMC.Core.Features.PCManagement.Models.EventViewer;

/// <summary>
/// Represents metadata about an event log (from EventLogConfiguration).
/// Used for the Log Properties dialog.
/// </summary>
public partial class EventLogInfo : ObservableObject
{
    [ObservableProperty]
    public partial string LogName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LogFilePath { get; set; } = string.Empty;

    /// <summary>Log file size in bytes.</summary>
    [ObservableProperty]
    public partial long LogFileSize { get; set; }

    /// <summary>Maximum log file size in bytes.</summary>
    [ObservableProperty]
    public partial long MaxLogFileSize { get; set; }

    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    /// <summary>Log mode: Circular, AutoBackup, or Retain.</summary>
    [ObservableProperty]
    public partial string LogMode { get; set; } = string.Empty;
}


