using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using ManagementTools.Core.Features.PCManagement.Models.EventViewer;
using ManagementTools.Core.Localization;
using Microsoft.Extensions.Logging;

namespace ManagementTools.Core.Features.PCManagement.Services.EventViewer;

/// <summary>
/// Service layer wrapping <see cref="System.Diagnostics.Eventing.Reader"/> APIs
/// for reading, exporting, clearing, and monitoring Windows event logs.
/// </summary>
public class EventViewerService : IDisposable
{
    private readonly ILogger<EventViewerService> _logger;
    private EventLogWatcher? _activeWatcher;
    private bool _disposed;

    /// <summary>
    /// The five standard Windows Logs shown at the top of the tree.
    /// </summary>
    private static readonly string[] StandardWindowsLogNames =
    [
        "Application",
        "Security",
        "Setup",
        "System",
        "ForwardedEvents"
    ];

    private static readonly HashSet<string> WindowsLogNames = new(StandardWindowsLogNames, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Raised when a new event arrives during live monitoring.
    /// </summary>
    public event EventHandler<EventLogEntry>? EventReceived;

    public EventViewerService(ILogger<EventViewerService> logger)
    {
        _logger = logger;
    }

    // ========================================================================
    // Tree Building
    // ========================================================================

    /// <summary>
    /// Builds the complete tree structure of event logs.
    /// Returns localized root nodes for Windows logs and applications/services logs.
    /// </summary>
    public async Task<List<EventLogTreeNode>> BuildLogTreeAsync(CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            var allLogNames = EventLogSession.GlobalSession.GetLogNames().ToList();
            var availableLogNames = new HashSet<string>(allLogNames, StringComparer.OrdinalIgnoreCase);

            _logger.LogInformation("Discovered {LogCount} event logs on the system.", allLogNames.Count);

            // Windows Logs root
            var windowsLogsRoot = new EventLogTreeNode
            {
                DisplayName = LocalizationProvider.Current.GetString(
                    ResourceFileNames.EventViewer,
                    EventViewerKeys.TreeWindowsLogs)
            };
            foreach (var name in StandardWindowsLogNames)
            {
                if (!availableLogNames.Contains(name))
                    continue;

                windowsLogsRoot.Children.Add(new EventLogTreeNode
                {
                    DisplayName = GetStandardWindowsLogDisplayName(name),
                    LogName = name
                });
            }

            // Applications and Services Logs root
            var appServicesRoot = new EventLogTreeNode
            {
                DisplayName = LocalizationProvider.Current.GetString(
                    ResourceFileNames.EventViewer,
                    EventViewerKeys.TreeAppServicesLogs)
            };
            var appServiceLogNames = new List<string>();

            foreach (var logName in allLogNames)
            {
                ct.ThrowIfCancellationRequested();

                if (WindowsLogNames.Contains(logName))
                    continue;

                try
                {
                    using var config = new EventLogConfiguration(logName);
                    if (config.IsEnabled)
                    {
                        appServiceLogNames.Add(logName);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Skipping inaccessible log: {LogName}", logName);
                }
            }

            BuildHierarchicalTree(appServicesRoot, appServiceLogNames);

            _logger.LogInformation("Built event log tree: {WindowsCount} Windows logs, {AppCount} application/service logs.",
                windowsLogsRoot.Children.Count, appServiceLogNames.Count);

            return new List<EventLogTreeNode> { windowsLogsRoot, appServicesRoot };
        }, ct);
    }

    private static string GetStandardWindowsLogDisplayName(string logName)
    {
        var resourceKey = logName switch
        {
            "Application" => EventViewerKeys.TreeApplication,
            "Security" => EventViewerKeys.TreeSecurity,
            "Setup" => EventViewerKeys.TreeSetup,
            "System" => EventViewerKeys.TreeSystem,
            "ForwardedEvents" => EventViewerKeys.TreeForwardedEvents,
            _ => null
        };

        return resourceKey is null
            ? logName
            : LocalizationProvider.Current.GetString(ResourceFileNames.EventViewer, resourceKey);
    }

    /// <summary>
    /// Builds a hierarchical tree from flat log names by splitting on '-' and '/'.
    /// E.g., "Microsoft-Windows-PowerShell/Operational" â†’ Microsoft > Windows > PowerShell > Operational.
    /// </summary>
    private static void BuildHierarchicalTree(EventLogTreeNode root, List<string> logNames)
    {
        logNames.Sort(StringComparer.OrdinalIgnoreCase);

        foreach (var fullLogName in logNames)
        {
            // Split by '/' first (channel separator), then split the provider by '-'
            var slashParts = fullLogName.Split('/');
            var providerParts = slashParts[0].Split('-');

            // Combine: all provider segments + channel segments (if any)
            var allSegments = new List<string>(providerParts);
            for (int i = 1; i < slashParts.Length; i++)
                allSegments.Add(slashParts[i]);

            var currentNode = root;
            for (int i = 0; i < allSegments.Count; i++)
            {
                var segment = allSegments[i];
                bool isLeaf = i == allSegments.Count - 1;

                var existingChild = currentNode.Children
                    .FirstOrDefault(c => c.DisplayName.Equals(segment, StringComparison.OrdinalIgnoreCase));

                if (existingChild is not null)
                {
                    if (isLeaf && existingChild.LogName is null)
                    {
                        // This folder node is also a valid log â€” mark it
                        existingChild.LogName = fullLogName;
                    }
                    currentNode = existingChild;
                }
                else
                {
                    var newNode = new EventLogTreeNode
                    {
                        DisplayName = segment,
                        LogName = isLeaf ? fullLogName : null
                    };
                    currentNode.Children.Add(newNode);
                    currentNode = newNode;
                }
            }
        }
    }

    // ========================================================================
    // Event Reading
    // ========================================================================

    /// <summary>
    /// Reads events from a specific log in reverse chronological order (newest first).
    /// </summary>
    /// <param name="logName">The log name (e.g., "Application").</param>
    /// <param name="xpathQuery">XPath filter (e.g., "*[System[Level=2]]"). Pass "*" for all events.</param>
    /// <param name="maxEvents">Maximum number of events to return per batch.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<List<EventLogEntry>> ReadEventsAsync(
        string logName,
        string xpathQuery = "*",
        int maxEvents = 200,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var results = new List<EventLogEntry>(maxEvents);
            try
            {
                if (!LogExists(logName))
                {
                    _logger.LogWarning("Event log not found: {LogName}", logName);
                    return results;
                }

                var query = new EventLogQuery(logName, PathType.LogName, xpathQuery)
                {
                    ReverseDirection = true
                };

                using var reader = new EventLogReader(query);
                EventRecord? record;
                while ((record = reader.ReadEvent()) is not null && results.Count < maxEvents)
                {
                    ct.ThrowIfCancellationRequested();
                    using (record)
                    {
                        results.Add(MapEventRecord(record));
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (EventLogNotFoundException ex)
            {
                _logger.LogWarning(ex, "Event log not found: {LogName}", logName);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Access denied reading log: {LogName}", logName);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading events from {LogName}.", logName);
                throw;
            }

            return results;
        }, ct);
    }

    /// <summary>
    /// Reads the next batch of events after the specified record ID for paginated loading.
    /// </summary>
    public async Task<List<EventLogEntry>> ReadMoreEventsAsync(
        string logName,
        string xpathQuery,
        long lastRecordId,
        int maxEvents = 200,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var results = new List<EventLogEntry>(maxEvents);
            try
            {
                if (!LogExists(logName))
                {
                    _logger.LogWarning("Event log not found: {LogName}", logName);
                    return results;
                }

                // Build a query that filters out records we've already seen
                string combinedQuery;
                if (xpathQuery == "*")
                {
                    combinedQuery = $"*[System[EventRecordID<{lastRecordId}]]";
                }
                else
                {
                    // Extract the inner predicate from the xpathQuery (e.g., "Level=2" from "*[System[Level=2]]")
                    // and combine with the record ID constraint
                    var levelMatch = System.Text.RegularExpressions.Regex.Match(xpathQuery, @"\*\[System\[(.+?)\]\]");
                    if (levelMatch.Success)
                    {
                        combinedQuery = $"*[System[EventRecordID<{lastRecordId} and {levelMatch.Groups[1].Value}]]";
                    }
                    else
                    {
                        combinedQuery = $"*[System[EventRecordID<{lastRecordId}]]";
                    }
                }

                var query = new EventLogQuery(logName, PathType.LogName, combinedQuery)
                {
                    ReverseDirection = true
                };

                using var reader = new EventLogReader(query);
                EventRecord? record;
                while ((record = reader.ReadEvent()) is not null && results.Count < maxEvents)
                {
                    ct.ThrowIfCancellationRequested();
                    using (record)
                    {
                        results.Add(MapEventRecord(record));
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading more events from {LogName}.", logName);
                throw;
            }

            return results;
        }, ct);
    }

    // ========================================================================
    // Log Properties
    // ========================================================================

    /// <summary>
    /// Gets metadata for a specific event log via <see cref="EventLogConfiguration"/>.
    /// </summary>
    public async Task<EventLogInfo> GetLogInfoAsync(string logName, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            if (!LogExists(logName))
            {
                _logger.LogWarning("Event log not found: {LogName}", logName);
                return new EventLogInfo
                {
                    LogName = logName,
                    LogFilePath = string.Empty,
                    LogFileSize = 0,
                    MaxLogFileSize = 0,
                    IsEnabled = false,
                    LogMode = string.Empty
                };
            }

            using var config = new EventLogConfiguration(logName);

            long fileSize = 0;
            try
            {
                var logPath = Environment.ExpandEnvironmentVariables(config.LogFilePath ?? string.Empty);
                if (File.Exists(logPath))
                {
                    fileSize = new FileInfo(logPath).Length;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not read file size for log: {LogName}", logName);
            }

            return new EventLogInfo
            {
                LogName = config.LogName,
                LogFilePath = config.LogFilePath ?? string.Empty,
                LogFileSize = fileSize,
                MaxLogFileSize = config.MaximumSizeInBytes,
                IsEnabled = config.IsEnabled,
                LogMode = config.LogMode.ToString()
            };
        }, ct);
    }

    // ========================================================================
    // Log Actions
    // ========================================================================

    /// <summary>
    /// Clears all events from a log. May require administrator privileges.
    /// </summary>
    /// <param name="logName">The log name.</param>
    /// <param name="backupPath">Optional path to save events before clearing.</param>
    public async Task ClearLogAsync(string logName, string? backupPath = null, CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            _logger.LogInformation("Clearing event log: {LogName}, backup: {BackupPath}", logName, backupPath ?? "(none)");

            if (!LogExists(logName))
            {
                _logger.LogWarning("Event log not found: {LogName}", logName);
                return;
            }

            var session = EventLogSession.GlobalSession;
            if (!string.IsNullOrEmpty(backupPath))
            {
                RunWithPreparedTargetFile(backupPath, tempPath => session.ClearLog(logName, tempPath));
            }
            else
            {
                session.ClearLog(logName);
            }

            _logger.LogInformation("Event log cleared: {LogName}", logName);
        }, ct);
    }

    /// <summary>
    /// Exports a log to .evtx file using <see cref="EventLogSession.ExportLog"/>.
    /// </summary>
    public async Task ExportLogAsync(string logName, string targetPath, string xpathQuery = "*", CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            _logger.LogInformation("Exporting event log: {LogName} to {TargetPath}", logName, targetPath);

            if (!LogExists(logName))
            {
                _logger.LogWarning("Event log not found: {LogName}", logName);
                return;
            }

            var session = EventLogSession.GlobalSession;
            RunWithPreparedTargetFile(
                targetPath,
                tempPath => session.ExportLog(logName, PathType.LogName, xpathQuery, tempPath, tolerateQueryErrors: true));

            _logger.LogInformation("Event log exported: {LogName} to {TargetPath}", logName, targetPath);
        }, ct);
    }

    private void RunWithPreparedTargetFile(string targetPath, Action<string> writeAction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        var fullTargetPath = Path.GetFullPath(targetPath);
        var directory = Path.GetDirectoryName(fullTargetPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException($"Could not determine target directory for '{targetPath}'.");
        }

        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(
            directory,
            $"{Path.GetFileNameWithoutExtension(fullTargetPath)}.{Guid.NewGuid():N}{Path.GetExtension(fullTargetPath)}");

        try
        {
            writeAction(tempPath);

            if (!File.Exists(tempPath))
            {
                throw new IOException($"The event log API did not produce the expected file: {tempPath}");
            }

            File.Move(tempPath, fullTargetPath, overwrite: true);
        }
        catch
        {
            TryDeleteFile(tempPath);
            throw;
        }
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to delete temporary event log file: {Path}", path);
        }
    }

    // ========================================================================
    // Real-time Monitoring
    // ========================================================================

    /// <summary>
    /// Starts an <see cref="EventLogWatcher"/> for real-time event monitoring.
    /// Only one watcher can be active at a time â€” calling this stops any existing watcher.
    /// </summary>
    public void StartWatching(string logName, string xpathQuery = "*")
    {
        StopWatching();

        if (!LogExists(logName))
        {
            _logger.LogWarning("Event log not found: {LogName}", logName);
            return;
        }

        try
        {
            var query = new EventLogQuery(logName, PathType.LogName, xpathQuery);
            _activeWatcher = new EventLogWatcher(query);
            _activeWatcher.EventRecordWritten += OnWatcherEventRecordWritten;
            _activeWatcher.Enabled = true;

            _logger.LogInformation("Started watching event log: {LogName}", logName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start event log watcher for {LogName}.", logName);
            throw;
        }
    }

    /// <summary>
    /// Stops the active <see cref="EventLogWatcher"/>.
    /// </summary>
    public void StopWatching()
    {
        if (_activeWatcher is not null)
        {
            _activeWatcher.Enabled = false;
            _activeWatcher.EventRecordWritten -= OnWatcherEventRecordWritten;
            _activeWatcher.Dispose();
            _activeWatcher = null;

            _logger.LogInformation("Stopped event log watcher.");
        }
    }

    private bool LogExists(string logName)
    {
        try
        {
            var logNames = EventLogSession.GlobalSession.GetLogNames();
            return logNames.Contains(logName, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to enumerate event logs while validating {LogName}.", logName);
            return false;
        }
    }

    private void OnWatcherEventRecordWritten(object? sender, EventRecordWrittenEventArgs e)
    {
        if (e.EventRecord is not null)
        {
            try
            {
                var entry = MapEventRecord(e.EventRecord);
                EventReceived?.Invoke(this, entry);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error mapping watched event record.");
            }
        }
    }

    // ========================================================================
    // Event Record Mapping
    // ========================================================================

    /// <summary>
    /// Converts an <see cref="EventRecord"/> to an <see cref="EventLogEntry"/> model.
    /// </summary>
    private EventLogEntry MapEventRecord(EventRecord record)
    {
        var level = (byte)(record.Level ?? 4);
        var levelDisplay = level switch
        {
            1 => "Critical",
            2 => "Error",
            3 => "Warning",
            4 => "Information",
            5 => "Verbose",
            _ => "Information"
        };

        string xmlData;
        try
        {
            xmlData = record.ToXml();
        }
        catch { xmlData = string.Empty; }

        var taskDisplay = ResolveTaskCategory(record, xmlData);
        var opCodeDisplay = ResolveOpCode(record, xmlData);
        var keywordsDisplay = ResolveKeywords(record, xmlData);

        var message = ExtractMessage(record, xmlData);

        string userName = "N/A";
        try
        {
            if (record.UserId is not null)
            {
                userName = record.UserId.Translate(typeof(NTAccount))?.ToString() ?? record.UserId.Value;
            }
        }
        catch
        {
            try { userName = record.UserId?.Value ?? "N/A"; }
            catch { /* keep N/A */ }
        }

        return new EventLogEntry
        {
            RecordId = record.RecordId ?? 0,
            LogName = record.LogName ?? string.Empty,
            Source = record.ProviderName ?? string.Empty,
            EventId = record.Id,
            Level = level,
            LevelDisplayName = levelDisplay,
            TimeCreated = record.TimeCreated ?? DateTime.MinValue,
            TaskCategory = taskDisplay,
            Keywords = keywordsDisplay,
            User = userName,
            Computer = record.MachineName ?? string.Empty,
            OpCode = opCodeDisplay,
            Message = message,
            XmlData = xmlData
        };
    }

    private static string ExtractMessage(EventRecord record, string xmlData)
    {
        try
        {
            var formattedMessage = record.FormatDescription();
            if (!string.IsNullOrWhiteSpace(formattedMessage))
                return formattedMessage;
        }
        catch
        {
            // Fall back to XML parsing when message metadata is unavailable.
        }

        return ExtractMessageFromXml(xmlData);
    }

    private static string ExtractMessageFromXml(string xmlData)
    {
        if (string.IsNullOrWhiteSpace(xmlData))
            return string.Empty;

        try
        {
            var xml = XDocument.Parse(xmlData);

            var renderingMessage = xml
                .Descendants()
                .FirstOrDefault(e =>
                    e.Name.LocalName == "Message" &&
                    e.Parent?.Name.LocalName == "RenderingInfo")
                ?.Value;

            if (!string.IsNullOrWhiteSpace(renderingMessage))
                return renderingMessage;

            var values = xml
                .Descendants()
                .Where(e => e.Name.LocalName == "Data")
                .Select(e => e.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();

            return values.Count > 0 ? string.Join(" | ", values) : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    // ========================================================================
    // Display Name Resolution
    // ========================================================================

    /// <summary>
    /// Resolves the human-readable Task Category display name matching native eventvwr.msc.
    /// Uses provider metadata first, then falls back to XML-derived values and finally the raw task id.
    /// </summary>
    private static string ResolveTaskCategory(EventRecord record, string xmlData)
    {
        // Native metadata is the most accurate source when the provider publishes a localized task name.
        var taskDisplayName = TryGetDisplayName(() => record.TaskDisplayName);
        if (!string.IsNullOrWhiteSpace(taskDisplayName))
            return taskDisplayName;

        // Some callers may provide rendered XML that already includes the resolved task display name.
        var xmlValue = ExtractRenderingInfoValue(xmlData, "Task");
        if (!string.IsNullOrWhiteSpace(xmlValue))
            return xmlValue;

        // Raw value fallback: Task 0 = "None" (matches eventvwr behavior)
        var rawTask = record.Task;
        if (rawTask is null or 0)
            return "None";

        return rawTask.Value.ToString();
    }

    /// <summary>
    /// Resolves the human-readable OpCode display name matching native eventvwr.msc.
    /// Uses XML RenderingInfo first, then well-known ETW opcode mappings.
    /// </summary>
    private static string ResolveOpCode(EventRecord record, string xmlData)
    {
        // 1. Try XML RenderingInfo (zero-cost, no exceptions)
        var xmlValue = ExtractRenderingInfoValue(xmlData, "Opcode");
        if (!string.IsNullOrWhiteSpace(xmlValue))
            return xmlValue;

        // 2. Raw value fallback with well-known OpCode mappings
        var rawOpcode = record.Opcode;
        if (rawOpcode is null)
            return string.Empty;

        return rawOpcode.Value switch
        {
            0 => "Info",
            1 => "Start",
            2 => "Stop",
            3 => "DataCollectionStart",
            4 => "DataCollectionStop",
            5 => "Extension",
            6 => "Reply",
            7 => "Resume",
            8 => "Suspend",
            9 => "Send",
            _ => rawOpcode.Value.ToString()
        };
    }

    /// <summary>
    /// Resolves the human-readable Keywords display string matching native eventvwr.msc.
    /// Uses provider metadata first, then XML-derived values, then a conservative raw bitmask fallback.
    /// </summary>
    private static string ResolveKeywords(EventRecord record, string xmlData)
    {
        var keywordsDisplayNames = TryGetDisplayNames(() => record.KeywordsDisplayNames);
        if (keywordsDisplayNames is not null)
            return string.Join(", ", keywordsDisplayNames);

        // Some callers may provide rendered XML with resolved keyword names.
        var xmlValue = ExtractRenderingInfoKeywords(xmlData);
        if (!string.IsNullOrWhiteSpace(xmlValue))
            return xmlValue;

        // Raw bitmask fallback with well-known keyword values
        var rawKeywords = record.Keywords;
        if (rawKeywords is null or 0)
            return string.Empty;

        var kw = (ulong)rawKeywords.Value;

        // Native Event Viewer leaves classic-only events blank instead of showing "Classic".
        if (kw == 0x8000000000000000UL)
            return string.Empty;

        var names = new List<string>();

        // Well-known keyword bitmask values defined by ETW/Windows
        if ((kw & 0x0001000000000000UL) != 0) names.Add("Response Time");
        if ((kw & 0x0010000000000000UL) != 0) names.Add("WDI Diag");
        if ((kw & 0x0020000000000000UL) != 0) names.Add("SQM");
        if ((kw & 0x0040000000000000UL) != 0) names.Add("Audit Failure");
        if ((kw & 0x0080000000000000UL) != 0) names.Add("Audit Success");
        if ((kw & 0x0100000000000000UL) != 0) names.Add("Correlation Hint");

        if (names.Count > 0)
            return string.Join(", ", names);

        // Unknown bitmask â€” show hex
        return $"0x{kw:X}";
    }

    /// <summary>
    /// Safely resolves a single provider display string without failing the event mapping path.
    /// </summary>
    private static string? TryGetDisplayName(Func<string?> getDisplayName)
    {
        try
        {
            var value = getDisplayName();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Safely resolves provider display strings without failing the event mapping path.
    /// </summary>
    private static List<string>? TryGetDisplayNames(Func<IEnumerable<string>?> getDisplayNames)
    {
        try
        {
            var values = getDisplayNames()?
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            return values is { Count: > 0 } ? values : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts a single display value from the XML RenderingInfo section.
    /// For example, extracting "Task", "Opcode", or "Level" display names.
    /// </summary>
    private static string? ExtractRenderingInfoValue(string xmlData, string elementName)
    {
        if (string.IsNullOrWhiteSpace(xmlData))
            return null;

        try
        {
            var xml = XDocument.Parse(xmlData);
            return xml
                .Descendants()
                .FirstOrDefault(e =>
                    e.Name.LocalName == elementName &&
                    e.Parent?.Name.LocalName == "RenderingInfo")
                ?.Value;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts keyword display names from the XML RenderingInfo section.
    /// The RenderingInfo may contain multiple &lt;Keyword&gt; elements.
    /// </summary>
    private static string? ExtractRenderingInfoKeywords(string xmlData)
    {
        if (string.IsNullOrWhiteSpace(xmlData))
            return null;

        try
        {
            var xml = XDocument.Parse(xmlData);
            var renderingInfo = xml
                .Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "RenderingInfo");

            if (renderingInfo is null)
                return null;

            var keywords = renderingInfo
                .Elements()
                .Where(e => e.Name.LocalName == "Keyword")
                .Select(e => e.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();

            return keywords.Count > 0 ? string.Join(", ", keywords) : null;
        }
        catch
        {
            return null;
        }
    }

    // ========================================================================
    // Dispose
    // ========================================================================

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopWatching();
        GC.SuppressFinalize(this);
    }
}


