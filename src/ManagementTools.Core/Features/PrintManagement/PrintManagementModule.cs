using ManagementTools.Core.Features.PrintManagement.Services.PrintManagement;
using ManagementTools.Core.Features.PrintManagement.ViewModels.PrintManagement;
using Microsoft.Extensions.DependencyInjection;

namespace ManagementTools.Core.Features.PrintManagement;

internal static class PrintManagementModule
{
    internal static IServiceCollection AddPrintManagement(this IServiceCollection services)
    {
        services.AddTransient<GpoPrinterDeploymentService>();
        services.AddTransient<PrintManagementService>();
        services.AddTransient<PrintManagementViewModel>();

        return services;
    }
}
