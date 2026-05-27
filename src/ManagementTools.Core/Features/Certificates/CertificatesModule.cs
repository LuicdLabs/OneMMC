using Microsoft.Extensions.DependencyInjection;
using ManagementTools.Core.Features.Certificates.Services;
using ManagementTools.Core.Features.Certificates.ViewModels;

namespace ManagementTools.Core.Features.Certificates;

internal static class CertificatesModule
{
    internal static IServiceCollection AddCertificates(this IServiceCollection services)
    {
        services.AddTransient<CertificateStoreService>();
        services.AddTransient<CertificateNativeUiService>();
        services.AddTransient<CurrentUserCertificatesViewModel>();
        services.AddTransient<LocalComputerCertificatesViewModel>();

        return services;
    }
}
