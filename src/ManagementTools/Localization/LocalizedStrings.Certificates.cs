using ManagementTools.Core.Localization;

namespace ManagementTools.Localization
{
    /// <summary>
    /// Localized strings for certificate management pages.
    /// </summary>
    public partial class LocalizedStrings
    {
        public string Certificates_LocalComputerScopeFormat => GetResource(ResourceFileNames.Certificates, CertificateKeys.LocalComputerScopeFormat);
        public string Certificates_CurrentUserScopeFormat => GetResource(ResourceFileNames.Certificates, CertificateKeys.CurrentUserScopeFormat);
        public string Certificates_ImportStoreCommand => GetResource(ResourceFileNames.Certificates, CertificateKeys.ImportStoreCommand);
        public string Certificates_ExportStoreCommand => GetResource(ResourceFileNames.Certificates, CertificateKeys.ExportStoreCommand);
        public string Certificates_ExportItemCommand => GetResource(ResourceFileNames.Certificates, CertificateKeys.ExportItemCommand);
        public string Certificates_PropertiesCommand => GetResource(ResourceFileNames.Certificates, CertificateKeys.PropertiesCommand);
        public string Certificates_MoreButton => GetResource(ResourceFileNames.Certificates, CertificateKeys.MoreButton);
        public string Certificates_SectionCertificates => GetResource(ResourceFileNames.Certificates, CertificateKeys.SectionCertificates);
        public string Certificates_SectionCRLs => GetResource(ResourceFileNames.Certificates, CertificateKeys.SectionCRLs);
        public string Certificates_SectionCTLs => GetResource(ResourceFileNames.Certificates, CertificateKeys.SectionCTLs);
        public string Certificates_EmptyCertificates => GetResource(ResourceFileNames.Certificates, CertificateKeys.EmptyCertificates);
        public string Certificates_EmptyCRLs => GetResource(ResourceFileNames.Certificates, CertificateKeys.EmptyCRLs);
        public string Certificates_EmptyCTLs => GetResource(ResourceFileNames.Certificates, CertificateKeys.EmptyCTLs);
        public string Certificates_CertificateIssuerFormat => GetResource(ResourceFileNames.Certificates, CertificateKeys.CertificateIssuerFormat);
        public string Certificates_CertificateValidityFormat => GetResource(ResourceFileNames.Certificates, CertificateKeys.CertificateValidityFormat);
        public string Certificates_ContextValidityFormat => GetResource(ResourceFileNames.Certificates, CertificateKeys.ContextValidityFormat);
        public string Certificates_ItemHashFormat => GetResource(ResourceFileNames.Certificates, CertificateKeys.ItemHashFormat);
        public string Certificates_NotAvailable => GetResource(ResourceFileNames.Certificates, CertificateKeys.NotAvailable);
        public string Certificates_DeleteConfirmTitle => GetResource(ResourceFileNames.Certificates, CertificateKeys.DeleteConfirmTitle);
        public string Certificates_DeleteConfirmMessage => GetResource(ResourceFileNames.Certificates, CertificateKeys.DeleteConfirmMessage);
    }
}
