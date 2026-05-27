namespace ManagementTools.Core.Features.Certificates.Models;

/// <summary>
/// Identifies the kind of item stored in a logical certificate store section.
/// </summary>
public enum CertificateEntryKind
{
    Certificate = 0,
    CertificateRevocationList = 1,
    CertificateTrustList = 2
}
