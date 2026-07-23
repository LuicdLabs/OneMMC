using System;
using OneMMC.Core.Features.SystemManagement.Models.WF.ConnectionSecurity;

namespace OneMMC.Core.Features.SystemManagement.Services.WF.ConnectionSecurity;

/// <summary>
/// Maps a connection security rule's authentication-set references to the named authentication-
/// method preset shown by Windows Firewall with Advanced Security (WF.msc): Default, Computer and
/// user (Kerberos V5), Computer (Kerberos V5), User (Kerberos V5), or Advanced. The four named
/// presets use Windows' built-in authentication sets; only Advanced uses custom proposal sets.
/// Semantics verified on Windows 11 26200 against netsh consec auth1/auth2 configurations.
/// </summary>
public static class ConnectionSecurityAuthMethodResolver
{
    /// <summary>Preset tag: use the IPsec defaults for authentication.</summary>
    public const string PresetDefault = "Default";

    /// <summary>Preset tag: computer (Kerberos V5) first auth and user (Kerberos V5) second auth.</summary>
    public const string PresetComputerAndUser = "ComputerAndUser";

    /// <summary>Preset tag: computer (Kerberos V5) first auth only.</summary>
    public const string PresetComputer = "Computer";

    /// <summary>Preset tag: anonymous first auth plus user (Kerberos V5) second auth.</summary>
    public const string PresetUser = "User";

    /// <summary>Preset tag: a custom combination not covered by a named preset.</summary>
    public const string PresetAdvanced = "Advanced";

    private const string DefaultPhase1AuthSetId = "{E5A5D32A-4BCE-4e4d-B07F-4AB1BA7E5FE3}";
    private const string DefaultPhase2AuthSetId = "{E5A5D32A-4BCE-4e4d-B07F-4AB1BA7E5FE4}";
    private const string ComputerKerberosAuthSetId = "ComputerKerberos";
    private const string AnonymousAuthSetId = "Anonymous";
    private const string UserKerberosAuthSetId = "UserKerberos";
    private const string EmptyAuthSetId = "EmptySet";

    /// <summary>
    /// Resolves the named authentication-method preset a rule maps to, matching WF.msc.
    /// </summary>
    /// <param name="rule">The connection security rule to inspect.</param>
    /// <returns>One of the <c>Preset*</c> tags.</returns>
    public static string ResolvePreset(ConnectionSecurityRuleModel rule)
    {
        if (IsDefaultAuthSetReference(rule.Phase1AuthSet, phase1: true) &&
            IsDefaultAuthSetReference(rule.Phase2AuthSet, phase1: false))
        {
            return PresetDefault;
        }

        if (IsAuthSetReference(rule.Phase1AuthSet, ComputerKerberosAuthSetId) &&
            IsAuthSetReference(rule.Phase2AuthSet, UserKerberosAuthSetId))
        {
            return PresetComputerAndUser;
        }

        if (IsAuthSetReference(rule.Phase1AuthSet, ComputerKerberosAuthSetId) &&
            IsAuthSetReference(rule.Phase2AuthSet, EmptyAuthSetId))
        {
            return PresetComputer;
        }

        if (IsAuthSetReference(rule.Phase1AuthSet, AnonymousAuthSetId) &&
            IsAuthSetReference(rule.Phase2AuthSet, UserKerberosAuthSetId))
        {
            return PresetUser;
        }

        return PresetAdvanced;
    }

    /// <summary>
    /// Applies a named authentication-method preset to a rule's authentication-set references so
    /// the saved rule matches WF.msc's behavior for that preset. <see cref="PresetAdvanced"/>
    /// leaves the existing references and proposal methods untouched for the customize dialog.
    /// </summary>
    /// <param name="rule">The rule to update in place.</param>
    /// <param name="preset">One of the <c>Preset*</c> tags.</param>
    public static void ApplyPreset(ConnectionSecurityRuleModel rule, string preset)
    {
        if (string.Equals(preset, PresetAdvanced, StringComparison.Ordinal))
        {
            return;
        }

        rule.FirstAuthMethods.Clear();
        rule.SecondAuthMethods.Clear();
        rule.IsFirstAuthOptional = false;
        rule.IsSecondAuthOptional = false;

        switch (preset)
        {
            case PresetDefault:
                rule.Phase1AuthSet = DefaultPhase1AuthSetId;
                rule.Phase2AuthSet = DefaultPhase2AuthSetId;
                break;

            case PresetComputerAndUser:
                rule.Phase1AuthSet = ComputerKerberosAuthSetId;
                rule.Phase2AuthSet = UserKerberosAuthSetId;
                break;

            case PresetComputer:
                rule.Phase1AuthSet = ComputerKerberosAuthSetId;
                rule.Phase2AuthSet = EmptyAuthSetId;
                break;

            case PresetUser:
                rule.Phase1AuthSet = AnonymousAuthSetId;
                rule.Phase2AuthSet = UserKerberosAuthSetId;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(preset), preset, "Unknown authentication-method preset.");
        }
    }

    private static bool IsDefaultAuthSetReference(string? setId, bool phase1)
    {
        string id = (setId ?? string.Empty).Trim();
        if (id.Length == 0 || string.Equals(id, "Default", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string defaultId = phase1 ? DefaultPhase1AuthSetId : DefaultPhase2AuthSetId;
        return id.Contains(defaultId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAuthSetReference(string? setId, string expectedId)
    {
        string id = (setId ?? string.Empty).Trim();
        int separatorIndex = id.LastIndexOf('|');
        if (separatorIndex >= 0)
        {
            id = id[(separatorIndex + 1)..];
        }

        return string.Equals(id, expectedId, StringComparison.OrdinalIgnoreCase);
    }
}
