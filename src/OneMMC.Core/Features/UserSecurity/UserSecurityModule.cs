using OneMMC.Core.Features.UserSecurity.Services.AzMan;
using OneMMC.Core.Features.UserSecurity.Services.SecPol;
using OneMMC.Core.Features.UserSecurity.Services.SecPol.IPSecurity;
using OneMMC.Core.Features.UserSecurity.Services.SecPol.NetworkListManager;
using OneMMC.Core.Features.UserSecurity.Services.SecPol.PublicKeyPolicies;
using OneMMC.Core.Features.UserSecurity.Services.SecPol.SoftwareRestriction;
using OneMMC.Core.Features.UserSecurity.Services.SecPol.SystemAudit;
using OneMMC.Core.Features.UserSecurity.ViewModels.AzMan;
using OneMMC.Core.Features.UserSecurity.ViewModels.SecPol;
using OneMMC.Core.Features.UserSecurity.ViewModels.SecPol.IPSecurity;
using OneMMC.Core.Features.UserSecurity.ViewModels.SecPol.NetworkListManager;
using OneMMC.Core.Features.UserSecurity.ViewModels.SecPol.PublicKeyPolicies;
using OneMMC.Core.Features.UserSecurity.ViewModels.SecPol.SoftwareRestriction;
using OneMMC.Core.Features.UserSecurity.ViewModels.SecPol.SystemAudit;
using Microsoft.Extensions.DependencyInjection;

namespace OneMMC.Core.Features.UserSecurity;

internal static class UserSecurityModule
{
    internal static IServiceCollection AddUserSecurity(this IServiceCollection services)
    {
        // AzMan service owns a dedicated STA COM thread plus a cache of opened authorization stores, so
        // it is a singleton — same rationale as ITaskSchedulerService in PCManagementModule. As a
        // transient it was recreated (and leaked, along with its STA thread) on every drill-down and
        // back-navigation through the AzMan pages.
        services.AddSingleton<AzManService>();
        services.AddTransient<AuthorizationManagerViewModel>();
        services.AddTransient<AccountPoliciesViewModel>();
        services.AddTransient<LocalPoliciesViewModel>();
        services.AddTransient<NetworkListManagerViewModel>();
        services.AddTransient<NetworkListPolicyService>();
        services.AddTransient<IPSecurityStoreService>();
        services.AddTransient<IPSecurityCommandExecutor>();
        services.AddTransient<IPSecurityMutationService>();
        services.AddTransient<IPSecurityPolicyService>();
        services.AddTransient<IPSecurityPoliciesViewModel>();
        services.AddTransient<PublicKeyPolicyService>();
        services.AddTransient<PublicKeyPoliciesViewModel>();
        services.AddTransient<SoftwareRestrictionPolicyService>();
        services.AddTransient<SoftwareRestrictionViewModel>();
        services.AddSingleton<SecurityPolicyService>();
        services.AddSingleton<SystemAuditPolicyService>();
        services.AddTransient<SystemAuditAclEditorService>();
        services.AddTransient<SystemAuditViewModel>();

        return services;
    }
}
