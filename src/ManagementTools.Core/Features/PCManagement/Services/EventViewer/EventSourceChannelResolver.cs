using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using Microsoft.Win32;

namespace ManagementTools.Core.Features.PCManagement.Services.EventViewer;

/// <summary>
/// Resolves the event channel(s) a source/provider writes to, so a "By source" event filter can be
/// authored the way Event Viewer authors it — one <c>&lt;Select Path="channel"&gt;</c> per channel a
/// provider belongs to, rather than assuming everything lives in the Application log.
/// </summary>
/// <remarks>
/// Modern (manifest) providers expose their channels through <see cref="ProviderMetadata.LogLinks"/>.
/// Classic/legacy sources (e.g. ".NET Runtime") have no manifest, so their owning log is found via the
/// registry where they are registered:
/// <c>HKLM\SYSTEM\CurrentControlSet\Services\EventLog\{log}\{source}</c>. Falls back to the Application
/// log, which is where unregistered classic sources report.
/// </remarks>
public static class EventSourceChannelResolver
{
    private const string EventLogServicesKey = @"SYSTEM\CurrentControlSet\Services\EventLog";
    private const string DefaultChannel = "Application";

    /// <summary>Returns the channel(s) the given source/provider writes to (never empty).</summary>
    public static IReadOnlyList<string> GetChannels(string providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            return [DefaultChannel];
        }

        var manifestChannels = TryGetManifestChannels(providerName);
        if (manifestChannels.Count > 0)
        {
            return manifestChannels;
        }

        var classicLog = TryFindClassicSourceLog(providerName);
        return classicLog is not null ? [classicLog] : [DefaultChannel];
    }

    private static IReadOnlyList<string> TryGetManifestChannels(string providerName)
    {
        try
        {
            using var metadata = new ProviderMetadata(providerName);
            return metadata.LogLinks
                .Select(link => link.LogName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception)
        {
            // Classic sources and providers without read access throw here; fall back to the registry.
            return [];
        }
    }

    private static string? TryFindClassicSourceLog(string source)
    {
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(EventLogServicesKey);
            if (root is null)
            {
                return null;
            }

            foreach (var logName in root.GetSubKeyNames())
            {
                using var logKey = root.OpenSubKey(logName);
                using var sourceKey = logKey?.OpenSubKey(source);
                if (sourceKey is not null)
                {
                    return logName;
                }
            }
        }
        catch (Exception)
        {
            // Registry unavailable — fall back to the default channel.
        }

        return null;
    }
}
