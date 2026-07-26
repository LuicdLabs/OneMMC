using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Registry;

namespace OneMMC.Core.Features.SystemManagement.Services.WF.Monitoring;

/// <summary>
/// Watches the Windows Firewall policy registry stores and raises debounced change notifications.
/// </summary>
/// <remarks>
/// This used to subscribe to WMI intrinsic events (<c>__InstanceOperationEvent WITHIN</c>) over the
/// <c>root\StandardCimv2</c> firewall classes, which is not a viable mechanism here. To diff a polled
/// intrinsic event WMI caches a full instance snapshot of the watched class, and that cost is charged
/// against the per-user <c>__ArbitratorConfiguration.PollingMemoryPerUser</c> quota (5 MB by default).
/// On a machine with an ordinary rule count (~500 rules) a single <c>MSFT_NetFirewallRule</c> subscription
/// already approaches that quota on its own, so registrations failed nondeterministically with
/// <c>WBEM_E_QUOTA_VIOLATION</c> (0x8004106C); the poll interval made no difference because the cost tracks
/// snapshot size, not frequency. Registry change notification costs nothing, reports changes immediately,
/// and a single subtree registration covers rules, connection security rules, authentication sets, crypto
/// sets and per-profile settings.
/// </remarks>
public sealed partial class WindowsFirewallRuleChangeService : IDisposable
{
    private static readonly TimeSpan ChangeDebounceInterval = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan WatchThreadJoinTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Reports subkey add/remove (rules are stored as values, sets as subkeys) and value writes.
    /// <c>REG_NOTIFY_THREAD_AGNOSTIC</c> keeps the registration alive independently of the thread that
    /// armed it, so a re-arm from any thread stays valid.
    /// </summary>
    private const REG_NOTIFY_FILTER WatchFilter =
        REG_NOTIFY_FILTER.REG_NOTIFY_CHANGE_NAME |
        REG_NOTIFY_FILTER.REG_NOTIFY_CHANGE_LAST_SET |
        REG_NOTIFY_FILTER.REG_NOTIFY_THREAD_AGNOSTIC;

    /// <summary>
    /// Registry subtrees backing the firewall configuration, relative to <c>HKEY_LOCAL_MACHINE</c>. The
    /// persistent store holds local rules, connection security rules, authentication/crypto sets and the
    /// per-profile settings; the policy store only exists once Group Policy has delivered firewall settings.
    /// </summary>
    private static readonly string[] WatchedKeyPaths =
    [
        @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy",
        @"SOFTWARE\Policies\Microsoft\WindowsFirewall"
    ];

    private readonly ILogger<WindowsFirewallRuleChangeService> _logger;
    private readonly object _syncRoot = new();
    private readonly object _debounceLock = new();
    private List<WatchedKey> _watchedKeys = [];
    private ManualResetEvent? _stopSignal;
    private Thread? _watchThread;
    private Timer? _debounceTimer;
    private bool _isDebounceEnabled;
    private int _subscriberCount;
    private volatile bool _isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsFirewallRuleChangeService"/> class.
    /// </summary>
    public WindowsFirewallRuleChangeService()
        : this(NullLogger<WindowsFirewallRuleChangeService>.Instance)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsFirewallRuleChangeService"/> class.
    /// </summary>
    /// <param name="logger">Logger used for diagnostics.</param>
    public WindowsFirewallRuleChangeService(ILogger<WindowsFirewallRuleChangeService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Occurs after Windows reports firewall configuration creation, deletion, or modification.
    /// </summary>
    public event EventHandler? RulesChanged;

    /// <summary>
    /// Gets a value indicating whether at least one policy key is currently being watched.
    /// </summary>
    public bool IsWatching { get; private set; }

    /// <summary>
    /// Subscribes to debounced Windows Firewall configuration changes.
    /// </summary>
    /// <param name="handler">Handler invoked when Windows reports a firewall configuration change.</param>
    /// <returns>A disposable subscription that releases the handler and stops monitoring when unused.</returns>
    public IDisposable Subscribe(EventHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_syncRoot)
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(WindowsFirewallRuleChangeService));
            }

            RulesChanged += handler;
            _subscriberCount++;
            StartCore();

            return new FirewallChangeSubscription(this, handler);
        }
    }

    /// <summary>
    /// Starts monitoring Windows Firewall configuration changes.
    /// </summary>
    public void Start()
    {
        lock (_syncRoot)
        {
            StartCore();
        }
    }

    /// <summary>
    /// Stops monitoring Windows Firewall configuration changes.
    /// </summary>
    public void Stop()
    {
        lock (_syncRoot)
        {
            StopCore();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            StopCore();
        }

        GC.SuppressFinalize(this);
    }

    private void StartCore()
    {
        if (_isDisposed || IsWatching)
        {
            return;
        }

        List<WatchedKey> watchedKeys = OpenWatchedKeys();
        if (watchedKeys.Count == 0)
        {
            _logger.LogWarning("Windows Firewall change monitoring is unavailable; no policy key could be watched.");
            return;
        }

        _watchedKeys = watchedKeys;
        _stopSignal = new ManualResetEvent(false);

        lock (_debounceLock)
        {
            _isDebounceEnabled = true;
        }

        WatchedKey[] keys = [.. watchedKeys];
        ManualResetEvent stopSignal = _stopSignal;
        _watchThread = new Thread(() => WatchLoop(keys, stopSignal))
        {
            IsBackground = true,
            Name = "OneMMC.FirewallPolicyWatcher"
        };

        IsWatching = true;
        _watchThread.Start();
    }

    private List<WatchedKey> OpenWatchedKeys()
    {
        List<WatchedKey> watchedKeys = [];

        foreach (string keyPath in WatchedKeyPaths)
        {
            RegistryKey? key = null;
            try
            {
                // KEY_READ includes KEY_NOTIFY, so a read-only open is enough to arm the notification and
                // monitoring works without elevation.
                key = Registry.LocalMachine.OpenSubKey(keyPath, writable: false);
                if (key is null)
                {
                    _logger.LogDebug("Windows Firewall policy key HKLM\\{KeyPath} does not exist; skipping it.", keyPath);
                    continue;
                }

                var watchedKey = new WatchedKey(keyPath, key);
                key = null;
                if (TryArmNotification(watchedKey))
                {
                    watchedKeys.Add(watchedKey);
                }
                else
                {
                    watchedKey.Dispose();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to open Windows Firewall policy key HKLM\\{KeyPath} for change monitoring.", keyPath);
                key?.Dispose();
            }
        }

        return watchedKeys;
    }

    /// <summary>
    /// Arms a one-shot asynchronous change notification for the supplied key.
    /// </summary>
    private bool TryArmNotification(WatchedKey watchedKey)
    {
        WIN32_ERROR result = PInvoke.RegNotifyChangeKeyValue(
            watchedKey.Key.Handle,
            bWatchSubtree: true,
            WatchFilter,
            watchedKey.Signal.SafeWaitHandle,
            fAsynchronous: true);

        if (result == WIN32_ERROR.NO_ERROR)
        {
            return true;
        }

        _logger.LogWarning(
            "RegNotifyChangeKeyValue failed for HKLM\\{KeyPath} with {ErrorCode}.",
            watchedKey.Path,
            result);
        return false;
    }

    private void WatchLoop(WatchedKey[] watchedKeys, ManualResetEvent stopSignal)
    {
        var handles = new WaitHandle[watchedKeys.Length + 1];
        for (int index = 0; index < watchedKeys.Length; index++)
        {
            handles[index] = watchedKeys[index].Signal;
        }

        int stopIndex = watchedKeys.Length;
        handles[stopIndex] = stopSignal;

        while (true)
        {
            int signaledIndex;
            try
            {
                signaledIndex = WaitHandle.WaitAny(handles);
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (signaledIndex == stopIndex)
            {
                return;
            }

            WatchedKey watchedKey = watchedKeys[signaledIndex];
            watchedKey.Signal.Reset();

            // The registration is one-shot. Re-arm before reporting so writes that land while subscribers
            // reload are still observed. A key whose re-arm fails simply stops signalling; the remaining
            // keys keep working.
            if (!TryArmNotification(watchedKey))
            {
                _logger.LogWarning(
                    "Stopped monitoring HKLM\\{KeyPath}; its change notification could not be re-armed.",
                    watchedKey.Path);
            }

            ScheduleChangeNotification();
        }
    }

    private void ScheduleChangeNotification()
    {
        lock (_debounceLock)
        {
            if (!_isDebounceEnabled)
            {
                return;
            }

            _debounceTimer ??= new Timer(OnDebounceElapsed, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _debounceTimer.Change(ChangeDebounceInterval, Timeout.InfiniteTimeSpan);
        }
    }

    private void RemoveSubscription(EventHandler handler)
    {
        lock (_syncRoot)
        {
            if (_isDisposed)
            {
                return;
            }

            RulesChanged -= handler;
            if (_subscriberCount > 0)
            {
                _subscriberCount--;
            }

            if (_subscriberCount == 0)
            {
                StopCore();
            }
        }
    }

    private void OnDebounceElapsed(object? state)
    {
        if (_isDisposed)
        {
            return;
        }

        EventHandler? handlers = RulesChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (Delegate subscriber in handlers.GetInvocationList())
        {
            try
            {
                ((EventHandler)subscriber).Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "A Windows Firewall change subscriber failed.");
            }
        }
    }

    private void StopCore()
    {
        Thread? watchThread = _watchThread;
        ManualResetEvent? stopSignal = _stopSignal;
        List<WatchedKey> watchedKeys = _watchedKeys;

        IsWatching = false;
        _watchThread = null;
        _stopSignal = null;
        _watchedKeys = [];

        // Disable debouncing before signalling the watch thread so a change observed during teardown cannot
        // resurrect the timer after it has been disposed.
        lock (_debounceLock)
        {
            _isDebounceEnabled = false;
            _debounceTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }

        stopSignal?.Set();

        // The watch thread only ever takes _debounceLock, never _syncRoot, so joining it while the caller
        // holds _syncRoot cannot deadlock.
        if (watchThread is not null && !watchThread.Join(WatchThreadJoinTimeout))
        {
            _logger.LogDebug("The Windows Firewall policy watch thread did not exit within the join timeout.");
        }

        // Closing the key cancels its pending notification, so the keys must go before their events.
        foreach (WatchedKey watchedKey in watchedKeys)
        {
            watchedKey.Dispose();
        }

        stopSignal?.Dispose();
    }

    /// <summary>
    /// Pairs an open policy key with the event its change notification signals.
    /// </summary>
    private sealed partial class WatchedKey : IDisposable
    {
        public WatchedKey(string path, RegistryKey key)
        {
            Path = path;
            Key = key;
            Signal = new ManualResetEvent(false);
        }

        public string Path { get; }

        public RegistryKey Key { get; }

        public ManualResetEvent Signal { get; }

        public void Dispose()
        {
            Key.Dispose();
            Signal.Dispose();
        }
    }

    private sealed partial class FirewallChangeSubscription : IDisposable
    {
        private WindowsFirewallRuleChangeService? _owner;
        private EventHandler? _handler;

        public FirewallChangeSubscription(WindowsFirewallRuleChangeService owner, EventHandler handler)
        {
            _owner = owner;
            _handler = handler;
        }

        public void Dispose()
        {
            WindowsFirewallRuleChangeService? owner = Interlocked.Exchange(ref _owner, null);
            EventHandler? handler = Interlocked.Exchange(ref _handler, null);
            if (owner is not null && handler is not null)
            {
                owner.RemoveSubscription(handler);
            }
        }
    }
}
