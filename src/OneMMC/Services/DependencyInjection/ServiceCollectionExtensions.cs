using OneMMC.Core.DependencyInjection;
using OneMMC.Localization;
using Microsoft.Extensions.DependencyInjection;
using OneMMC.Services;
using OneMMC.ViewModels;

namespace OneMMC.Services.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOneMMCApplicationServices(this IServiceCollection services)
    {
        services.AddOneMMCCore();
        services.AddSingleton<WinUIThemeService>();
        services.AddSingleton<IThemeService>(sp => sp.GetRequiredService<WinUIThemeService>());
        services.AddSingleton<BreadcrumbNavigationService>();

        services.AddSingleton<LocalizationService>();
        services.AddSingleton<NavigationService>();
        services.AddSingleton<SettingsViewModel>();

        return services;
    }
}
