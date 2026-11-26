using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

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
    public NavigationService(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        _frame = frame;
        _frameRegistration = TrackFrame(frame);
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
    /// <returns>A registration that must be disposed with the frame owner.</returns>
    public static IDisposable TrackFrame(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return new FrameNavigationRegistration(frame);
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

    private static void ScheduleReclamation()
    {
        var reclamationCts = new CancellationTokenSource();
        CancellationTokenSource? previousCts;

        lock (ReclamationLock)
        {
            previousCts = s_reclamationCts;
            s_reclamationCts = reclamationCts;
            previousCts?.Cancel();
        }
        _ = ReclaimAfterNavigationAsync(reclamationCts);
    }

    private static async Task ReclaimAfterNavigationAsync(CancellationTokenSource reclamationCts)
    {
        try
        {
            await Task.Delay(NavigationReclamationDelayMilliseconds, reclamationCts.Token).ConfigureAwait(false);

            // The departed page's native XAML tree is released only after a GC runs the reference
            // tracker walk, and the GC does not see native memory, so it rarely runs on its own.
            // The process uses the Segment Heap (app.manifest), which returns the freed native memory
            // to the OS; no working-set trim is needed. See doc/MemoryManagement.md.
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
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
        private Frame? _frame;
        private bool _reclaimOnNavigated;

        public FrameNavigationRegistration(Frame frame)
        {
            _frame = frame;
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
            ScheduleReclamation();
        }
    }
}
