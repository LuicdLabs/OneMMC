using System.Security.Cryptography;

namespace OneMMC.Core.Features.UserSecurity.Models.SecPol.PublicKeyPolicies;

/// <summary>
/// Resolves display names for peer-trust enhanced key usage (EKU) OIDs.
/// </summary>
/// <remarks>
/// The Certificate Path Validation Settings UI in Windows shows the CryptoAPI friendly name for a
/// purpose (for example "Client Authentication" for 1.3.6.1.5.5.7.3.2) rather than the raw OID, while
/// still persisting the OID to the policy registry. This helper centralizes that mapping so the editor
/// and the read-only details summary render identical text.
/// </remarks>
public static class PublicKeyPolicyPurposeDisplay
{
    /// <summary>
    /// Gets the friendly display name for an enhanced key usage OID, falling back to the OID itself
    /// when no CryptoAPI friendly name is registered.
    /// </summary>
    /// <param name="oid">The enhanced key usage OID (for example "1.3.6.1.5.5.7.3.2").</param>
    /// <returns>The CryptoAPI friendly name if known; otherwise the trimmed OID.</returns>
    public static string GetDisplayName(string? oid)
    {
        string trimmed = oid?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        string? friendlyName = new Oid(trimmed).FriendlyName;
        return string.IsNullOrWhiteSpace(friendlyName) ? trimmed : friendlyName;
    }
}
