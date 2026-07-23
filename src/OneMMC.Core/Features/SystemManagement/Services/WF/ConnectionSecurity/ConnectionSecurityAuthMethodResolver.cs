using System;
using System.Collections.Generic;
using System.Linq;
using OneMMC.Core.Localization;
using OneMMC.Core.Features.SystemManagement.Models.WF.Authentication;
using OneMMC.Core.Features.SystemManagement.Models.WF.ConnectionSecurity;

namespace OneMMC.Core.Features.SystemManagement.Services.WF.ConnectionSecurity;

/// <summary>
/// Maps a connection security rule's authentication sets to the named authentication-method
/// preset shown by Windows Firewall with Advanced Security (WF.msc): Default, Computer and user
/// (Kerberos V5), Computer (Kerberos V5), User (Kerberos V5), or Advanced. The distinction
/// between "Default"/"User" (which reference the service default first-authentication set) and
/// "Computer"/"Computer and user" (which use an explicit first-authentication set) requires the
/// auth-set reference, not just the resolved methods — both surface Computer Kerberos as the
/// first method. Semantics verified on Windows 11 26200 against netsh consec auth1/auth2 configs.
/// </summary>
public static class ConnectionSecurityAuthMethodResolver
{
    /// <summary>Preset tag: use the IPsec defaults for authentication.</summary>
    public const string PresetDefault = "Default";

    /// <summary>Preset tag: computer (Kerberos V5) first auth and user (Kerberos V5) second auth.</summary>
    public const string PresetComputerAndUser = "ComputerAndUser";

    /// <summary>Preset tag: computer (Kerberos V5) first auth only.</summary>
    public const string PresetComputer = "Computer";

    /// <summary>Preset tag: default computer first auth plus user (Kerberos V5) second auth.</summary>
    public const string PresetUser = "User";

    /// <summary>Preset tag: a custom combination not covered by a named preset.</summary>
    public const string PresetAdvanced = "Advanced";

    private const string DefaultPhase1AuthSetId = "{E5A5D32A-4BCE-4e4d-B07F-4AB1BA7E5FE3}";
    private const string DefaultPhase2AuthSetId = "{E5A5D32A-4BCE-4e4d-B07F-4AB1BA7E5FE4}";

    /// <summary>
    /// Resolves the named authentication-method preset a rule maps to, matching WF.msc.
    /// </summary>
    /// <param name="rule">The connection security rule to inspect.</param>
    /// <returns>One of the <c>Preset*</c> tags.</returns>
    public static string ResolvePreset(ConnectionSecurityRuleModel rule)
    {
        bool firstUsesDefault = IsDefaultAuthSetReference(rule.Phase1AuthSet, phase1: true);
        List<string> first = rule.FirstAuthMethods.Select(item => item.Result.Kind).ToList();
        List<string> second = rule.SecondAuthMethods.Select(item => item.Result.Kind).ToList();

        bool secondEmpty = second.Count == 0;
        bool secondIsUserKerberos = second is ["UserKerberos"];

        if (firstUsesDefault)
        {
            // First authentication is the IPsec default (references the service default P1 set).
            if (secondEmpty)
            {
                return PresetDefault;
            }

            return secondIsUserKerberos ? PresetUser : PresetAdvanced;
        }

        // First authentication is an explicit set.
        if (first is ["ComputerKerberos"])
        {
            if (secondEmpty)
            {
                return PresetComputer;
            }

            if (secondIsUserKerberos)
            {
                return PresetComputerAndUser;
            }
        }

        return PresetAdvanced;
    }

    /// <summary>
    /// Applies a named authentication-method preset to a rule's authentication sets so the saved
    /// rule matches WF.msc's behavior for that preset. <see cref="PresetAdvanced"/> leaves the
    /// existing first/second methods untouched (they are managed by the customize dialog).
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
            case PresetComputerAndUser:
                rule.FirstAuthMethods.Add(CreateComputerKerberos());
                rule.SecondAuthMethods.Add(CreateUserKerberos());
                break;

            case PresetComputer:
                rule.FirstAuthMethods.Add(CreateComputerKerberos());
                break;

            case PresetUser:
                // Default computer first authentication (empty -> service default P1 set) plus an
                // explicit user Kerberos second authentication, matching netsh auth2=userkerb.
                rule.SecondAuthMethods.Add(CreateUserKerberos());
                break;

            // PresetDefault: both cleared -> the rule references the IPsec default auth sets.
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

    private static AuthMethodListItem CreateComputerKerberos()
        => new()
        {
            Method = GetString("WF_AuthMethod_ComputerKerberos"),
            Details = GetString("WF_AuthDetails_KerberosAuthentication"),
            Result = new AuthMethodDialogResult
            {
                Kind = "ComputerKerberos",
                Method = GetString("WF_AuthMethod_ComputerKerberos"),
                Details = GetString("WF_AuthDetails_KerberosAuthentication")
            }
        };

    private static AuthMethodListItem CreateUserKerberos()
        => new()
        {
            Method = GetString("WF_AuthMethod_UserKerberos"),
            Details = GetString("WF_AuthDetails_KerberosAuthentication"),
            Result = new AuthMethodDialogResult
            {
                Kind = "UserKerberos",
                Method = GetString("WF_AuthMethod_UserKerberos"),
                Details = GetString("WF_AuthDetails_KerberosAuthentication")
            }
        };

    private static string GetString(string key)
        => LocalizationProvider.Current.GetString(ResourceFileNames.WF, key);
}
