using System;
using System.Globalization;
using System.Linq;
using ManagementTools.Core.Features.SystemManagement.Interop.WF;
using ManagementTools.Core.Features.SystemManagement.Infrastructure.WF;
using ManagementTools.Core.Features.SystemManagement.Models.WF.ConnectionSecurity;
using Microsoft.Management.Infrastructure;

namespace ManagementTools.Core.Features.SystemManagement.Services.WF.ConnectionSecurity;

internal static class ValueHelper
{
    internal static bool ReadEnabled(CimInstance instance)
    {
        int enabled = ReadInt32(instance, "Enabled", 1);
        return enabled != 0 && enabled != 2;
    }

    internal static bool IsAnyOrEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ||
           string.Equals(value, "Any", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(value, "*", StringComparison.OrdinalIgnoreCase);

    internal static bool ReadBool(CimInstance instance, string propertyName)
    {
        object? value = instance.CimInstanceProperties[propertyName]?.Value;
        return value switch
        {
            bool boolValue => boolValue,
            byte byteValue => byteValue != 0,
            ushort ushortValue => ushortValue != 0,
            uint uintValue => uintValue != 0,
            int intValue => intValue != 0,
            _ => false
        };
    }

    internal static int ReadInt32(CimInstance instance, string propertyName, int defaultValue)
    {
        object? value = ReadPropertyValue(instance, propertyName);
        return value is null
            ? defaultValue
            : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    internal static object? ReadPropertyValue(CimInstance instance, string propertyName)
        => instance.CimInstanceProperties[propertyName]?.Value;

    internal static void SetPropertyValueIfPresent(CimInstance instance, string propertyName, object? value)
    {
        CimProperty? property = instance.CimInstanceProperties[propertyName];
        if (property is not null)
        {
            property.Value = value;
        }
    }

    internal static ConnectionSecurityRequirement ReadRequirement(CimInstance instance, string propertyName)
    {
        return ReadInt32(instance, propertyName, 0) switch
        {
            1 => ConnectionSecurityRequirement.Request,
            2 => ConnectionSecurityRequirement.Require,
            _ => ConnectionSecurityRequirement.None
        };
    }

    internal static ConnectionSecurityMode ReadMode(CimInstance instance)
        => ReadInt32(instance, "Mode", 1) == 2
            ? ConnectionSecurityMode.Tunnel
            : ConnectionSecurityMode.Transport;

    internal static string ResolveKeyModule(object? value)
        => value switch
        {
            1 => "IKEv1",
            2 => "AuthIP",
            4 => "IKEv2",
            _ => "Default"
        };

    internal static string ResolveTunnelType(object? value)
    {
        int numericValue = value switch
        {
            byte byteValue => byteValue,
            ushort ushortValue => ushortValue,
            uint uintValue => unchecked((int)uintValue),
            int intValue => intValue,
            string stringValue when int.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedValue) => parsedValue,
            _ => 0
        };

        return numericValue switch
        {
            512 => "ClientToGateway",
            1 => "ClientToGateway",
            2 => "GatewayToClient",
            3 => "GatewayToGateway",
            _ => string.Empty
        };
    }

    internal static string ResolveInterfaceType(object? value)
    {
        uint mask = value switch
        {
            null => 0u,
            byte byteValue => byteValue,
            ushort ushortValue => ushortValue,
            uint uintValue => uintValue,
            int intValue => unchecked((uint)intValue),
            string stringValue when uint.TryParse(stringValue, out uint parsedValue) => parsedValue,
            _ => 0u
        };

        if (mask == 0)
        {
            return "Any";
        }

        List<string> values = [];
        if ((mask & 1u) != 0)
        {
            values.Add("Wired");
        }

        if ((mask & 2u) != 0)
        {
            values.Add("Wireless");
        }

        if ((mask & 4u) != 0)
        {
            values.Add("RemoteAccess");
        }

        return values.Count == 0 ? "Any" : string.Join(",", values);
    }

    internal static ushort ResolveProfilesMask(ConnectionSecurityRuleModel rule)
    {
        int mask = WindowsFirewallSupport.NormalizeProfileMask(rule.ProfilesMask == 0
            ? WindowsFirewallSupport.BuildProfileMask(rule)
            : rule.ProfilesMask);

        return mask == WindowsFirewallSupport.NetFwProfile2All
            ? (ushort)(WindowsFirewallSupport.NetFwProfile2Domain |
                       WindowsFirewallSupport.NetFwProfile2Private |
                       WindowsFirewallSupport.NetFwProfile2Public)
            : (ushort)mask;
    }

    internal static ushort ResolveModeValue(ConnectionSecurityMode mode)
        => mode == ConnectionSecurityMode.Tunnel ? (ushort)2 : (ushort)1;

    internal static ushort ResolveKeyModuleValue(string keyModule)
        => keyModule switch
        {
            "IKEv1" => 1,
            "AuthIP" => 2,
            "IKEv2" => 4,
            _ => 0
        };

    internal static ushort? ResolveTunnelTypeValue(string tunnelType, string keyModule)
    {
        bool usesIkeV2 = string.Equals(keyModule, "IKEv2", StringComparison.OrdinalIgnoreCase);
        bool clientToGateway = string.Equals(tunnelType, "ClientToGateway", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(tunnelType, "PointToSite", StringComparison.OrdinalIgnoreCase);

        return usesIkeV2 && clientToGateway ? (ushort)512 : null;
    }

    internal static uint ResolveInterfaceTypeValue(string interfaceTypes)
    {
        uint mask = 0;
        foreach (string value in WindowsFirewallSupport.ParseCsv(interfaceTypes))
        {
            switch (value)
            {
                case "Any":
                case "All":
                    return 0u;
                case "LAN":
                case "Lan":
                case "Wired":
                    mask |= 1u;
                    break;
                case "Wireless":
                    mask |= 2u;
                    break;
                case "RemoteAccess":
                    mask |= 4u;
                    break;
            }
        }

        return mask;
    }

    internal static string ResolveMainModeCryptoSetId(string value)
        => string.IsNullOrWhiteSpace(value) ? "{E5A5D32A-4BCE-4e4d-B07F-4AB1BA7E5FE1}" : value.Trim();

    internal static string ResolveQuickModeCryptoSetId(string value)
        => string.IsNullOrWhiteSpace(value) ? "{E5A5D32A-4BCE-4e4d-B07F-4AB1BA7E5FE2}" : value.Trim();

    internal static string? NormalizeOptionalString(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static string ResolveProtocolValue(string protocol)
        => string.IsNullOrWhiteSpace(protocol) || string.Equals(protocol, "Any", StringComparison.OrdinalIgnoreCase)
            ? "Any"
            : protocol.Trim();

    internal static bool SupportsPorts(string protocol)
    {
        if (string.IsNullOrWhiteSpace(protocol))
        {
            return false;
        }

        if (string.Equals(protocol, "TCP", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(protocol, "UDP", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return int.TryParse(protocol, NumberStyles.Integer, CultureInfo.InvariantCulture, out int protocolNumber) &&
               (protocolNumber == 6 || protocolNumber == 17);
    }

    internal static string[]? BuildAddressArray(string value)
    {
        string[] parts = WindowsFirewallSupport.ParseCsv(value);
        return parts.Length == 0 || parts.All(IsAnyToken)
            ? null
            : parts;
    }

    internal static string[]? BuildPortArray(string value)
    {
        string[] parts = WindowsFirewallSupport.ParseCsv(value);
        return parts.Length == 0 || parts.All(IsAnyToken)
            ? null
            : parts;
    }

    internal static bool IsAnyToken(string value)
        => string.Equals(value, "Any", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(value, "*", StringComparison.OrdinalIgnoreCase);

    internal static string JoinArray(object? value, string defaultValue = "")
    {
        return value switch
        {
            null => defaultValue,
            string stringValue when string.IsNullOrWhiteSpace(stringValue) => defaultValue,
            string stringValue => stringValue,
            string[] strings => strings.Length == 0 ? defaultValue : string.Join(", ", strings),
            Array array => string.Join(", ", array.Cast<object>().Select(item => item?.ToString()).Where(item => !string.IsNullOrWhiteSpace(item))),
            _ => value.ToString() ?? defaultValue
        };
    }
}
