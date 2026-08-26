using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Win32;

namespace OneMMC.Services;

/// <summary>
/// Navigates the application shell and coordinates reclamation whenever a tracked frame replaces a page.
/// </summary>
public sealed partial class NavigationService : INavigationService, IDisposable
{
    private const int NavigationReclamationDelayMilliseconds = 750;

    private static readonly object ReclamationLock = new();
    private static CancellationTokenSource? s_reclamationCts;

    private readonly Frame _frame;
    private readonly IDisposable _frameRegistration;
    private readonly Dictionary<string, Type> _pageMap;

    /// <summary>
    /// Initializes navigation for the supplied shell frame.
    /// </summary>
    /// <param name="frame">The frame that hosts shell pages.</param>
    /// <param name="logger">The logger used for native reclamation failures.</param>
    public NavigationService(Frame frame, ILogger<NavigationService> logger)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(logger);

        _frame = frame;
        _frameRegistration = TrackFrame(frame, logger);
        _pageMap = new Dictionary<string, Type>
        {
            { "PCManagement", typeof(Views.PCManagement.PCManagement) },
            { "PolicyPolicies", typeof(Views.PolicyManagement.PolicyManagement) },
            { "CertificatesCredential", typeof(Views.CertificatesCredential) },
            { "UserSecurity", typeof(Views.UserSecurityPage) },
            { "PrintManagement", typeof(Views.PrintManagement) },
            { "SystemManagement", typeof(Views.SystemManagement) },
            { "SettingsPage", typeof(Views.SettingsPage) }
        };
    }

    /// <inheritdoc />
    public void Navigate(string pageKey)
    {
        if (_pageMap.TryGetValue(pageKey, out Type? pageType))
        {
            _frame.Navigate(pageType);
        }
    }

    /// <inheritdoc />
    public void GoBack()
    {
        if (_frame.CanGoBack)
        {
            _frame.GoBack();
        }
    }

    /// <summary>
    /// Tracks page replacement on an additional frame, including nested frames owned by a page.
    /// </summary>
    /// <param name="frame">The frame whose departed pages should trigger reclamation.</param>
    /// <param name="logger">The logger used for native reclamation failures.</param>
    /// <returns>A registration that must be disposed with the frame owner.</returns>
    public static IDisposable TrackFrame(Frame frame, ILogger<NavigationService> logger)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(logger);
        return new FrameNavigationRegistration(frame, logger);
    }

    /// <summary>
    /// Cancels a pending reclamation operation during application shutdown.
    /// </summary>
    public static void CancelPendingReclamation()
    {
        CancellationTokenSource? reclamationCts;
        lock (ReclamationLock)
        {
            reclamationCts = s_reclamationCts;
            s_reclamationCts = null;
            reclamationCts?.Cancel();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _frameRegistration.Dispose();
    }

    private static void ScheduleReclamation(ILogger<NavigationService> logger)
    {
        var reclamationCts = new CancellationTokenSource();
        CancellationTokenSource? previousCts;

        lock (ReclamationLock)
        {
            previousCts = s_reclamationCts;
            s_reclamationCts = reclamationCts;
            previousCts?.Cancel();
        }
        _ = ReclaimAfterNavigationAsync(reclamationCts, logger);
    }

    private static async Task ReclaimAfterNavigationAsync(
        CancellationTokenSource reclamationCts,
        ILogger<NavigationService> logger)
    {
        try
        {
            await Task.Delay(NavigationReclamationDelayMilliseconds, reclamationCts.Token).ConfigureAwait(false);

            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

            if (!PInvoke.EmptyWorkingSet(PInvoke.GetCurrentProcess()))
            {
                logger.LogWarning(
                    "Post-navigation working-set trim failed with Win32 error {ErrorCode}.",
                    System.Runtime.InteropServices.Marshal.GetLastPInvokeError());
            }
        }
        catch (OperationCanceledException) when (reclamationCts.IsCancellationRequested)
        {
        }
        finally
        {
            lock (ReclamationLock)
            {
                if (ReferenceEquals(s_reclamationCts, reclamationCts))
                {
                    s_reclamationCts = null;
                }

                reclamationCts.Dispose();
            }
        }
    }

    private sealed partial class FrameNavigationRegistration : IDisposable
    {
        private readonly ILogger<NavigationService> _logger;
        private Frame? _frame;
        private bool _reclaimOnNavigated;

        public FrameNavigationRegistration(Frame frame, ILogger<NavigationService> logger)
        {
            _frame = frame;
            _logger = logger;
            frame.Navigating += OnNavigating;
            frame.Navigated += OnNavigated;
        }

        public void Dispose()
        {
            Frame? frame = Interlocked.Exchange(ref _frame, null);
            if (frame is null)
            {
                return;
            }

            frame.Navigating -= OnNavigating;
            frame.Navigated -= OnNavigated;
        }

        private void OnNavigating(object sender, NavigatingCancelEventArgs e)
        {
            _reclaimOnNavigated = _frame?.Content is Page;
        }

        private void OnNavigated(object sender, NavigationEventArgs e)
        {
            if (!_reclaimOnNavigated)
            {
                return;
            }

            _reclaimOnNavigated = false;
            ScheduleReclamation(_logger);
        }
    }
}
