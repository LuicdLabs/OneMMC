using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace OneMMC.Core.Features.PCManagement.Services.TaskSchd;

/// <summary>A named network profile that a task's network condition can require.</summary>
/// <param name="Id">The network profile GUID stored in the task's <c>&lt;NetworkSettings&gt;&lt;Id&gt;</c>.</param>
/// <param name="Name">The friendly profile name shown in the picker and stored in <c>&lt;Name&gt;</c>.</param>
public sealed record NetworkProfile(Guid Id, string Name);

/// <summary>
/// Enumerates the named network profiles offered by the Conditions tab's
/// "Start only if the following network connection is available" picker.
/// </summary>
/// <remarks>
/// taskschd.msc fills this list from the Network List Manager (<c>INetworkListManager.GetNetworks</c>),
/// pairing each network's friendly name with its profile GUID. Those same profiles are recorded under
/// <c>HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList\Profiles\{guid}</c>, so this reads
/// them from the registry instead — it avoids a COM dependency, needs no elevation, and yields the same
/// GUID/name pairs the task XML expects.
/// </remarks>
public static class NetworkProfileProvider
{
    private const string ProfilesKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList\Profiles";

    /// <summary>Returns the known network profiles, sorted by name. Empty when none can be read.</summary>
    public static IReadOnlyList<NetworkProfile> GetProfiles()
    {
        var profiles = new List<NetworkProfile>();
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(ProfilesKey);
            if (root is null)
            {
                return profiles;
            }

            foreach (var subKeyName in root.GetSubKeyNames())
            {
                if (!Guid.TryParse(subKeyName, out var id))
                {
                    continue;
                }

                using var key = root.OpenSubKey(subKeyName);
                if (key?.GetValue("ProfileName") is string name && !string.IsNullOrWhiteSpace(name))
                {
                    profiles.Add(new NetworkProfile(id, name.Trim()));
                }
            }
        }
        catch (Exception)
        {
            // Best-effort: if the profiles cannot be read, the picker falls back to "Any connection" only.
        }

        return profiles
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
