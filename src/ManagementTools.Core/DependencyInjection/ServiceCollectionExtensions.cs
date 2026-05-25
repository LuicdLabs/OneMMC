using ManagementTools.Core.Abstractions.Services;
using ManagementTools.Core.Features.Certificates;
using ManagementTools.Core.Features.PCManagement;
using ManagementTools.Core.Features.PolicyManagement;
using ManagementTools.Core.Features.PrintManagement;
using ManagementTools.Core.Features.SystemManagement;
using ManagementTools.Core.Features.UserSecurity;
using ManagementTools.Core.Infrastructure.Admin;
using ManagementTools.Core.Infrastructure.PolicyStorage;
using ManagementTools.Core.Infrastructure.WindowsCapabilities;
using Microsoft.Extensions.DependencyInjection;

namespace ManagementTools.Core.DependencyInjection;

/// <summary>
/// Registers all Core-layer services, view models, and feature modules.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the ManagementTools.Core dependency graph to the service collection.
    /// </summary>
    /// <param name="services">The service collection to populate.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance for chaining.</returns>
    public static IServiceCollection AddManagementToolsCore(this IServiceCollection services)
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
