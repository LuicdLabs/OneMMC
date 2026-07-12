using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace OneMMC.Core.Features.PCManagement.Services.FsMgmt;

/// <summary>
/// Resolves the SMB session client identifier reported by <c>NetSessionEnum</c> to a friendly
/// computer name. The Server service usually records the client's raw transport address (for
/// example <c>[fe80::4725:c576:5cba:9c94]</c> for a link-local IPv6 peer) rather than a name, so
/// this helper maps that address back to a computer name on a best-effort basis:
/// <list type="bullet">
/// <item><description>loopback and this machine's own addresses map to the local computer name;</description></item>
/// <item><description>other addresses are resolved by reverse lookup (DNS, and LLMNR for link-local peers);</description></item>
/// <item><description>values that are already names, or that cannot be resolved, yield an empty result so
/// the caller falls back to the raw address.</description></item>
/// </list>
/// Results are cached so repeated refreshes — including the live auto-refresh timer — do not
/// repeatedly hit the resolver.
/// </summary>
internal static class SmbClientNameResolver
{
    private static readonly ConcurrentDictionary<string, CacheEntry> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan PositiveTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan NegativeTtl = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan LookupTimeout = TimeSpan.FromMilliseconds(1200);

    /// <summary>
    /// Resolves the supplied client identifiers to friendly computer names.
    /// </summary>
    /// <param name="clientNames">The raw client identifiers from the enumerated sessions.</param>
    /// <returns>
    /// A map from each supplied identifier to its resolved computer name. Entries that could not be
    /// resolved map to an empty string.
    /// </returns>
    public static async Task<IReadOnlyDictionary<string, string>> ResolveAsync(IReadOnlyCollection<string> clientNames)
    {
        var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pending = new List<string>();

        foreach (string clientName in clientNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(clientName))
            {
                continue;
            }

            if (TryGetCached(clientName, out string cached))
            {
                results[clientName] = cached;
            }
            else
            {
                pending.Add(clientName);
            }
        }

        if (pending.Count > 0)
        {
            (string ClientName, string ResolvedName)[] resolved = await Task.WhenAll(
                pending.Select(async clientName =>
                    (clientName, await ResolveSingleAsync(clientName).ConfigureAwait(false))))
                .ConfigureAwait(false);

            foreach ((string clientName, string resolvedName) in resolved)
            {
                Store(clientName, resolvedName);
                results[clientName] = resolvedName;
            }
        }

        return results;
    }

    private static async Task<string> ResolveSingleAsync(string clientName)
    {
        string address = NormalizeAddress(clientName);
        if (!IPAddress.TryParse(address, out IPAddress? ip))
        {
            // Already a computer/NetBIOS name rather than an address; nothing to resolve.
            return string.Empty;
        }

        if (IsLocalAddress(ip))
        {
            return Environment.MachineName;
        }

        return await ReverseLookupAsync(ip).ConfigureAwait(false) ?? string.Empty;
    }

    private static async Task<string?> ReverseLookupAsync(IPAddress ip)
    {
        // A scopeless IPv6 link-local address (fe80::/10) cannot be reached without an interface
        // scope id, so probe each candidate interface until reverse resolution succeeds.
        if (ip.AddressFamily == AddressFamily.InterNetworkV6 && ip.IsIPv6LinkLocal && ip.ScopeId == 0)
        {
            byte[] addressBytes = ip.GetAddressBytes();
            foreach (long scopeId in GetCandidateScopeIds())
            {
                string? scopedName = await ReverseLookupOnceAsync(new IPAddress(addressBytes, scopeId)).ConfigureAwait(false);
                if (scopedName is not null)
                {
                    return scopedName;
                }
            }

            return null;
        }

        return await ReverseLookupOnceAsync(ip).ConfigureAwait(false);
    }

    private static async Task<string?> ReverseLookupOnceAsync(IPAddress ip)
    {
        try
        {
            Task<IPHostEntry> lookupTask = Dns.GetHostEntryAsync(ip);
            Task finished = await Task.WhenAny(lookupTask, Task.Delay(LookupTimeout)).ConfigureAwait(false);
            if (finished != lookupTask)
            {
                // Timed out. Observe the eventual fault so it is never surfaced as unhandled.
                _ = lookupTask.ContinueWith(static task => _ = task.Exception, TaskScheduler.Default);
                return null;
            }

            IPHostEntry entry = await lookupTask.ConfigureAwait(false);
            return ExtractComputerName(entry.HostName);
        }
        catch (SocketException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string? ExtractComputerName(string? hostName)
    {
        if (string.IsNullOrWhiteSpace(hostName))
        {
            return null;
        }

        // getnameinfo hands back the numeric address string when no name is registered.
        string withoutScope = StripScopeId(hostName);
        if (IPAddress.TryParse(withoutScope, out _))
        {
            return null;
        }

        int firstDot = withoutScope.IndexOf('.');
        string shortName = firstDot > 0 ? withoutScope[..firstDot] : withoutScope;
        return string.IsNullOrWhiteSpace(shortName) ? null : shortName;
    }

    private static IEnumerable<long> GetCandidateScopeIds()
    {
        NetworkInterface[] adapters;
        try
        {
            adapters = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException)
        {
            adapters = [];
        }

        foreach (NetworkInterface adapter in adapters)
        {
            // Check IPv6 support up front: GetIPv6Properties() throws when a NIC has no IPv6
            // configuration, and probing that per adapter would spam first-chance exceptions.
            if (adapter.OperationalStatus != OperationalStatus.Up
                || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback
                || !adapter.Supports(NetworkInterfaceComponent.IPv6))
            {
                continue;
            }

            long index;
            try
            {
                index = adapter.GetIPProperties().GetIPv6Properties().Index;
            }
            catch (NetworkInformationException)
            {
                continue;
            }

            yield return index;
        }
    }

    private static bool IsLocalAddress(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))
        {
            return true;
        }

        NetworkInterface[] adapters;
        try
        {
            adapters = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException)
        {
            return false;
        }

        byte[] target = ip.GetAddressBytes();
        foreach (NetworkInterface adapter in adapters)
        {
            if (adapter.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            IPInterfaceProperties properties;
            try
            {
                properties = adapter.GetIPProperties();
            }
            catch (NetworkInformationException)
            {
                continue;
            }

            foreach (UnicastIPAddressInformation unicast in properties.UnicastAddresses)
            {
                // Compare address bytes (ignoring scope id) so a scopeless link-local address
                // from the Server service still matches this machine's scoped interface address.
                if (unicast.Address.AddressFamily == ip.AddressFamily
                    && target.AsSpan().SequenceEqual(unicast.Address.GetAddressBytes()))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string NormalizeAddress(string clientName)
    {
        string trimmed = clientName.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '[' && trimmed[^1] == ']')
        {
            trimmed = trimmed[1..^1];
        }

        return StripScopeId(trimmed);
    }

    private static string StripScopeId(string value)
    {
        int scopeSeparator = value.IndexOf('%');
        return scopeSeparator >= 0 ? value[..scopeSeparator] : value;
    }

    private static bool TryGetCached(string clientName, out string resolvedName)
    {
        if (Cache.TryGetValue(clientName, out CacheEntry entry) && entry.ExpiresAtUtc > DateTime.UtcNow)
        {
            resolvedName = entry.ResolvedName;
            return true;
        }

        resolvedName = string.Empty;
        return false;
    }

    private static void Store(string clientName, string resolvedName)
    {
        TimeSpan ttl = string.IsNullOrEmpty(resolvedName) ? NegativeTtl : PositiveTtl;
        Cache[clientName] = new CacheEntry(resolvedName, DateTime.UtcNow + ttl);
    }

    private readonly record struct CacheEntry(string ResolvedName, DateTime ExpiresAtUtc);
}
