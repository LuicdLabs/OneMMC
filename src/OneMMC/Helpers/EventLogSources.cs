using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;

namespace OneMMC.Helpers;

/// <summary>
/// Shared system-enumeration helpers for the Basic "On an event" Log / Source pickers used by both the
/// New Trigger and Create Task dialogs, and for the New Event Filter editor. Reads the live event-log
/// channels and providers so the combos reflect the real machine instead of a hardcoded short list, and
/// resolves the friendly channel display names (e.g. "Hardware Events") that taskschd.msc shows.
/// </summary>
internal static class EventLogSources
{
    // Where classic (legacy) event sources are registered: SYSTEM\CurrentControlSet\Services\EventLog\{log}\{source}.
    private const string EventLogServicesKey = @"SYSTEM\CurrentControlSet\Services\EventLog";

    /// <summary>All event-log channel names (raw, non-localized), sorted; empty when enumeration is unavailable.</summary>
    public static List<string> GetChannels()
    {
        try
        {
            return EventLogSession.GlobalSession.GetLogNames()
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>All registered provider (source) names, sorted; empty when enumeration is unavailable.</summary>
    public static List<string> GetProviders()
    {
        try
        {
            return EventLogSession.GlobalSession.GetProviderNames()
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>
    /// Whether <paramref name="channel"/> can be used in an event-subscription query. Analytic and Debug
    /// ("direct") channels cannot: a <c>QueryList</c> <c>Select</c> over one makes the Task Scheduler
    /// service reject the task with <c>ERROR_NOT_SUPPORTED</c> ("The request is not supported") when the
    /// trigger is saved. The New Event Filter log tree omits them, matching taskschd.msc / Event Viewer,
    /// which hide Analytic and Debug logs by default. The classic Windows logs are always subscribable and
    /// are added by the caller regardless of this probe.
    /// </summary>
    public static bool IsSubscribableChannel(string channel)
    {
        try
        {
            using var config = new EventLogConfiguration(channel);
            return config.LogType is EventLogType.Administrative or EventLogType.Operational;
        }
        catch (Exception)
        {
            // Cannot read the channel config (access denied, missing manifest, …): exclude it so a
            // subscription over it cannot fail the save.
            return false;
        }
    }

    /// <summary>
    /// Maps raw channel names to their localized display names (e.g. "HardwareEvents" → "Hardware Events"),
    /// the way taskschd.msc / Event Viewer present them. Only the classic logs carry a friendly display
    /// name; modern manifest channels (e.g. "Microsoft-Windows-Kernel-Acpi/Diagnostic") are shown by their
    /// raw path, so they are intentionally absent here and callers fall back to the raw name.
    /// </summary>
    /// <remarks>
    /// The friendly names come from the classic <see cref="System.Diagnostics.EventLog.LogDisplayName"/>
    /// (the localized name Event Viewer shows for the legacy logs). This enumerates only the ~dozen classic
    /// logs, so it is cheap — unlike scanning every provider's <see cref="ProviderMetadata.LogLinks"/>, which
    /// is slow and does not surface the classic logs' display names. Still invoked off the UI thread because
    /// <see cref="System.Diagnostics.EventLog.LogDisplayName"/> performs a resource lookup per log.
    /// </remarks>
    public static Dictionary<string, string> BuildChannelDisplayNameMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var log in System.Diagnostics.EventLog.GetEventLogs())
            {
                try
                {
                    var raw = log.Log;
                    var display = log.LogDisplayName;
                    if (!string.IsNullOrWhiteSpace(raw) &&
                        !string.IsNullOrWhiteSpace(display) &&
                        !string.Equals(raw, display, StringComparison.Ordinal))
                    {
                        map.TryAdd(raw, display);
                    }
                }
                catch (Exception)
                {
                    // A log whose display name cannot be read (e.g. access denied) keeps its raw name.
                }
                finally
                {
                    log.Dispose();
                }
            }
        }
        catch (Exception)
        {
            // Classic-log enumeration unavailable — callers fall back to raw channel names.
        }
        return map;
    }

    /// <summary>
    /// The sources registered to a channel, matching taskschd.msc's Source list: the classic registry
    /// sources under <c>EventLog\{channel}\{source}</c> unioned with the channel's manifest providers.
    /// Returns <paramref name="fallback"/> when neither can be read (e.g. analytic channels).
    /// </summary>
    public static List<string> GetSourcesForChannel(string channel, List<string> fallback)
    {
        var sources = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        // Classic sources are registry subkeys of the log. Hierarchical channel paths (e.g.
        // "Microsoft-Windows-…/Operational") are manifest-only and have no such key, so skip them to
        // avoid the "/" being misread as a nested registry path.
        if (!channel.Contains('/') && !channel.Contains('\\'))
        {
            try
            {
                using var logKey = Registry.LocalMachine.OpenSubKey($@"{EventLogServicesKey}\{channel}");
                if (logKey is not null)
                {
                    foreach (var source in logKey.GetSubKeyNames())
                    {
                        if (!string.IsNullOrWhiteSpace(source))
                        {
                            sources.Add(source);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Registry unavailable — fall through to manifest providers.
            }
        }

        try
        {
            using var config = new EventLogConfiguration(channel);
            if (config.ProviderNames is { } providers)
            {
                foreach (var provider in providers)
                {
                    if (!string.IsNullOrWhiteSpace(provider))
                    {
                        sources.Add(provider);
                    }
                }
            }
        }
        catch (Exception)
        {
            // Analytic/Debug channels, or channels without read access, cannot report providers.
        }

        return sources.Count > 0 ? sources.ToList() : fallback;
    }

    /// <summary>
    /// The task categories defined by the given sources (from <see cref="ProviderMetadata.Tasks"/>),
    /// sorted; empty when none can be read. Used to populate the New Event Filter "Task category" picker,
    /// which is only meaningful when filtering by source.
    /// </summary>
    public static List<string> GetTaskCategories(IEnumerable<string> sources)
    {
        var categories = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var source in sources)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            try
            {
                using var metadata = new ProviderMetadata(source);
                foreach (var task in metadata.Tasks)
                {
                    var name = !string.IsNullOrWhiteSpace(task.DisplayName) ? task.DisplayName : task.Name;
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        categories.Add(name);
                    }
                }
            }
            catch (Exception)
            {
                // Sources whose metadata cannot be loaded contribute no categories.
            }
        }
        return categories.ToList();
    }

    /// <summary>The chosen value of an editable combo: the selected item, or the typed text.</summary>
    public static string? SelectedText(ComboBox combo)
    {
        if (combo.SelectedItem is string selected && !string.IsNullOrWhiteSpace(selected))
        {
            return selected;
        }
        return string.IsNullOrWhiteSpace(combo.Text) ? null : combo.Text.Trim();
    }
}
