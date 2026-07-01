using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Threading;
using System.Threading.Tasks;
using OneMMC.Core.Features.PCManagement.Models.EventViewer;
using Microsoft.Extensions.Logging;

namespace OneMMC.Core.Features.PCManagement.Services.EventViewer;

/// <summary>
/// Reads Task Scheduler task history from the operational event channel and toggles "All Tasks History"
/// (the equivalent of taskschd.msc's Enable/Disable All Tasks History action). Reuses
/// <see cref="EventViewerService"/> for reading so the History tab stays in the BCL eventing paradigm.
/// </summary>
public sealed class TaskHistoryService
{
    /// <summary>The operational channel that records per-task history events.</summary>
    public const string OperationalChannel = "Microsoft-Windows-TaskScheduler/Operational";

    private readonly EventViewerService _eventViewer;
    private readonly ILogger<TaskHistoryService> _logger;

    public TaskHistoryService(EventViewerService eventViewer, ILogger<TaskHistoryService> logger)
    {
        _eventViewer = eventViewer;
        _logger = logger;
    }

    /// <summary>Returns whether the Task Scheduler operational channel (task history) is currently enabled.</summary>
    public bool IsHistoryEnabled()
    {
        try
        {
            using var config = new EventLogConfiguration(OperationalChannel);
            return config.IsEnabled;
        }
        catch (EventLogException ex)
        {
            _logger.LogWarning(ex, "[TaskHistoryService] Could not query the operational channel state.");
            return false;
        }
    }

    /// <summary>
    /// Enables or disables the Task Scheduler operational channel for the whole computer (admin-gated).
    /// </summary>
    public Task SetHistoryEnabledAsync(bool enabled) => Task.Run(() =>
    {
        using var config = new EventLogConfiguration(OperationalChannel)
        {
            IsEnabled = enabled,
        };
        config.SaveChanges();
        _logger.LogInformation("[TaskHistoryService] All Tasks History set to {Enabled}.", enabled);
    });

    /// <summary>
    /// Reads recent operational events for a single task, filtered by its full path. Returns newest first.
    /// </summary>
    public Task<List<EventLogEntry>> ReadTaskHistoryAsync(string taskPath, int maxEvents = 200, CancellationToken cancellationToken = default)
    {
        // Task Scheduler operational events carry the task path in EventData/Data[@Name='TaskName'].
        var escaped = taskPath.Replace("'", "&apos;");
        var xpath = $"*[EventData[Data[@Name='TaskName']='{escaped}']]";
        return _eventViewer.ReadEventsAsync(OperationalChannel, xpath, maxEvents, cancellationToken);
    }
}
