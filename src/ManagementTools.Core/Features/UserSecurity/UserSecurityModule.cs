using ManagementTools.Core.Features.UserSecurity.Services.AzMan;
using ManagementTools.Core.Features.UserSecurity.Services.NetworkListManager;
using ManagementTools.Core.Features.UserSecurity.Services.SecPol;
using ManagementTools.Core.Features.UserSecurity.Services.SecPol.SystemAudit;
using ManagementTools.Core.Features.UserSecurity.ViewModels.AzMan;
using ManagementTools.Core.Features.UserSecurity.ViewModels.NetworkListManager;
using ManagementTools.Core.Features.UserSecurity.ViewModels.SecPol;
using ManagementTools.Core.Features.UserSecurity.ViewModels.SecPol.SystemAudit;
using Microsoft.Extensions.DependencyInjection;

namespace ManagementTools.Core.Features.UserSecurity;

internal static class UserSecurityModule
{
    internal static IServiceCollection AddUserSecurity(this IServiceCollection services)
    {
        services.AddTransient<AzManService>();
        services.AddTransient<AuthorizationManagerViewModel>();
        services.AddTransient<AccountPoliciesViewModel>();
        services.AddTransient<LocalPoliciesViewModel>();
        services.AddTransient<NetworkListManagerViewModel>();
        services.AddTransient<NetworkListPolicyService>();
        services.AddSingleton<SecurityPolicyService>();
        services.AddSingleton<SystemAuditPolicyService>();
        services.AddTransient<SystemAuditAclEditorService>();
        services.AddTransient<SystemAuditViewModel>();

        return services;
    }
}
