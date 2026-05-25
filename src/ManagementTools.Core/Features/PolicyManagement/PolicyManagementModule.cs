using ManagementTools.Core.Features.PolicyManagement.Services.RSoP;
using ManagementTools.Core.Features.PolicyManagement.ViewModels.GpEdit;
using ManagementTools.Core.Features.PolicyManagement.ViewModels.RSoP;
using Microsoft.Extensions.DependencyInjection;

namespace ManagementTools.Core.Features.PolicyManagement;

internal static class PolicyManagementModule
{
    internal static IServiceCollection AddPolicyManagement(this IServiceCollection services)
    {
        services.AddTransient<GroupPolicyEditorViewModel>();
        services.AddTransient<PolicyDetailsViewModel>();
        services.AddTransient<ResultantSetOfPolicyViewModel>();
        services.AddTransient<RSoPService>();

        return services;
    }
}
