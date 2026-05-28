using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using ManagementTools.Models;
using ManagementTools.Services;
using ManagementTools.Views;
using ManagementTools.Views.Commons;
using ManagementTools.Views.LusrMgr;
using ManagementTools.Views.PerfMon;
using ManagementTools.Views.PCManagement;
using ManagementTools.ViewModels;
using ManagementTools.Helpers;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Graphics;

// Title bar Colors
using Microsoft.UI;
using Windows.UI;
using Microsoft.Extensions.Logging;

// Debug
using System.Diagnostics;

namespace ManagementTools
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private readonly ILogger<MainWindow> _logger;
        private readonly IAdminService _adminService;
        private readonly NavigationMemoryCleanupService _navigationMemoryCleanupService;
        private const double MinimumWidthForTitleBarAppTitle = 900;
        private bool _welcomeDialogRequested;
        private OverlappedPresenterState _windowState = OverlappedPresenterState.Restored;
        private int? _windowX;
        private int? _windowY;
        private int? _windowWidth;
        private int? _windowHeight;
        private bool _isProgrammaticSelectionChange;

        public void NavigateToPage(int index)
        {
            if (contentFrame == null) return;
            _logger.LogDebug("NavigateToPage invoked with index={Index}", index);
            var transition = new Microsoft.UI.Xaml.Media.Animation.SlideNavigationTransitionInfo
            {
                Effect = Microsoft.UI.Xaml.Media.Animation.SlideNavigationTransitionEffect.FromRight
            };

            switch (index)
            {
                case 0:
                    BreadcrumbNavigationService.AddBreadcrumb(LocalizedStrings.PageTitle_DeviceManager ?? "Device Manager", typeof(DeviceManagerPage));
                    contentFrame.Navigate(typeof(DeviceManagerPage), null, transition);
                    break;
                case 1:
                    BreadcrumbNavigationService.AddBreadcrumb(LocalizedStrings.PageTitle_DiskManagement ?? "Disk Management", typeof(DiskManagementPage));
                    contentFrame.Navigate(typeof(DiskManagementPage), null, transition);
                    break;
                case 2:
                    BreadcrumbNavigationService.AddBreadcrumb(LocalizedStrings.PageTitle_Services ?? "Services", typeof(ServicesPage));
                    contentFrame.Navigate(typeof(ServicesPage), null, transition);
                    break;
                case 3:
                    BreadcrumbNavigationService.AddBreadcrumb(LocalizedStrings.PageTitle_EventViewer ?? "Event Viewer", typeof(EventViewerPage));
                    contentFrame.Navigate(typeof(EventViewerPage), null, transition);
                    break;
                case 4:
                    BreadcrumbNavigationService.AddBreadcrumb(LocalizedStrings.PageTitle_TaskScheduler ?? "Task Scheduler", typeof(TaskSchedulerPage));
                    contentFrame.Navigate(typeof(TaskSchedulerPage), null, transition);
                    break;
                case 5:
                    BreadcrumbNavigationService.AddBreadcrumb(LocalizedStrings.PageTitle_PerformanceMonitor ?? "Performance Monitor", typeof(PerformanceMonitorPage));
                    contentFrame.Navigate(typeof(PerformanceMonitorPage), null, transition);
                    break;
                case 6:
                    BreadcrumbNavigationService.AddBreadcrumb(LocalizedStrings.PageTitle_LocalUsersGroups ?? "Local Users and Groups", typeof(LocalUsersGroupsPage));
                    contentFrame.Navigate(typeof(LocalUsersGroupsPage), null, transition);
                    break;
                case 7:
                    BreadcrumbNavigationService.AddBreadcrumb(LocalizedStrings.PageTitle_SharedFolders ?? "Shared Folders", typeof(SharedFoldersPage));
                    contentFrame.Navigate(typeof(SharedFoldersPage), null, transition);
                    break;
                default:
                    _logger.LogWarning("NavigateToPage received unsupported index={Index}", index);
                    break;
            }

            BreadcrumbNavigationService.TrimNavigationHistory();
            UpdateBackButtonState();
            _navigationMemoryCleanupService.RequestCleanup();
        }

        public ManagementTools.Localization.LocalizedStrings LocalizedStrings { get; } = ManagementTools.Localization.LocalizedStrings.Instance;
        public MainWindowViewModel ViewModel { get; }

        public MainWindow()
        {
            _logger = App.GetRequiredService<ILogger<MainWindow>>();
            _adminService = App.GetRequiredService<IAdminService>();
            _navigationMemoryCleanupService = App.GetRequiredService<NavigationMemoryCleanupService>();
            _logger.LogInformation("MainWindow initializing.");

            InitializeComponent();
            App.MainWindowInstance = this;
            ExtendsContentIntoTitleBar = true;
            if (AppTitleBar != null)
            {
                SetTitleBar(AppTitleBar);
            }

            if (_adminService.IsRunningAsAdmin)
            {
                RunAsAdminItem.Visibility = Visibility.Collapsed;
            }

            _adminService.RestartRequested += () =>
            {
                Application.Current.Exit();
            };

            OverlappedPresenter presenter = OverlappedPresenter.Create();
            presenter.PreferredMinimumWidth = 700;
            presenter.PreferredMinimumHeight = 325;
            AppWindow.SetPresenter(presenter);

            RestoreWindowPlacement();
            AppWindow.Changed += AppWindow_Changed;
            Closed += MainWindow_Closed;

            BreadcrumbNavigationService.Init(NavigationViewControl, MainBreadcrumb, contentFrame!);

            var navigationService = new NavigationService(contentFrame!);
            ViewModel = new MainWindowViewModel(navigationService);

            var appWindow = AppWindow;
            if (appWindow != null && appWindow.TitleBar != null)
            {
                appWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
            }

            if (AppTitleBar != null)
            {
                AppTitleBar.RequestedTheme = App.CurrentTheme;
            }

            UpdateTitleBarColors();

            App.ThemeChanged += OnThemeChanged;

            if (contentFrame != null)
            {
                contentFrame.Navigated += ContentFrame_Navigated;
            }

            DispatcherQueue.GetForCurrentThread().TryEnqueue(() =>
            {
                NavigateToMainPage("PCManagement");
            });

            NavigationViewControl.Loaded += OnNavigationViewLoaded;

            _logger.LogInformation("MainWindow initialized.");
        }

        private void RootGrid_Loaded(object sender, RoutedEventArgs args)
        {
            UpdateAppTitleVisibility(RootGrid.ActualWidth);
        }

        private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs args)
        {
            UpdateAppTitleVisibility(args.NewSize.Width);
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            PersistWindowPlacement();
        }

        private void UpdateAppTitleVisibility(double windowWidth)
        {
            if (windowWidth <= 0)
            {
                return;
            }

            AppTitle.Visibility = windowWidth >= MinimumWidthForTitleBarAppTitle
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private async void OnNavigationViewLoaded(object sender, RoutedEventArgs args)
        {
            if (_welcomeDialogRequested)
            {
                return;
            }

            _welcomeDialogRequested = true;
            NavigationViewControl.Loaded -= OnNavigationViewLoaded;

            await ShowWelcomeDialogIfNeededAsync();
        }

        private async Task ShowWelcomeDialogIfNeededAsync()
        {
            var settings = AppSettings.Load();
            if (!App.ShouldShowWelcomeDialog(settings))
            {
                _logger.LogDebug("Welcome dialog skipped by user settings.");
                return;
            }

            await Task.Yield();

            var xamlRoot = await WaitForMainXamlRootAsync();
            if (xamlRoot is null)
            {
                _logger.LogWarning("Welcome dialog could not be shown because the main window XamlRoot is unavailable.");
                return;
            }

            try
            {
                var dialog = new WelcomeDialog
                {
                    XamlRoot = xamlRoot,
                    RequestedTheme = App.CurrentTheme
                };

                await dialog.ShowAsync();
            }
            catch (System.Runtime.InteropServices.COMException ex) when (ex.HResult == unchecked((int)0x80000019))
            {
                _logger.LogWarning(ex, "Welcome dialog could not be shown because another ContentDialog is already open.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Welcome dialog failed to show.");
            }
        }

        private async Task<XamlRoot?> WaitForMainXamlRootAsync()
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                if (NavigationViewControl.XamlRoot is not null)
                {
                    return NavigationViewControl.XamlRoot;
                }

                if (contentFrame?.XamlRoot is not null)
                {
                    return contentFrame.XamlRoot;
                }

                if (Content is FrameworkElement rootElement && rootElement.XamlRoot is not null)
                {
                    return rootElement.XamlRoot;
                }

                await Task.Delay(50);
            }

            return null;
        }

        private void NavigationView_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
        {
            ViewModel.GoBackCommand.Execute(null);
        }

        private void NavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.IsSettingsSelected)
            {
                return;
            }
        }

        private void NavigationView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (_isProgrammaticSelectionChange)
            {
                return;
            }

            if (args.IsSettingsInvoked)
            {
                if (contentFrame?.Content is SettingsPage)
                {
                    return;
                }

                BreadcrumbNavigationService.ClearBreadcrumbs();
                BreadcrumbNavigationService.AddBreadcrumb(LocalizedStrings.Navigation_Settings ?? "Settings", typeof(SettingsPage));
                ViewModel.NavigateToSettingsCommand.Execute(null);
                UpdateBackButtonState();
                return;
            }

            if (args.InvokedItemContainer is NavigationViewItem item && item.Tag is string tag)
            {
                if (tag == "RunAsAdmin")
                {
                    AdminDialogHelper.RunasAdmin();
                    return;
                }

                var targetPageType = GetPageTypeFromTag(tag);
                if (contentFrame?.Content?.GetType() == targetPageType)
                {
                    return;
                }

                string pageTitle = GetPageTitleFromTag(tag);
                BreadcrumbNavigationService.ClearBreadcrumbs();
                BreadcrumbNavigationService.AddBreadcrumb(pageTitle, targetPageType);
                ViewModel.NavigateToPageCommand.Execute(tag);
                UpdateBackButtonState();
            }
        }

        private string GetPageTitleFromTag(string tag)
        {
            return tag switch
            {
                "PCManagement" => LocalizedStrings.Navigation_PC_Management ?? "PC Management",
                "PolicyPolicies" => LocalizedStrings.Navigation_Policy_Policies ?? "Policy & Policies",
                "CertificatesCredential" => LocalizedStrings.Navigation_Certificates_Credential ?? "Certificates & Credential",
                "UserSecurity" => LocalizedStrings.Navigation_User_Security ?? "User & Security",
                "PrintManagement" => LocalizedStrings.Navigation_Print_Management ?? "Print Management",
                "SystemManagement" => LocalizedStrings.Navigation_System_Management ?? "System Management",
                _ => tag
            };
        }

        private Type GetPageTypeFromTag(string tag)
        {
            return tag switch
            {
                "PCManagement" => typeof(PCManagement),
                "PolicyPolicies" => typeof(Views.PolicyManagement.PolicyManagement),
                "CertificatesCredential" => typeof(CertificatesCredential),
                "UserSecurity" => typeof(UserSecurityPage),
                "PrintManagement" => typeof(PrintManagement),
                "SystemManagement" => typeof(SystemManagement),
                _ => typeof(PCManagement)
            };
        }

        private string? GetTagFromPageType(Type pageType)
        {
            return pageType switch
            {
                var type when type == typeof(PCManagement) => "PCManagement",
                var type when type == typeof(Views.PolicyManagement.PolicyManagement) => "PolicyPolicies",
                var type when type == typeof(CertificatesCredential) => "CertificatesCredential",
                var type when type == typeof(UserSecurityPage) => "UserSecurity",
                var type when type == typeof(PrintManagement) => "PrintManagement",
                var type when type == typeof(SystemManagement) => "SystemManagement",
                _ => null
            };
        }

        private void NavigateToMainPage(string tag)
        {
            string pageTitle = GetPageTitleFromTag(tag);
            BreadcrumbNavigationService.ClearBreadcrumbs();
            BreadcrumbNavigationService.AddBreadcrumb(pageTitle, GetPageTypeFromTag(tag));
            ViewModel.NavigateToPageCommand.Execute(tag);
            UpdateBackButtonState();
        }

        private void NavigateToSettings()
        {
            BreadcrumbNavigationService.ClearBreadcrumbs();
            BreadcrumbNavigationService.AddBreadcrumb(LocalizedStrings.Navigation_Settings ?? "Settings", typeof(SettingsPage));
            ViewModel.NavigateToSettingsCommand.Execute(null);
            UpdateBackButtonState();
        }

        private void UpdateBackButtonState()
        {
            if (NavigationViewControl != null && contentFrame != null)
            {
                NavigationViewControl.IsBackEnabled = contentFrame.CanGoBack;
            }
        }

        private void MainBreadcrumb_ItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
        {
            if (args.Index < BreadcrumbNavigationService.BreadCrumbs.Count - 1)
            {
                var crumb = (BreadcrumbNavigationService.Breadcrumb)args.Item;
                BreadcrumbNavigationService.NavigateFromBreadcrumb(crumb.Page, args.Index, crumb.Parameter);
            }
        }

        private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
        {
            BreadcrumbNavigationService.TrimNavigationHistory();
            _navigationMemoryCleanupService.RequestCleanup();

            if (NavigationViewControl != null && contentFrame != null)
            {
                NavigationViewControl.IsBackEnabled = contentFrame.CanGoBack;
            }

            if (e.NavigationMode == NavigationMode.Back)
            {
                BreadcrumbNavigationService.RestorePreviousBreadcrumbState();
            }

            if (NavigationViewControl != null)
            {
                _isProgrammaticSelectionChange = true;
                try
                {
                    if (e.SourcePageType == typeof(SettingsPage))
                    {
                        NavigationViewControl.SelectedItem = NavigationViewControl.SettingsItem;
                    }
                    else
                    {
                        var tag = GetTagFromPageType(e.SourcePageType);
                        if (!string.IsNullOrEmpty(tag))
                        {
                            var item = NavigationViewControl.MenuItems
                                .OfType<NavigationViewItem>()
                                .FirstOrDefault(menuItem => string.Equals(menuItem.Tag as string, tag, StringComparison.Ordinal));
                            if (item != null)
                            {
                                NavigationViewControl.SelectedItem = item;
                            }
                        }
                    }
                }
                finally
                {
                    _isProgrammaticSelectionChange = false;
                }
            }
        }

        private void OnThemeChanged(ElementTheme theme)
        {
            if (AppTitleBar != null)
            {
                AppTitleBar.RequestedTheme = theme;
            }

            UpdateTitleBarColors();
        }

        private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
        {
            if (sender.Presenter is not OverlappedPresenter presenter)
            {
                return;
            }

            if (presenter.State != OverlappedPresenterState.Minimized)
            {
                _windowState = presenter.State;
            }

            if (presenter.State == OverlappedPresenterState.Restored &&
                (args.DidPositionChange || args.DidSizeChange))
            {
                _windowX = sender.Position.X;
                _windowY = sender.Position.Y;
                _windowWidth = sender.Size.Width;
                _windowHeight = sender.Size.Height;
            }
        }

        private void RestoreWindowPlacement()
        {
            AppSettings settings = AppSettings.Load();
            _windowState = ParseWindowState(settings.MainWindowState);

            if (settings.MainWindowX is int x &&
                settings.MainWindowY is int y &&
                settings.MainWindowWidth is int width &&
                settings.MainWindowHeight is int height)
            {
                ApplyWindowBounds(x, y, width, height);
                return;
            }

            CaptureCurrentWindowPlacement();
        }

        private void CaptureCurrentWindowPlacement()
        {
            _windowX = AppWindow.Position.X;
            _windowY = AppWindow.Position.Y;
            _windowWidth = AppWindow.Size.Width;
            _windowHeight = AppWindow.Size.Height;
        }

        private void ApplyWindowBounds(int x, int y, int width, int height)
        {
            if (TryGetClampedWindowBounds(x, y, width, height, out int clampedX, out int clampedY, out int clampedWidth, out int clampedHeight))
            {
                AppWindow.Resize(new SizeInt32(clampedWidth, clampedHeight));
                AppWindow.Move(new PointInt32(clampedX, clampedY));

                _windowX = clampedX;
                _windowY = clampedY;
                _windowWidth = clampedWidth;
                _windowHeight = clampedHeight;
            }

            if (_windowState == OverlappedPresenterState.Maximized && AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Maximize();
            }
        }

        private bool TryGetClampedWindowBounds(int x, int y, int width, int height, out int clampedX, out int clampedY, out int clampedWidth, out int clampedHeight)
        {
            clampedX = x;
            clampedY = y;
            clampedWidth = width;
            clampedHeight = height;

            var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
            if (displayArea?.WorkArea is not RectInt32 workArea)
            {
                return width > 0 && height > 0;
            }

            clampedWidth = Math.Min(Math.Max(width, 1), workArea.Width);
            clampedHeight = Math.Min(Math.Max(height, 1), workArea.Height);

            int maxX = workArea.X + Math.Max(0, workArea.Width - clampedWidth);
            int maxY = workArea.Y + Math.Max(0, workArea.Height - clampedHeight);

            clampedX = Math.Clamp(x, workArea.X, maxX);
            clampedY = Math.Clamp(y, workArea.Y, maxY);
            return true;
        }

        private void PersistWindowPlacement()
        {
            if (_windowWidth is null || _windowHeight is null)
            {
                CaptureCurrentWindowPlacement();
            }

            AppSettings settings = AppSettings.Load();
            settings.MainWindowX = _windowX;
            settings.MainWindowY = _windowY;
            settings.MainWindowWidth = _windowWidth;
            settings.MainWindowHeight = _windowHeight;
            settings.MainWindowState = _windowState.ToString();
            settings.Save();
        }

        private static OverlappedPresenterState ParseWindowState(string? state)
        {
            return Enum.TryParse(state, ignoreCase: true, out OverlappedPresenterState parsedState)
                ? parsedState
                : OverlappedPresenterState.Restored;
        }

        private void UpdateTitleBarColors()
        {
            var titleBar = AppWindow?.TitleBar;
            if (titleBar == null) return;

            bool isDark = App.CurrentTheme == ElementTheme.Dark ||
                          (App.CurrentTheme == ElementTheme.Default &&
                           Application.Current.RequestedTheme == ApplicationTheme.Dark);

            var (fg, bg, hoverBg, pressBg, inactiveFg) = isDark
                ? (Colors.White, Colors.Transparent, Color.FromArgb(20, 255, 255, 255),
                   Color.FromArgb(30, 255, 255, 255), Color.FromArgb(128, 255, 255, 255))
                : (Colors.Black, Colors.Transparent, Color.FromArgb(20, 0, 0, 0),
                   Color.FromArgb(30, 0, 0, 0), Color.FromArgb(128, 0, 0, 0));

            titleBar.ButtonBackgroundColor = bg;
            titleBar.ButtonForegroundColor = fg;
            titleBar.ButtonHoverBackgroundColor = hoverBg;
            titleBar.ButtonHoverForegroundColor = fg;
            titleBar.ButtonPressedBackgroundColor = pressBg;
            titleBar.ButtonPressedForegroundColor = fg;
            titleBar.ButtonInactiveBackgroundColor = bg;
            titleBar.ButtonInactiveForegroundColor = inactiveFg;
        }
    }
}
