using System;
using System.Collections.Generic;
using System.Globalization;
using OneMMC.Core.Localization;

namespace OneMMC.Core.Features.SystemManagement.Models.WF.Authentication;

/// <summary>
/// Normalizes authentication dialog values between localized display strings,
/// legacy dialog tags, and the canonical identifiers used for persistence.
/// </summary>
public static class AuthMethodValueMapper
{
    private const string SigningAlgorithmRsa = "RSA";
    private const string SigningAlgorithmEcdsaP256 = "ECDSA-P256";
    private const string SigningAlgorithmEcdsaP384 = "ECDSA-P384";
    private const string CertificateStoreTypeRootCa = "RootCA";
    private const string CertificateStoreTypeIntermediateCa = "IntermediateCA";

    /// <summary>
    /// Normalizes a signing algorithm value to the canonical tag used by the authentication dialogs.
    /// </summary>
    /// <param name="value">The raw value from UI state, legacy tags, or localized display text.</param>
    /// <returns>The canonical signing algorithm tag.</returns>
    public static string NormalizeSigningAlgorithmTag(string? value)
    {
        string normalizedValue = value?.Trim() ?? string.Empty;
        if (string.Equals(normalizedValue, SigningAlgorithmEcdsaP256, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedValue, "ECDSA P-256", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedValue, GetString("WF_SigningAlgorithm_EcdsaP256"), StringComparison.CurrentCulture))
        {
            return SigningAlgorithmEcdsaP256;
        }

        if (string.Equals(normalizedValue, SigningAlgorithmEcdsaP384, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedValue, "ECDSA P-384", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedValue, GetString("WF_SigningAlgorithm_EcdsaP384"), StringComparison.CurrentCulture))
        {
            return SigningAlgorithmEcdsaP384;
        }

        return SigningAlgorithmRsa;
    }

    /// <summary>
    /// Normalizes a certificate store type value to the canonical tag used by the authentication dialogs.
    /// </summary>
    /// <param name="value">The raw value from UI state, legacy tags, or localized display text.</param>
    /// <returns>The canonical certificate store type tag.</returns>
    public static string NormalizeCertificateStoreTypeTag(string? value)
    {
        string normalizedValue = value?.Trim() ?? string.Empty;
        if (string.Equals(normalizedValue, CertificateStoreTypeIntermediateCa, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedValue, "Intermediate CA", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedValue, GetString("WF_CertificateStore_IntermediateCA"), StringComparison.CurrentCulture))
        {
            return CertificateStoreTypeIntermediateCa;
        }

        return CertificateStoreTypeRootCa;
    }

    /// <summary>
    /// Converts a canonical or legacy signing algorithm value to the localized string shown in the UI.
    /// </summary>
    /// <param name="value">The raw value to convert.</param>
    /// <returns>The localized display string for the signing algorithm.</returns>
    public static string GetSigningAlgorithmDisplay(string? value)
    {
        return NormalizeSigningAlgorithmTag(value) switch
        {
            SigningAlgorithmEcdsaP256 => GetString("WF_SigningAlgorithm_EcdsaP256"),
            SigningAlgorithmEcdsaP384 => GetString("WF_SigningAlgorithm_EcdsaP384"),
            _ => GetString("WF_SigningAlgorithm_RsaDefault")
        };
    }

    /// <summary>
    /// Converts a canonical or legacy certificate store type value to the localized string shown in the UI.
    /// </summary>
    /// <param name="value">The raw value to convert.</param>
    /// <returns>The localized display string for the certificate store type.</returns>
    public static string GetCertificateStoreTypeDisplay(string? value)
    {
        return NormalizeCertificateStoreTypeTag(value) switch
        {
            CertificateStoreTypeIntermediateCa => GetString("WF_CertificateStore_IntermediateCA"),
            _ => GetString("WF_CertificateStore_RootCADefault")
        };
    }

    /// <summary>
    /// Converts a WMI signing algorithm code to the canonical dialog tag.
    /// </summary>
    /// <param name="value">The numeric signing algorithm code from the CIM proposal.</param>
    /// <returns>The canonical signing algorithm tag.</returns>
    public static string GetSigningAlgorithmTag(ushort value)
    {
        return value switch
        {
            2 => SigningAlgorithmEcdsaP256,
            3 => SigningAlgorithmEcdsaP384,
            _ => SigningAlgorithmRsa
        };
    }

    /// <summary>
    /// Converts a WMI trusted CA type code to the canonical dialog tag.
    /// </summary>
    /// <param name="value">The numeric trusted CA type code from the CIM proposal.</param>
    /// <returns>The canonical certificate store type tag.</returns>
    public static string GetCertificateStoreTypeTag(ushort value)
    {
        return value == 2 ? CertificateStoreTypeIntermediateCa : CertificateStoreTypeRootCa;
    }

    /// <summary>
    /// Builds the localized certificate details string shown in authentication method lists.
    /// </summary>
    /// <param name="certificateAuthorityName">The selected certificate authority distinguished name.</param>
    /// <param name="signingAlgorithmTag">The canonical or legacy signing algorithm value.</param>
    /// <param name="certificateStoreTypeTag">The canonical or legacy certificate store type value.</param>
    /// <param name="healthCertificatesOnly">Whether the method is restricted to health certificates only.</param>
    /// <param name="certificateMappingEnabled">Whether certificate-to-account mapping is enabled.</param>
    /// <param name="advancedCriteria">Optional advanced certificate criteria.</param>
    /// <returns>The localized details string for the authentication method.</returns>
    public static string BuildCertificateDetails(
        string? certificateAuthorityName,
        string? signingAlgorithmTag,
        string? certificateStoreTypeTag,
        bool healthCertificatesOnly,
        bool certificateMappingEnabled,
        AdvancedCertCriteriaResult? advancedCriteria)
    {
        string certificateAuthority = string.IsNullOrWhiteSpace(certificateAuthorityName)
            ? GetString("WF_AuthDetails_NotSet")
            : certificateAuthorityName.Trim();

        List<string> detailParts =
        [
            string.Format(
                CultureInfo.CurrentCulture,
                GetString("WF_AuthDetails_CertificateAuthorityFormat"),
                certificateAuthority),
            string.Format(
                CultureInfo.CurrentCulture,
                GetString("WF_AuthDetails_AlgorithmFormat"),
                GetSigningAlgorithmDisplay(signingAlgorithmTag)),
            string.Format(
                CultureInfo.CurrentCulture,
                GetString("WF_AuthDetails_StoreFormat"),
                GetCertificateStoreTypeDisplay(certificateStoreTypeTag))
        ];

        if (healthCertificatesOnly)
        {
            detailParts.Add(GetString("WF_AuthDetails_HealthCertificatesOnly"));
        }

        if (certificateMappingEnabled)
        {
            detailParts.Add(GetString("WF_AuthDetails_MappingEnabled"));
        }

        if (advancedCriteria is not null)
        {
            detailParts.Add(advancedCriteria.Summary);
        }

        return string.Join(", ", detailParts);
    }

    internal static ushort ResolveSigningAlgorithmCode(string? value)
    {
        return NormalizeSigningAlgorithmTag(value) switch
        {
            SigningAlgorithmEcdsaP256 => 2,
            SigningAlgorithmEcdsaP384 => 3,
            _ => 1
        };
    }

    internal static ushort ResolveTrustedCaTypeCode(string? value)
    {
        return NormalizeCertificateStoreTypeTag(value) == CertificateStoreTypeIntermediateCa ? (ushort)2 : (ushort)1;
    }

    private static string GetString(string key)
        => LocalizationProvider.Current.GetString(ResourceFileNames.WF, key);
}
