using ManagementTools.Core.DependencyInjection;
using ManagementTools.Localization;
using Microsoft.Extensions.DependencyInjection;
using ManagementTools.Services;
using ManagementTools.ViewModels;

namespace ManagementTools.Services.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddManagementToolsApplicationServices(this IServiceCollection services)
    {
        services.AddManagementToolsCore();
        services.AddSingleton<WinUIThemeService>();
        services.AddSingleton<IThemeService>(sp => sp.GetRequiredService<WinUIThemeService>());
        services.AddSingleton<BreadcrumbNavigationService>();
        services.AddSingleton<LocalizationService>();
        services.AddSingleton<NavigationService>();
        services.AddSingleton<SettingsViewModel>();

        return services;
    }
}
