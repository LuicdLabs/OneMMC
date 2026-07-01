using System.Security.Cryptography.X509Certificates;
using OneMMC.Core.Features.Certificates.Services;
using Microsoft.Extensions.Logging;

namespace OneMMC.Core.Features.Certificates.ViewModels;

/// <summary>
/// View-model for the current-user certificate management page.
/// </summary>
public sealed class CurrentUserCertificatesViewModel : CertificateStoresViewModelBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CurrentUserCertificatesViewModel"/> class.
    /// </summary>
    /// <param name="certificateStoreService">The certificate store enumeration service.</param>
    /// <param name="adminService">The administrator permission service.</param>
    /// <param name="logger">The logger used for diagnostics.</param>
    public CurrentUserCertificatesViewModel(
        CertificateStoreService certificateStoreService,
        IAdminService adminService,
        ILogger<CurrentUserCertificatesViewModel> logger)
        : base(certificateStoreService, adminService, logger, StoreLocation.CurrentUser)
    {
    }
}
