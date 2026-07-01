using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Management.Infrastructure;
using OneMMC.Core.Features.SystemManagement.Interop.WF;
using OneMMC.Core.Features.SystemManagement.Models.WF.Authentication;
using OneMMC.Core.Features.SystemManagement.Models.WF.ConnectionSecurity;
using OneMMC.Core.Features.SystemManagement.Models.WF.Monitoring;
using OneMMC.Core.Features.SystemManagement.Models.WF.Profiles;
using OneMMC.Core.Features.SystemManagement.Models.WF.Rules;
using OneMMC.Core.Features.SystemManagement.Services.WF.ConnectionSecurity;
using OneMMC.Core.Features.SystemManagement.Services.WF.Rules;
using OneMMC.Core.Features.SystemManagement.Infrastructure.WF;
using OneMMC.Core.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace OneMMC.Core.Features.SystemManagement.Services.WF.Monitoring;

public class FirewallMonitoringService
{
    private readonly WindowsFirewallRuleService _firewallRuleService;
    private readonly ConnectionSecurityService _connectionSecurityService;

    public FirewallMonitoringService(WindowsFirewallRuleService firewallRuleService, ConnectionSecurityService connectionSecurityService)
    {
        _firewallRuleService = firewallRuleService;
        _connectionSecurityService = connectionSecurityService;
    }

    public IReadOnlyList<FirewallRuleModel> GetActiveFirewallRules()
    {
        return _firewallRuleService.GetRules(FirewallRuleDirection.Inbound)
            .Concat(_firewallRuleService.GetRules(FirewallRuleDirection.Outbound))
            .Where(rule => rule.Enabled)
            .OrderBy(rule => rule.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<ConnectionSecurityRuleModel> GetConnectionSecurityRules()
        => _connectionSecurityService.GetRules();

    /// <summary>
    /// Gets active Main Mode SA entries.
    /// By default duplicates are collapsed because WMI often surfaces parallel/rekey rows for the same association.
    /// </summary>
    public IReadOnlyList<MainModeSecurityAssociationModel> GetMainModeSecurityAssociations(bool deduplicate = true)
    {
        using CimSession session = CimSession.Create(null);
        IEnumerable<CimInstance> instances = session
            .EnumerateInstances(WindowsFirewallSupport.StandardCimNamespace, "MSFT_NetMainModeSA")
            .Where(IsDisplayableMainMode);

        IEnumerable<MainModeSecurityAssociationModel> items = instances.Select(instance => new MainModeSecurityAssociationModel
        {
            LocalEndpoint = instance.CimInstanceProperties["LocalEndpoint"]?.Value?.ToString() ?? string.Empty,
            RemoteEndpoint = instance.CimInstanceProperties["RemoteEndpoint"]?.Value?.ToString() ?? string.Empty,
            MainMode = ResolveKeyModule(instance.CimInstanceProperties["KeyModule"]?.Value),
            FirstAuthMethod = instance.CimInstanceProperties["LocalFirstId"]?.Value?.ToString() ?? string.Empty,
            SecondAuthMethod = instance.CimInstanceProperties["LocalSecondId"]?.Value?.ToString() ?? string.Empty,
            CipherAlgorithm = ResolveCipher(instance.CimInstanceProperties["CipherAlgorithm"]?.Value),
            HashAlgorithm = ResolveHash(instance.CimInstanceProperties["HashAlgorithm"]?.Value),
            KeyExchange = ResolveGroup(instance.CimInstanceProperties["GroupId"]?.Value)
        }).Select(item =>
        {
            item.Name = BuildMainModeDisplayName(item.LocalEndpoint, item.RemoteEndpoint);
            item.Description = item.MainMode;
            return item;
        });

        List<MainModeSecurityAssociationModel> ordered = items
            .OrderBy(item => item.LocalEndpoint, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.RemoteEndpoint, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.MainMode, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (!deduplicate)
        {
            return ordered;
        }

        return DeduplicateMainMode(ordered);
    }

    /// <summary>
    /// Gets active Quick Mode SA entries.
    /// By default duplicates are collapsed because WMI can surface paired rows for the same effective association.
    /// </summary>
    public IReadOnlyList<QuickModeSecurityAssociationModel> GetQuickModeSecurityAssociations(bool deduplicate = true)
    {
        using CimSession session = CimSession.Create(null);
        IEnumerable<CimInstance> instances = session
            .EnumerateInstances(WindowsFirewallSupport.StandardCimNamespace, "MSFT_NetQuickModeSA")
            .Where(IsInboundAssociation);

        IEnumerable<QuickModeSecurityAssociationModel> items = instances.Select(instance =>
        {
            (string ahIntegrity, string espIntegrity, string espEncryption) = ResolveQuickModeAlgorithms(instance);
            return new QuickModeSecurityAssociationModel
            {
                Name = instance.CimInstanceProperties["LocalEndpoint"]?.Value?.ToString() ?? string.Empty,
                Description = instance.CimInstanceProperties["RemoteEndpoint"]?.Value?.ToString() ?? string.Empty,
                LocalAddress = instance.CimInstanceProperties["LocalEndpoint"]?.Value?.ToString() ?? string.Empty,
                LocalPort = ResolvePort(instance.CimInstanceProperties["LocalPort"]?.Value),
                RemoteAddress = instance.CimInstanceProperties["RemoteEndpoint"]?.Value?.ToString() ?? string.Empty,
                RemotePort = ResolvePort(instance.CimInstanceProperties["RemotePort"]?.Value),
                Protocol = ResolveProtocol(instance.CimInstanceProperties["IpProtocol"]?.Value),
                AhIntegrity = ahIntegrity,
                EspIntegrity = espIntegrity,
                EspEncryption = espEncryption
            };
        });

        List<QuickModeSecurityAssociationModel> ordered = items
            .OrderBy(item => item.LocalAddress, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.RemoteAddress, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Protocol, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (!deduplicate)
        {
            return ordered;
        }

        return DeduplicateQuickMode(ordered);
    }

    private static IReadOnlyList<MainModeSecurityAssociationModel> DeduplicateMainMode(
        IEnumerable<MainModeSecurityAssociationModel> items)
    {
        return items
            .GroupBy(BuildMainModeKey)
            .Select(group => group.First())
            .ToList();
    }

    private static IReadOnlyList<QuickModeSecurityAssociationModel> DeduplicateQuickMode(
        IEnumerable<QuickModeSecurityAssociationModel> items)
    {
        return items
            .GroupBy(BuildQuickModeKey)
            .Select(group => group.First())
            .ToList();
    }

    private static MainModeKey BuildMainModeKey(MainModeSecurityAssociationModel item)
        => new(
            NormalizeKeyPart(item.LocalEndpoint),
            NormalizeKeyPart(item.RemoteEndpoint),
            NormalizeKeyPart(item.MainMode),
            NormalizeKeyPart(item.FirstAuthMethod),
            NormalizeKeyPart(item.SecondAuthMethod),
            NormalizeKeyPart(item.CipherAlgorithm),
            NormalizeKeyPart(item.HashAlgorithm),
            NormalizeKeyPart(item.KeyExchange));

    private static QuickModeKey BuildQuickModeKey(QuickModeSecurityAssociationModel item)
        => new(
            NormalizeKeyPart(item.LocalAddress),
            NormalizeKeyPart(item.RemoteAddress),
            NormalizeKeyPart(item.LocalPort),
            NormalizeKeyPart(item.RemotePort),
            NormalizeKeyPart(item.Protocol),
            NormalizeKeyPart(item.AhIntegrity),
            NormalizeKeyPart(item.EspIntegrity),
            NormalizeKeyPart(item.EspEncryption));

    private static string NormalizeKeyPart(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private readonly record struct MainModeKey(
        string LocalEndpoint,
        string RemoteEndpoint,
        string MainMode,
        string FirstAuthMethod,
        string SecondAuthMethod,
        string CipherAlgorithm,
        string HashAlgorithm,
        string KeyExchange);

    private readonly record struct QuickModeKey(
        string LocalAddress,
        string RemoteAddress,
        string LocalPort,
        string RemotePort,
        string Protocol,
        string AhIntegrity,
        string EspIntegrity,
        string EspEncryption);

    private static string ResolveCipher(object? value)
    {
        int code = value is null ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture);
        return code switch
        {
            2 => "DES",
            6 => "3DES",
            65001 => "AES-128",
            65002 => "AES-192",
            65003 => "AES-256",
            65004 => "AES-GCM-128",
            65005 => "AES-GCM-192",
            65006 => "AES-GCM-256",
            _ => "None"
        };
    }

    private static string ResolveHash(object? value)
    {
        int code = value is null ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture);
        return code switch
        {
            2 => "MD5",
            3 => "SHA-1",
            65001 => "SHA-256",
            65002 => "SHA-384",
            65003 => "AES-GMAC-128",
            65004 => "AES-GMAC-192",
            65005 => "AES-GMAC-256",
            _ => "None"
        };
    }

    private static string ResolveGroup(object? value)
    {
        int code = value is null ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture);
        return code switch
        {
            1 => "DH Group 1",
            2 => "DH Group 2",
            14 => "DH Group 14",
            19 => "DH Group 19",
            20 => "DH Group 20",
            24 => "DH Group 24",
            _ => "None"
        };
    }

    private static string ResolveKeyModule(object? value)
    {
        int code = value is null ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture);
        return code switch
        {
            0 => "IKEv1",
            1 => "AuthIP",
            2 => "IKEv2",
            _ => "Default"
        };
    }

    private static string ResolvePort(object? value)
    {
        int code = value is null ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture);
        return code == 0 ? "Any" : code.ToString(CultureInfo.InvariantCulture);
    }

    private static string ResolveProtocol(object? value)
    {
        int code = value is null ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture);
        if (code == 0)
        {
            return "Any";
        }

        FirewallRuleProtocol protocol = WindowsFirewallSupport.ResolveProtocol(code);
        return protocol == FirewallRuleProtocol.Custom ? code.ToString(CultureInfo.InvariantCulture) : protocol.ToString();
    }

    private static bool IsInboundAssociation(CimInstance instance)
    {
        if (instance.CimInstanceProperties["InboundDirection"]?.Value is bool inbound)
        {
            return inbound;
        }

        return true;
    }

    private static bool IsDisplayableMainMode(CimInstance instance)
    {
        // Rule-based SA entries usually carry policy linkage, while VPN IKEv2 SA entries do not.
        if (!string.IsNullOrWhiteSpace(TryGetString(instance, "MmTargetName")) ||
            !string.IsNullOrWhiteSpace(TryGetString(instance, "IkePolicyKey")) ||
            TryGetUInt64(instance, "ExtendedFilterId") is > 0)
        {
            return true;
        }

        if (TryGetInt32(instance, "KeyModule") is int keyModule)
        {
            // EffectiveKeyModule enum: 0=IkeV1, 1=AuthIP, 2=IkeV2.
            return keyModule is not 2;
        }

        return false;
    }

    private static (string AhIntegrity, string EspIntegrity, string EspEncryption) ResolveQuickModeAlgorithms(CimInstance instance)
    {
        const int IpsecTransformAh = 1;
        const int IpsecTransformEspAuth = 2;
        const int IpsecTransformEspCipher = 3;
        const int IpsecTransformEspAuthAndCipher = 4;
        const int IpsecTransformEspAuthFw = 5;

        string ahIntegrity = "None";
        string espIntegrity = "None";
        string espEncryption = "None";

        ApplyTransform(
            TryGetInt32(instance, "FirstTransformType") ?? 0,
            TryGetInt32(instance, "FirstIntegrityAlgorithm"),
            TryGetInt32(instance, "FirstCipherAlgorithm"),
            ref ahIntegrity,
            ref espIntegrity,
            ref espEncryption);

        ApplyTransform(
            TryGetInt32(instance, "SecondTransformType") ?? 0,
            TryGetInt32(instance, "SecondIntegrityAlgorithm"),
            TryGetInt32(instance, "SecondCipherAlgorithm"),
            ref ahIntegrity,
            ref espIntegrity,
            ref espEncryption);

        return (ahIntegrity, espIntegrity, espEncryption);

        void ApplyTransform(
            int transformType,
            int? integrityCode,
            int? cipherCode,
            ref string currentAhIntegrity,
            ref string currentEspIntegrity,
            ref string currentEspEncryption)
        {
            switch (transformType)
            {
                case IpsecTransformAh:
                    currentAhIntegrity = PickPreferredValue(currentAhIntegrity, ResolveHash(integrityCode));
                    break;
                case IpsecTransformEspAuth:
                case IpsecTransformEspAuthFw:
                    currentEspIntegrity = PickPreferredValue(currentEspIntegrity, ResolveHash(integrityCode));
                    break;
                case IpsecTransformEspCipher:
                    currentEspEncryption = PickPreferredValue(currentEspEncryption, ResolveCipher(cipherCode));
                    break;
                case IpsecTransformEspAuthAndCipher:
                    currentEspIntegrity = PickPreferredValue(currentEspIntegrity, ResolveHash(integrityCode));
                    currentEspEncryption = PickPreferredValue(currentEspEncryption, ResolveCipher(cipherCode));
                    break;
            }
        }
    }

    private static string PickPreferredValue(string currentValue, string candidateValue)
    {
        return string.Equals(currentValue, "None", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(candidateValue, "None", StringComparison.OrdinalIgnoreCase)
            ? candidateValue
            : currentValue;
    }

    private static string BuildMainModeDisplayName(string localEndpoint, string remoteEndpoint)
    {
        string local = string.IsNullOrWhiteSpace(localEndpoint) ? "Unknown" : localEndpoint.Trim();
        string remote = string.IsNullOrWhiteSpace(remoteEndpoint) ? "Unknown" : remoteEndpoint.Trim();
        return $"{local} <-> {remote}";
    }

    private static string? TryGetString(CimInstance instance, string propertyName)
        => instance.CimInstanceProperties[propertyName]?.Value?.ToString();

    private static int? TryGetInt32(CimInstance instance, string propertyName)
    {
        object? value = instance.CimInstanceProperties[propertyName]?.Value;
        if (value is null)
        {
            return null;
        }

        try
        {
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return null;
        }
        catch (InvalidCastException)
        {
            return null;
        }
    }

    private static ulong? TryGetUInt64(CimInstance instance, string propertyName)
    {
        object? value = instance.CimInstanceProperties[propertyName]?.Value;
        if (value is null)
        {
            return null;
        }

        try
        {
            return Convert.ToUInt64(value, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return null;
        }
        catch (InvalidCastException)
        {
            return null;
        }
    }
}




