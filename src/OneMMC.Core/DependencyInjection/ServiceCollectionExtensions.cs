using OneMMC.Core.Abstractions.Services;
using OneMMC.Core.Features.Certificates;
using OneMMC.Core.Features.PCManagement;
using OneMMC.Core.Features.PolicyManagement;
using OneMMC.Core.Features.PrintManagement;
using OneMMC.Core.Features.SystemManagement;
using OneMMC.Core.Features.UserSecurity;
using OneMMC.Core.Infrastructure.Admin;
using OneMMC.Core.Infrastructure.PolicyStorage;
using OneMMC.Core.Infrastructure.WindowsCapabilities;
using Microsoft.Extensions.DependencyInjection;

namespace OneMMC.Core.DependencyInjection;

/// <summary>
/// Registers all Core-layer services, view models, and feature modules.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the OneMMC.Core dependency graph to the service collection.
    /// </summary>
    /// <param name="services">The service collection to populate.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance for chaining.</returns>
    public static IServiceCollection AddOneMMCCore(this IServiceCollection services)
    {
        services.AddSingleton<AdminService>();
        services.AddSingleton<IAdminService>(sp => sp.GetRequiredService<AdminService>());
        services.AddSingleton<IFileDialogService, AppSdkFileDialogService>();
        services.AddSingleton<AclEditorService>();
        services.AddSingleton<CertificateAuthorityPickerService>();
        services.AddSingleton<IconPickerService>();
        services.AddSingleton<LocalPolicyFileStore>();

        services.AddCertificates();
        services.AddPCManagement();
        services.AddPolicyManagement();
        services.AddPrintManagement();
        services.AddSystemManagement();
        services.AddUserSecurity();

        return services;
    }
}
