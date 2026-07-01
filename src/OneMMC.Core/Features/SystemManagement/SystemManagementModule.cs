using OneMMC.Core.Features.SystemManagement.Services.ComExp;
using OneMMC.Core.Features.SystemManagement.Services.TPM;
using OneMMC.Core.Features.SystemManagement.ViewModels.ComExp;
using OneMMC.Core.Features.SystemManagement.ViewModels.TPM;
using OneMMC.Core.Features.SystemManagement.Services.WF.ConnectionSecurity;
using OneMMC.Core.Features.SystemManagement.Services.WF.Monitoring;
using OneMMC.Core.Features.SystemManagement.Services.WF.Profiles;
using OneMMC.Core.Features.SystemManagement.Services.WF.Rules;
using OneMMC.Core.Features.SystemManagement.ViewModels.WF.Rules;
using Microsoft.Extensions.DependencyInjection;

namespace OneMMC.Core.Features.SystemManagement;

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
