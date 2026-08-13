using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using OneMMC.Models;
using OneMMC.Services;
using OneMMC.Views;
using OneMMC.Views.Commons;
using OneMMC.Views.LusrMgr;
using OneMMC.Views.PerfMon;
using OneMMC.Views.PCManagement;
using OneMMC.ViewModels;
using OneMMC.Helpers;
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

namespace OneMMC
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private readonly ILogger<MainWindow> _logger;
        private readonly IAdminService _adminService;
        private readonly IMemoryDiagnostics _memoryDiagnostics;

        // Read once at startup: forcing a collection per navigation is a measurement mode, not a setting
        // users toggle mid-session. See AppSettings.MemoryProbeMode.
        private readonly bool _memoryProbeMode;

        private const double MinimumWidthForTitleBarAppTitle = 900;

        // Minimum window size in DIPs at 100% scale; converted to physical pixels at startup.
        private const int MinimumWindowWidthDips = 700;
        private const int MinimumWindowHeightDips = 325;

        // Probe metadata must stay bounded even when a regression keeps every departed page alive. The
        // entries contain weak references only, so evicting one affects diagnostics, never page lifetime.
        private const int MaximumDepartedPageProbes = 128;
        private const int MaximumLoggedPageSurvivors = 8;

        // Every PageStackEntry keeps a strong reference to the NavigationTransitionInfo it was navigated
        // with (PageStackEntry.m_pParameter's sibling TrackerPtr), so a per-navigation instance would be
        // retained for as long as the journal entry is. The type carries no per-navigation state, so one
        // shared instance serves every navigation — this is also how it is declared when set in XAML.
        private static readonly Microsoft.UI.Xaml.Media.Animation.SlideNavigationTransitionInfo SlideFromRight =
            new() { Effect = Microsoft.UI.Xaml.Media.Animation.SlideNavigationTransitionEffect.FromRight };

        private bool _welcomeDialogRequested;
        private readonly List<DepartedPageProbe> _departedPageProbes = [];
        private PendingNavigationAttempt? _activeNavigationAttempt;
        private NavigationInitiator _nextNavigationInitiator;
        private long _navigationSequence;
        private long _droppedDepartedPageProbes;
        private OverlappedPresenterState _windowState = OverlappedPresenterState.Restored;
        private int? _windowX;
        private int? _windowY;
        private int? _windowWidth;
        private int? _windowHeight;
        private bool _isProgrammaticSelectionChange;

        // Set once the unsaved-changes guard has approved closing, so the re-entrant Close() that follows
        // does not prompt a second time.
        private bool _forceClose;

        public void NavigateToPage(int index)
        {
            if (contentFrame == null) return;
            _logger.LogDebug("NavigateToPage invoked with index={Index}", index);
            var transition = SlideFromRight;

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

            UpdateBackButtonState();
        }

        public OneMMC.Localization.LocalizedStrings LocalizedStrings { get; } = OneMMC.Localization.LocalizedStrings.Instance;
        public MainWindowViewModel ViewModel { get; }

        public MainWindow()
        {
            _logger = App.GetRequiredService<ILogger<MainWindow>>();
            _adminService = App.GetRequiredService<IAdminService>();
            _memoryDiagnostics = App.GetRequiredService<IMemoryDiagnostics>();
            _memoryProbeMode = AppSettings.Load().MemoryProbeMode;
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
            // PreferredMinimum* take physical pixels; scale the 100%-DIP design minimums by the
            // startup monitor's DPI. Not re-applied on DPI change (acceptable known limitation).
            double minSizeScale = DpiScaleHelper.GetScaleForWindow(
                Win32Interop.GetWindowFromWindowId(AppWindow.Id));
            presenter.PreferredMinimumWidth = (int)Math.Round(MinimumWindowWidthDips * minSizeScale);
            presenter.PreferredMinimumHeight = (int)Math.Round(MinimumWindowHeightDips * minSizeScale);
            AppWindow.SetPresenter(presenter);

            RestoreWindowPlacement();
            AppWindow.Changed += AppWindow_Changed;
            AppWindow.Closing += AppWindow_Closing;
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
                if (_memoryProbeMode)
                {
                    contentFrame.Navigating += ContentFrame_Navigating;
                    contentFrame.NavigationFailed += ContentFrame_NavigationFailed;
                    contentFrame.NavigationStopped += ContentFrame_NavigationStopped;
                }

                contentFrame.Navigated += ContentFrame_Navigated;
            }

            DispatcherQueue.GetForCurrentThread().TryEnqueue(() =>
            {
                RunNavigation(
                    NavigationInitiator.Startup,
                    () => NavigateToMainPage("PCManagement"));
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
            RunNavigation(
                NavigationInitiator.GoBack,
                () => ViewModel.GoBackCommand.Execute(null));
        }

        private void NavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.IsSettingsSelected)
            {
                return;
            }
        }

        private async void NavigationView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (_isProgrammaticSelectionChange)
            {
                return;
            }

            // The NavigationView moves SelectedItem to the invoked item after this handler runs; capture
            // the current selection so it can be restored when the unsaved-changes guard cancels.
            var previousSelection = NavigationViewControl.SelectedItem;

            if (args.IsSettingsInvoked)
            {
                if (contentFrame?.Content is SettingsPage)
                {
                    return;
                }

                if (!await EnsureSafeToLeaveCurrentPageAsync())
                {
                    RestoreNavSelection(previousSelection);
                    return;
                }

                BreadcrumbNavigationService.ClearBreadcrumbs();
                BreadcrumbNavigationService.AddBreadcrumb(LocalizedStrings.Navigation_Settings ?? "Settings", typeof(SettingsPage));
                RunNavigation(
                    NavigationInitiator.NavigationView,
                    () => ViewModel.NavigateToSettingsCommand.Execute(null));
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

                if (!await EnsureSafeToLeaveCurrentPageAsync())
                {
                    RestoreNavSelection(previousSelection);
                    return;
                }

                string pageTitle = GetPageTitleFromTag(tag);
                BreadcrumbNavigationService.ClearBreadcrumbs();
                BreadcrumbNavigationService.AddBreadcrumb(pageTitle, targetPageType);
                RunNavigation(
                    NavigationInitiator.NavigationView,
                    () => ViewModel.NavigateToPageCommand.Execute(tag));
                UpdateBackButtonState();
            }
        }

        /// <summary>
        /// Asks the current page (when it guards unsaved edits) to resolve them before the shell mutates
        /// navigation state. Returns <see langword="true"/> when navigation may proceed.
        /// </summary>
        private async Task<bool> EnsureSafeToLeaveCurrentPageAsync()
        {
            if (contentFrame?.Content is IUnsavedChangesGuard guard && guard.HasUnsavedChanges)
            {
                if (!await guard.ConfirmLeaveAsync())
                {
                    return false;
                }

                // The shell drives the navigation next, so suppress the page's own OnNavigatingFrom prompt.
                guard.SuppressNextNavigationGuard();
            }

            return true;
        }

        // Re-selects a navigation-pane item without re-triggering navigation (used to undo the visual
        // selection move when the unsaved-changes guard cancels a pane switch).
        private void RestoreNavSelection(object? selection)
        {
            if (NavigationViewControl is null)
            {
                return;
            }

            _isProgrammaticSelectionChange = true;
            try
            {
                NavigationViewControl.SelectedItem = selection;
            }
            finally
            {
                _isProgrammaticSelectionChange = false;
            }
        }

        private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            if (_forceClose)
            {
                return;
            }

            if (contentFrame?.Content is IUnsavedChangesGuard guard && guard.HasUnsavedChanges)
            {
                // Cancel synchronously: the event args are only honored before the handler first awaits.
                args.Cancel = true;

                if (await guard.ConfirmLeaveAsync())
                {
                    guard.SuppressNextNavigationGuard();
                    _forceClose = true;
                    Close();
                }
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

        private async void MainBreadcrumb_ItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
        {
            if (args.Index >= BreadcrumbNavigationService.BreadCrumbs.Count - 1)
            {
                return;
            }

            // Capture the click target before awaiting — the event args are only valid synchronously.
            var crumb = (Breadcrumb)args.Item;
            var index = args.Index;

            // Resolve unsaved edits first so the breadcrumb trail is only truncated when the user actually
            // leaves the page; cancelling here leaves the breadcrumb (and current page) untouched.
            if (!await EnsureSafeToLeaveCurrentPageAsync())
            {
                return;
            }

            RunNavigation(
                NavigationInitiator.Breadcrumb,
                () => BreadcrumbNavigationService.NavigateFromBreadcrumb(crumb.Page, index, crumb.Parameter));
        }

        private void ContentFrame_Navigating(object sender, NavigatingCancelEventArgs e)
        {
            if (_activeNavigationAttempt is { HasNavigated: false } abandonedAttempt)
            {
                abandonedAttempt.IsInvalidated = true;
            }

            NavigationInitiator initiator = e.NavigationMode == NavigationMode.Back
                ? NavigationInitiator.GoBack
                : _nextNavigationInitiator;

            _activeNavigationAttempt = new PendingNavigationAttempt(
                contentFrame?.Content as Page,
                e.SourcePageType,
                initiator,
                ++_navigationSequence);
        }

        private void ContentFrame_NavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            InvalidateActiveNavigationAttempt();
        }

        private void ContentFrame_NavigationStopped(object sender, NavigationEventArgs e)
        {
            InvalidateActiveNavigationAttempt();
        }

        private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
        {
            PendingNavigationAttempt? completedAttempt = _activeNavigationAttempt;
            if (completedAttempt is not null)
            {
                completedAttempt.HasNavigated = true;
            }

            long completedNavigationSequence = completedAttempt?.Sequence ?? 0;
            NavigationInitiator completedNavigationInitiator =
                completedAttempt?.Initiator ?? NavigationInitiator.Other;

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

            // NavigationEventArgs.Content strongly references the destination Page. Do not carry the event
            // args into the async settled-GC state machine: if another navigation starts while that sample
            // is pending, the probe itself could keep the newly departed Page alive.
            string sourcePageTypeName = e.SourcePageType.Name;
            NavigationMode navigationMode = e.NavigationMode;
            int backStackDepth = contentFrame?.BackStack.Count ?? 0;
            int breadcrumbDepth = BreadcrumbNavigationService.BreadCrumbs.Count;

            bool enqueued = DispatcherQueue.TryEnqueue(
                () => _ = LogNavigationAfterEventAsync(
                    completedAttempt,
                    sourcePageTypeName,
                    navigationMode,
                    backStackDepth,
                    breadcrumbDepth,
                    completedNavigationSequence,
                    completedNavigationInitiator));
            if (!enqueued)
            {
                if (completedAttempt is not null)
                {
                    completedAttempt.IsInvalidated = true;
                }

                if (ReferenceEquals(_activeNavigationAttempt, completedAttempt))
                {
                    _activeNavigationAttempt = null;
                }

                _logger.LogWarning(
                    "[Memory] Navigation diagnostics could not be queued for {PageType} sequence " +
                    "{NavigationSequence}.",
                    sourcePageTypeName,
                    completedNavigationSequence);
            }
        }

        private async Task LogNavigationAfterEventAsync(
            PendingNavigationAttempt? navigationAttempt,
            string sourcePageTypeName,
            NavigationMode navigationMode,
            int backStackDepth,
            int breadcrumbDepth,
            long navigationSequence,
            NavigationInitiator initiator)
        {
            try
            {
                // This method starts from a DispatcherQueue callback after the complete Frame navigation
                // stack has unwound. The attempt contains only scalar metadata and a weak Page reference.
                if (navigationAttempt is { IsInvalidated: true })
                {
                    return;
                }

                CompleteDepartedPageProbe(navigationAttempt);

                bool settled = await LogNavigationMemoryAsync(
                    sourcePageTypeName,
                    navigationMode,
                    backStackDepth,
                    breadcrumbDepth,
                    navigationSequence,
                    initiator);
                if (_memoryProbeMode && settled)
                {
                    LogDepartedPageLifetimes(navigationSequence);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[Memory] Navigation diagnostics failed for {PageType} sequence {NavigationSequence}.",
                    sourcePageTypeName,
                    navigationSequence);
            }
        }

        /// <summary>
        /// Records a memory reading plus the two journal depths on every navigation. Together these
        /// form the session growth curve used to verify that memory no longer scales with the number
        /// of pages visited; see <c>doc/MemoryManagement.md</c>.
        /// </summary>
        private async Task<bool> LogNavigationMemoryAsync(
            string sourcePageTypeName,
            NavigationMode navigationMode,
            int backStackDepth,
            int breadcrumbDepth,
            long navigationSequence,
            NavigationInitiator initiator)
        {
            string context = $"post-nav:{sourcePageTypeName}";
            string detail = _memoryProbeMode
                ? $"backStack={backStackDepth} breadcrumbs={breadcrumbDepth} mode={navigationMode} " +
                  $"sequence={navigationSequence} initiator={initiator}"
                : $"backStack={backStackDepth} breadcrumbs={breadcrumbDepth} mode={navigationMode}";

            if (_memoryProbeMode)
            {
                return await _memoryDiagnostics.LogSettledSnapshotAsync(context, detail) is not null;
            }

            _memoryDiagnostics.LogSnapshot(context, detail);
            return false;
        }

        private void CompleteDepartedPageProbe(PendingNavigationAttempt? navigationAttempt)
        {
            if (ReferenceEquals(_activeNavigationAttempt, navigationAttempt))
            {
                _activeNavigationAttempt = null;
            }

            if (!_memoryProbeMode || navigationAttempt?.DepartedPageProbe is not { } probe)
            {
                return;
            }

            if (_departedPageProbes.Count >= MaximumDepartedPageProbes)
            {
                _departedPageProbes.RemoveAt(0);
                _droppedDepartedPageProbes++;
            }

            _departedPageProbes.Add(probe);
        }

        private void InvalidateActiveNavigationAttempt()
        {
            if (_activeNavigationAttempt is not { } navigationAttempt)
            {
                return;
            }

            navigationAttempt.IsInvalidated = true;
            _activeNavigationAttempt = null;
        }

        private void LogDepartedPageLifetimes(long settledThroughSequence)
        {
            int collectedCount = 0;
            int survivorCount = 0;
            var survivorDetails = new List<string>(MaximumLoggedPageSurvivors);
            var newRepeatedSurvivors = new List<string>();

            for (int index = _departedPageProbes.Count - 1; index >= 0; index--)
            {
                DepartedPageProbe probe = _departedPageProbes[index];
                if (probe.DepartureSequence > settledThroughSequence)
                {
                    // A later navigation can finish while an earlier async settled sample is pending. That
                    // sample's collection did not necessarily occur after this page departed.
                    continue;
                }

                if (!probe.Page.TryGetTarget(out _))
                {
                    _departedPageProbes.RemoveAt(index);
                    collectedCount++;
                    continue;
                }

                probe.SettledGcSurvivals++;
                survivorCount++;

                string detail =
                    $"{probe.PageType}#{probe.DepartureSequence}->{probe.DestinationPageType}" +
                    $"({probe.Initiator},gc={probe.SettledGcSurvivals},navAge=" +
                    $"{settledThroughSequence - probe.DepartureSequence})";

                if (survivorDetails.Count < MaximumLoggedPageSurvivors)
                {
                    survivorDetails.Add(detail);
                }

                if (probe.SettledGcSurvivals >= 2 && !probe.RepeatedSurvivalReported)
                {
                    probe.RepeatedSurvivalReported = true;
                    newRepeatedSurvivors.Add(detail);
                }
            }

            string survivors = survivorDetails.Count == 0
                ? "none"
                : string.Join(", ", survivorDetails);

            _logger.LogInformation(
                "[Memory][PageLifetime] settledThrough={NavigationSequence} tracked={TrackedCount} " +
                "collected={CollectedCount} alive={SurvivorCount} dropped={DroppedCount} survivors={Survivors}",
                settledThroughSequence,
                _departedPageProbes.Count,
                collectedCount,
                survivorCount,
                _droppedDepartedPageProbes,
                survivors);

            if (newRepeatedSurvivors.Count > 0)
            {
                _logger.LogWarning(
                    "[Memory][PageLifetime] Departed Page instances survived at least two settled GC passes: " +
                    "{Survivors}. This is a retention lead, not proof of a leak; inspect event, async, " +
                    "navigation-parameter, cache, and native references.",
                    string.Join(", ", newRepeatedSurvivors));
            }
        }

        private void RunNavigation(NavigationInitiator initiator, Action navigation)
        {
            NavigationInitiator previousInitiator = _nextNavigationInitiator;
            _nextNavigationInitiator = initiator;

            try
            {
                navigation();
            }
            finally
            {
                _nextNavigationInitiator = previousInitiator;
            }
        }

        private enum NavigationInitiator
        {
            Other,
            Startup,
            GoBack,
            Breadcrumb,
            NavigationView
        }

        private sealed class PendingNavigationAttempt
        {
            public PendingNavigationAttempt(
                Page? outgoingPage,
                Type destinationPageType,
                NavigationInitiator initiator,
                long sequence)
            {
                DepartedPageProbe = outgoingPage is null
                    ? null
                    : new DepartedPageProbe(
                        outgoingPage,
                        destinationPageType.Name,
                        initiator,
                        sequence);
                Initiator = initiator;
                Sequence = sequence;
            }

            public DepartedPageProbe? DepartedPageProbe { get; }

            public NavigationInitiator Initiator { get; }

            public long Sequence { get; }

            public bool HasNavigated { get; set; }

            public bool IsInvalidated { get; set; }
        }

        private sealed class DepartedPageProbe
        {
            public DepartedPageProbe(
                Page page,
                string destinationPageType,
                NavigationInitiator initiator,
                long departureSequence)
            {
                Page = new WeakReference<Page>(page);
                PageType = page.GetType().Name;
                DestinationPageType = destinationPageType;
                Initiator = initiator;
                DepartureSequence = departureSequence;
            }

            public WeakReference<Page> Page { get; }

            public string PageType { get; }

            public string DestinationPageType { get; }

            public NavigationInitiator Initiator { get; }

            public long DepartureSequence { get; }

            public int SettledGcSurvivals { get; set; }

            public bool RepeatedSurvivalReported { get; set; }
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
