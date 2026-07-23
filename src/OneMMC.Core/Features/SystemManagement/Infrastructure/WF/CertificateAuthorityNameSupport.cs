using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace OneMMC.Core.Features.SystemManagement.Infrastructure.WF;

/// <summary>
/// Validates certification authority names destined for the Windows Firewall IPsec
/// configuration surfaces (WMI <c>MSFT_NetIKECertAuthProposal.TrustedCA</c>).
/// </summary>
public static class CertificateAuthorityNameSupport
{
    /// <summary>
    /// MS-FASP: the CA name must be shorter than 10,000 characters.
    /// </summary>
    private const int MaxTrustedCaNameLength = 9999;

    /// <summary>
    /// Determines whether the value is a certification authority name the Windows Firewall
    /// service accepts: per MS-FASP (FW_AUTH_SUITE) it must be a non-empty CERT_X500_NAME_STR
    /// string of fewer than 10,000 characters without the <c>|</c> multi-CA separator.
    /// </summary>
    /// <param name="name">The certification authority distinguished name to validate.</param>
    /// <returns><see langword="true"/> when the name satisfies the firewall's constraints.</returns>
    public static bool IsValidTrustedCaName(string? name)
    {
        string? trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxTrustedCaNameLength || trimmed.Contains('|'))
        {
            return false;
        }

        try
        {
            // Forward (no Reversed flag) parse matches the CertStrToName default parameterization
            // the firewall service uses to re-encode TrustedCA.
            _ = new X500DistinguishedName(trimmed, X500DistinguishedNameFlags.UseCommas);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
