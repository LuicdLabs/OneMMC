using System;
using System.Collections.Generic;
using System.Globalization;
using OneMMC.Core.Localization;

namespace OneMMC.Core.Features.SystemManagement.Models.WF.Authentication;

public sealed class AdvancedCertCriteriaResult
{
    public string RestrictUsage { get; init; } = "No restriction";

    public IReadOnlyList<string> RequiredEkus { get; init; } = Array.Empty<string>();

    public string NameTypeTag { get; init; } = "None";

    public string CertificateName { get; init; } = string.Empty;

    public string Thumbprint { get; init; } = string.Empty;

    public bool FollowRenewal { get; init; }

    public string Summary
    {
        get
        {
            var parts = new List<string>();

            if (!string.Equals(RestrictUsage, "No restriction", StringComparison.Ordinal))
            {
                parts.Add(string.Format(
                    CultureInfo.CurrentCulture,
                    GetString("WF_AdvancedCriteria_RestrictFormat"),
                    GetRestrictUsageDisplay(RestrictUsage)));
            }

            if (RequiredEkus.Count > 0)
            {
                parts.Add(string.Format(CultureInfo.CurrentCulture, GetString("WF_AdvancedCriteria_EkuFormat"), RequiredEkus.Count));
            }

            if (!string.Equals(NameTypeTag, "None", StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(CertificateName))
            {
                parts.Add(string.Format(CultureInfo.CurrentCulture, GetString("WF_AdvancedCriteria_NameFormat"), CertificateName));
            }

            if (!string.IsNullOrWhiteSpace(Thumbprint))
            {
                parts.Add(GetString("WF_AdvancedCriteria_ThumbprintSet"));
            }

            if (FollowRenewal)
            {
                parts.Add(GetString("WF_AdvancedCriteria_FollowRenewal"));
            }

            return parts.Count == 0 ? GetString("WF_AdvancedCriteria_Configured") : string.Join(", ", parts);
        }
    }

    private static string GetRestrictUsageDisplay(string restrictUsage)
        => restrictUsage switch
        {
            "Selection only" => GetString("WF_AdvancedCriteria_RestrictSelectionOnly"),
            "Validation only" => GetString("WF_AdvancedCriteria_RestrictValidationOnly"),
            "No restriction" => GetString("WF_AdvancedCriteria_RestrictNoRestriction"),
            _ => restrictUsage
        };

    private static string GetString(string key)
        => LocalizationProvider.Current.GetString(ResourceFileNames.WF, key);
}

public sealed class AuthMethodDialogResult
{
    public string Kind { get; init; } = string.Empty;

    public string Method { get; init; } = string.Empty;

    public string Details { get; init; } = string.Empty;

    public string SigningAlgorithm { get; init; } = string.Empty;

    public string CertificateStoreType { get; init; } = string.Empty;

    public string CaDistinguishedName { get; init; } = string.Empty;

    public bool HealthCertificateOnly { get; init; }

    public bool CertificateMappingEnabled { get; init; }

    public string PresharedKey { get; init; } = string.Empty;

    public AdvancedCertCriteriaResult? AdvancedCriteria { get; init; }
}

public sealed class AuthMethodListItem
{
    public string Method { get; init; } = string.Empty;

    public string Details { get; init; } = string.Empty;

    public AuthMethodDialogResult Result { get; init; } = new();
}


