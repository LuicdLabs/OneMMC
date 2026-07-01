using Microsoft.Extensions.DependencyInjection;
using OneMMC.Core.Features.Certificates.Services;
using OneMMC.Core.Features.Certificates.ViewModels;

namespace OneMMC.Core.Features.Certificates;

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
