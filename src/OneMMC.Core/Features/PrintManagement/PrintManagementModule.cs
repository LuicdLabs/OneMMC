using Microsoft.Extensions.DependencyInjection;
using OneMMC.Core.Features.PrintManagement.Services;
using OneMMC.Core.Features.PrintManagement.ViewModels;

namespace OneMMC.Core.Features.PrintManagement;

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
