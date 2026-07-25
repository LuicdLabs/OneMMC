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
        services.AddSingleton<SettingsViewModel>();

        // Deliberately NOT registered — all three were dead registrations that nothing ever resolved,
        // and two of them could not be constructed at all, which trips ValidateOnBuild in Debug:
        //   NavigationService          - its constructor needs the content Frame, which is not a service;
        //                                MainWindow creates it directly (see MainWindow.xaml.cs).
        //   LocalizationService        - has a private constructor; consumers use LocalizationService.Instance.
        //   BreadcrumbNavigationService - an entirely static class; there is nothing to inject.

        return services;
    }
}
