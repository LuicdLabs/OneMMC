using ManagementTools.Core.Features.UserSecurity.Services.AzMan;
using ManagementTools.Core.Features.UserSecurity.Services.NetworkListManager;
using ManagementTools.Core.Features.UserSecurity.Services.SecPol;
using ManagementTools.Core.Features.UserSecurity.Services.SecPol.IPSecurity;
using ManagementTools.Core.Features.UserSecurity.Services.SecPol.PublicKeyPolicies;
using ManagementTools.Core.Features.UserSecurity.Services.SecPol.SoftwareRestriction;
using ManagementTools.Core.Features.UserSecurity.Services.SecPol.SystemAudit;
using ManagementTools.Core.Features.UserSecurity.ViewModels.AzMan;
using ManagementTools.Core.Features.UserSecurity.ViewModels.NetworkListManager;
using ManagementTools.Core.Features.UserSecurity.ViewModels.SecPol;
using ManagementTools.Core.Features.UserSecurity.ViewModels.SecPol.IPSecurity;
using ManagementTools.Core.Features.UserSecurity.ViewModels.SecPol.PublicKeyPolicies;
using ManagementTools.Core.Features.UserSecurity.ViewModels.SecPol.SoftwareRestriction;
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
        services.AddTransient<IPSecurityStaticPolicyStoreService>();
        services.AddTransient<IPSecurityStaticPolicyCommandExecutor>();
        services.AddTransient<IPSecurityStaticPolicyMutationService>();
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
