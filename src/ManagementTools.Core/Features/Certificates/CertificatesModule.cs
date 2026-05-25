using Microsoft.Extensions.DependencyInjection;

namespace ManagementTools.Core.Features.Certificates;

internal static class CertificatesModule
{
    internal static IServiceCollection AddCertificates(this IServiceCollection services)
    {
        return services;
    }
}
