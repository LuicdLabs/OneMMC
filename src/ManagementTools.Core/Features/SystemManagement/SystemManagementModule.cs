using ManagementTools.Core.Features.SystemManagement.Services.ComExp;
using ManagementTools.Core.Features.SystemManagement.Services.TPM;
using ManagementTools.Core.Features.SystemManagement.ViewModels.ComExp;
using ManagementTools.Core.Features.SystemManagement.ViewModels.TPM;
using ManagementTools.Core.Features.SystemManagement.Services.WF.ConnectionSecurity;
using ManagementTools.Core.Features.SystemManagement.Services.WF.Monitoring;
using ManagementTools.Core.Features.SystemManagement.Services.WF.Profiles;
using ManagementTools.Core.Features.SystemManagement.Services.WF.Rules;
using ManagementTools.Core.Features.SystemManagement.ViewModels.WF.Rules;
using Microsoft.Extensions.DependencyInjection;

namespace ManagementTools.Core.Features.SystemManagement;

internal static class SystemManagementModule
{
    internal static IServiceCollection AddSystemManagement(this IServiceCollection services)
    {
        services.AddSingleton<ComponentServicesManager>();
        services.AddTransient<TPMService>();
        services.AddTransient<TPMManagerViewModel>();
        services.AddTransient<DcomConfigViewModel>();
        services.AddTransient<DtcStatisticsViewModel>();
        services.AddTransient<DtcTransactionListViewModel>();
        services.AddTransient<RunningProcessesViewModel>();

        services.AddSingleton<ConnectionSecurityService>();
        services.AddSingleton<FirewallMonitoringService>();
        services.AddSingleton<WindowsFirewallProfileService>();
        services.AddSingleton<FirewallAppContainerService>();
        services.AddSingleton<WindowsFirewallRuleChangeService>();
        services.AddSingleton<WindowsFirewallRuleService>();
        services.AddSingleton<WindowsFirewallService>();
        services.AddTransient<FirewallRuleViewModel>();

        return services;
    }
}
