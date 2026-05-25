using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.AccessControl;
using System.Security.Principal;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Authentication;
using ManagementTools.Core.Features.SystemManagement.Models.WF.ConnectionSecurity;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Monitoring;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Profiles;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Rules;

namespace ManagementTools.Core.Features.SystemManagement.Infrastructure.WF;

public static partial class WindowsFirewallSupport
{
    private static string NormalizeSingleAddressValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string normalizedValue = value.Trim();
        if (string.Equals(normalizedValue, "Any", StringComparison.OrdinalIgnoreCase))
        {
            return "*";
        }

        int separatorIndex = normalizedValue.IndexOf('/');
        if (separatorIndex <= 0 || separatorIndex >= normalizedValue.Length - 1)
        {
            return normalizedValue;
        }

        string addressPart = normalizedValue[..separatorIndex].Trim();
        string maskOrPrefixPart = normalizedValue[(separatorIndex + 1)..].Trim();
        if (!IPAddress.TryParse(addressPart, out IPAddress? address))
        {
            return normalizedValue;
        }

        if (!TryParsePrefixLength(address, maskOrPrefixPart, out int prefixLength))
        {
            return normalizedValue;
        }

        IPAddress networkAddress = ApplyNetworkMask(address, prefixLength);
        return $"{networkAddress}/{prefixLength.ToString(CultureInfo.InvariantCulture)}";
    }

    private static bool TryParsePrefixLength(IPAddress address, string value, out int prefixLength)
    {
        prefixLength = 0;
        int maxPrefixLength = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedPrefixLength))
        {
            if (parsedPrefixLength >= 0 && parsedPrefixLength <= maxPrefixLength)
            {
                prefixLength = parsedPrefixLength;
                return true;
            }

            return false;
        }

        if (address.AddressFamily != AddressFamily.InterNetwork ||
            !IPAddress.TryParse(value, out IPAddress? maskAddress) ||
            maskAddress.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        return TryConvertIpv4MaskToPrefixLength(maskAddress, out prefixLength);
    }

    private static bool TryConvertIpv4MaskToPrefixLength(IPAddress maskAddress, out int prefixLength)
    {
        prefixLength = 0;
        bool zeroSeen = false;

        foreach (byte octet in maskAddress.GetAddressBytes())
        {
            for (int bitIndex = 7; bitIndex >= 0; bitIndex--)
            {
                bool bitSet = (octet & (1 << bitIndex)) != 0;
                if (zeroSeen && bitSet)
                {
                    prefixLength = 0;
                    return false;
                }

                if (bitSet)
                {
                    prefixLength++;
                }
                else
                {
                    zeroSeen = true;
                }
            }
        }

        return true;
    }

    private static IPAddress ApplyNetworkMask(IPAddress address, int prefixLength)
    {
        byte[] bytes = address.GetAddressBytes();
        int remainingBits = prefixLength;

        for (int index = 0; index < bytes.Length; index++)
        {
            if (remainingBits >= 8)
            {
                remainingBits -= 8;
                continue;
            }

            if (remainingBits <= 0)
            {
                bytes[index] = 0;
                continue;
            }

            int mask = (0xFF << (8 - remainingBits)) & 0xFF;
            bytes[index] = (byte)(bytes[index] & mask);
            remainingBits = 0;
        }

        return new IPAddress(bytes);
    }

    private static Dictionary<string, string> BuildCapabilitySidLookup()
    {
        Dictionary<string, string> lookup = new(StringComparer.OrdinalIgnoreCase);

        foreach ((string sid, string name) in WellKnownCapabilityEntries)
        {
            lookup[name] = sid;
            lookup[$"CAPABILITY\\{name}"] = sid;
            lookup[$"APPLICATION PACKAGE AUTHORITY\\{name}"] = sid;
        }

        lookup["APPLICATION PACKAGE AUTHORITY\\ALL APPLICATION PACKAGES"] = AllApplicationPackagesSid;
        return lookup;
    }

    private static Dictionary<string, string> BuildCapabilityNameLookup()
    {
        Dictionary<string, string> lookup = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string sid, string name) in WellKnownCapabilityEntries)
        {
            lookup[sid] = name;
        }

        return lookup;
    }

    private static bool TryResolveWellKnownCapabilitySid(string value, out string? sid)
        => WellKnownCapabilitySidByName.TryGetValue(value.Trim(), out sid);
}
