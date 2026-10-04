using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace OneMMC;

/// <summary>
/// Application entry point. Replaces the XAML-generated <c>Main</c> (disabled through
/// <c>DISABLE_XAML_GENERATED_MAIN</c>) so that a second launch is redirected to the running instance
/// before any XAML, DI, or logging work. OneMMC is limited to one window until multi-window support
/// exists; see <c>doc/plan/MultiInstance.md</c>.
/// </summary>
public static class Program
{
    private const string InstanceKey = "OneMMC.MainWindow";
    private static readonly TimeSpan RedirectTimeout = TimeSpan.FromSeconds(5);

    [STAThread]
    private static int Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        if (TryRedirectToRunningInstance())
        {
            return 0;
        }

        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });

        return 0;
    }

    /// <summary>
    /// Releases the instance key so a replacement process (for example the elevated restart) does not
    /// redirect back to this one while it is shutting down.
    /// </summary>
    internal static void ReleaseInstanceKey() => AppInstance.GetCurrent().UnregisterKey();

    /// <summary>
    /// Re-claims the instance key after a replacement process was not started.
    /// </summary>
    internal static void ReclaimInstanceKey() => AppInstance.FindOrRegisterForKey(InstanceKey);

    /// <returns><see langword="true"/> when the launch was handed to the running instance.</returns>
    private static bool TryRedirectToRunningInstance()
    {
        try
        {
            AppInstance keyOwner = AppInstance.FindOrRegisterForKey(InstanceKey);
            if (keyOwner.IsCurrent)
            {
                keyOwner.Activated += (_, _) => BringMainWindowToFront();
                return false;
            }

            // Pass this launch's foreground right on, so the running instance may activate its window.
            PInvoke.AllowSetForegroundWindow(keyOwner.ProcessId);

            // The redirect must not block the STA (Windows App SDK guidance): run it on a worker thread
            // and wait with CoWaitForMultipleObjects, which keeps pumping COM.
            AppActivationArguments args = AppInstance.GetCurrent().GetActivatedEventArgs();
            var completed = new ManualResetEvent(false);
            bool redirected = false;
            _ = Task.Run(() =>
            {
                try
                {
                    keyOwner.RedirectActivationToAsync(args).AsTask().Wait();
                    redirected = true;
                }
                finally
                {
                    completed.Set();
                }
            });

            return WaitPumpingCom(completed, RedirectTimeout) && redirected;
        }
        catch (Exception)
        {
            // Fail open: a second window is better than a launch that silently does nothing.
            return false;
        }
    }

    private static void BringMainWindowToFront()
    {
        // Raised on a background thread; before the window exists there is nothing to do because the
        // starting window activates itself.
        MainWindow? window = App.MainWindowInstance;
        window?.DispatcherQueue.TryEnqueue(() =>
        {
            if (window.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
            {
                presenter.Restore();
            }

            window.Activate();
        });
    }

    private static unsafe bool WaitPumpingCom(WaitHandle waitHandle, TimeSpan timeout)
    {
        HANDLE handle = new(waitHandle.SafeWaitHandle.DangerousGetHandle());
        uint signaledIndex;
        HRESULT hr = PInvoke.CoWaitForMultipleObjects(0, (uint)timeout.TotalMilliseconds, 1, &handle, &signaledIndex);
        return hr.Succeeded;
    }
}
