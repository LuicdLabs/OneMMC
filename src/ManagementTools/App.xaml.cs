using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using ManagementTools.Models;
using ManagementTools.Services.Logging;
using ManagementTools.Views.Commons;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage;

namespace ManagementTools
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        public static IServiceProvider Services { get; private set; } = default!;
        public static ElementTheme CurrentTheme { get; private set; } = ElementTheme.Default;
        public static event Action<ElementTheme>? ThemeChanged;
        public static MainWindow? MainWindowInstance { get; set; }

        private static bool _isErrorDialogShowing = false;
        private readonly ILogger<App> _logger;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            Services = LoggingBootstrapper.BuildServiceProvider();
            _logger = GetRequiredService<ILogger<App>>();
            UiLogger.Configure(GetRequiredService<ILoggerFactory>());

            InitializeComponent();
            UnhandledException += App_UnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            _logger.LogInformation("Application initialized and logging is configured.");
            UiLogger.LogDebug("[App] Logging initialized via Microsoft.Extensions.Logging + Serilog.");
        }

        public static T GetRequiredService<T>() where T : notnull
        {
            return Services.GetRequiredService<T>();
        }

        private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            _logger.LogError(e.Exception, "Unhandled UI exception captured.");

            // Show error dialog to user
            ShowErrorDialog(e.Exception);

            // Mark as handled to prevent app crash
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object? sender, System.UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                _logger.LogCritical(ex, "AppDomain unhandled exception. IsTerminating={IsTerminating}", e.IsTerminating);
                return;
            }

            _logger.LogCritical("AppDomain unhandled non-exception object. IsTerminating={IsTerminating}", e.IsTerminating);
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            _logger.LogError(e.Exception, "Unobserved task exception captured.");
            e.SetObserved();
        }

        private async void ShowErrorDialog(Exception ex)
        {
            if (_isErrorDialogShowing) return;

            if (MainWindowInstance?.Content is FrameworkElement frameworkElement)
            {
                _isErrorDialogShowing = true;
                try
                {
                    var localized = ManagementTools.Localization.LocalizedStrings.Instance;
                    var dialog = new ContentDialog
                    {
                        Title = localized.App_Error_Title,
                        Content = string.Format(localized.App_Error_MessageFormat, ex.Message),
                        CloseButtonText = localized.Common_OKButton,
                        XamlRoot = frameworkElement.XamlRoot,
                        Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                        RequestedTheme = App.CurrentTheme
                    };

                    await dialog.ShowAsync();
                }
                catch (System.Runtime.InteropServices.COMException comEx) when (comEx.HResult == unchecked((int)0x80000019))
                {
                    _logger.LogWarning(comEx, "Could not show error dialog because another ContentDialog is already open. Original error: {ErrorMessage}", ex.Message);
                }
                finally
                {
                    _isErrorDialogShowing = false;
                }
            }
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            _logger.LogInformation("Application launched.");

            // Initialize localization service first
            ManagementTools.Localization.UILocalizationProvider.InitializeCoreLocalization();

            // Initialize UI-thread marshaling for PerformanceCounterInfo without exposing WinUI types to Core.
            var dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            ManagementTools.Core.Features.PCManagement.Models.PerfMon.PerformanceCounterInfo.InitializeDispatcher(action =>
            {
                if (dispatcherQueue.HasThreadAccess)
                {
                    action();
                    return;
                }

                dispatcherQueue.TryEnqueue(() => action());
            });

            // Fix: Use MainWindowInstance instead of _window
            MainWindowInstance = new MainWindow();
            MainWindowInstance.Closed += MainWindowInstance_Closed;
            MainWindowInstance.Activate();

            // Set theme when application starts
            ApplyThemeSetting();
        }

        private void MainWindowInstance_Closed(object sender, WindowEventArgs args)
        {
            _logger.LogInformation("Main window closed. Shutting down logger.");
            LoggingBootstrapper.Shutdown();
        }

        // Add method to set theme
        private void ApplyThemeSetting()
        {
            // Read theme from local settings
            string savedTheme = GetSavedTheme();
            if (!string.IsNullOrEmpty(savedTheme))
            {
                switch (savedTheme)
                {
                    case "Light":
                        SetAppTheme(ElementTheme.Light);
                        break;
                    case "Dark":
                        SetAppTheme(ElementTheme.Dark);
                        break;
                    default:
                        SetAppTheme(ElementTheme.Default);
                        break;
                }
            }
            else
            {
                SetAppTheme(ElementTheme.Default);
            }
        }

        // Add method to actually set application theme
        public void SetAppTheme(ElementTheme theme)
        {
            CurrentTheme = theme;
            if (MainWindowInstance?.Content is FrameworkElement rootElement)
            {
                rootElement.RequestedTheme = theme;
            }
            // Re-apply MainWindow TitleBar style
            if (MainWindowInstance != null)
            {
                var appWindow = MainWindowInstance.AppWindow;
                if (appWindow != null && appWindow.TitleBar != null)
                {
                    appWindow.TitleBar.PreferredHeightOption = Microsoft.UI.Windowing.TitleBarHeightOption.Tall;
                }
            }
            SaveTheme(theme);
            ThemeChanged?.Invoke(theme);
        }

        /// <summary>
        /// Gets a value indicating whether the welcome dialog should be shown.
        /// </summary>
        public static bool ShouldShowWelcomeDialog(AppSettings settings)
        {
            if (!settings.WelcomeDialogHidden)
            {
                return true;
            }

            if (!string.IsNullOrEmpty(settings.WelcomeDialogDismissedDate) &&
                DateTime.TryParse(settings.WelcomeDialogDismissedDate, out var dismissedDate))
            {
                return (DateTime.Now - dismissedDate).TotalDays >= 30;
            }

            return false;
        }

        private string GetSavedTheme()
        {
            try
            {
                var settings = AppSettings.Load();
                return settings.Theme;
            }
            catch { }
            return string.Empty;
        }

        private void SaveTheme(ElementTheme theme)
        {
            try
            {
                var settings = AppSettings.Load();
                settings.Theme = theme switch
                {
                    ElementTheme.Light => "Light",
                    ElementTheme.Dark => "Dark",
                    _ => "Default"
                };
                settings.Save();
            }
            catch { }
        }
    }
}
