using OneMMC.Core.Features.PolicyManagement.Services.RSoP;
using OneMMC.Core.Features.PolicyManagement.ViewModels.GpEdit;
using OneMMC.Core.Features.PolicyManagement.ViewModels.RSoP;
using Microsoft.Extensions.DependencyInjection;

namespace OneMMC.Core.Features.PolicyManagement;

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
