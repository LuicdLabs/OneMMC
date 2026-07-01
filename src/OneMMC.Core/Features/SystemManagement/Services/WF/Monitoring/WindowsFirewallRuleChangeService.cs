using System;
using System.Collections.Generic;
using System.Management;
using System.Threading;
using OneMMC.Core.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace OneMMC.Core.Features.SystemManagement.Services.WF.Monitoring;

/// <summary>
/// Watches Windows Firewall configuration instances and raises debounced change notifications.
/// </summary>
public sealed class WindowsFirewallRuleChangeService : IDisposable
{
    private static readonly TimeSpan ChangeDebounceInterval = TimeSpan.FromMilliseconds(750);
    private static readonly string[] WatchedClassNames =
    [
        "MSFT_NetFirewallRule",
        "MSFT_NetConSecRule",
        "MSFT_NetFirewallProfile",
        "MSFT_NetSecuritySettingData",
        "MSFT_NetApplicationFilter",
        "MSFT_NetServiceFilter",
        "MSFT_NetAddressFilter",
        "MSFT_NetProtocolPortFilter",
        "MSFT_NetInterfaceFilter",
        "MSFT_NetInterfaceTypeFilter",
        "MSFT_NetTransportFilter",
        "MSFT_NetNetworkLayerSecurityFilter",
        "MSFT_NetIKEMMCryptoSet",
        "MSFT_NetIKEQMCryptoSet",
        "MSFT_NetIKEP1AuthSet",
        "MSFT_NetIKEP2AuthSet"
    ];

    private readonly ILogger<WindowsFirewallRuleChangeService> _logger;
    private readonly object _syncRoot = new();
    private readonly List<ManagementEventWatcher> _watchers = [];
    private Timer? _debounceTimer;
    private int _subscriberCount;
    private bool _isDisposed;

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
    /// Gets a value indicating whether at least one WMI watcher is currently active.
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
            ThreadPool.QueueUserWorkItem(_ => StartForSubscribers());

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

    private void StartForSubscribers()
    {
        lock (_syncRoot)
        {
            if (_subscriberCount == 0)
            {
                return;
            }

            StartCore();
        }
    }

    private void StartCore()
    {
        if (_isDisposed || IsWatching)
        {
            return;
        }

        var scope = new ManagementScope(@"\\.\root\StandardCimv2");
        foreach (string className in WatchedClassNames)
        {
            ManagementEventWatcher? watcher = null;
            try
            {
                var query = new WqlEventQuery(
                    $"SELECT * FROM __InstanceOperationEvent WITHIN 2 WHERE TargetInstance ISA \"{className}\"");
                watcher = new ManagementEventWatcher(scope, query);
                watcher.EventArrived += OnRuleEventArrived;
                watcher.Start();
                _watchers.Add(watcher);
                watcher = null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to start Windows Firewall change monitoring for {CimClassName}.", className);
                if (watcher is not null)
                {
                    watcher.EventArrived -= OnRuleEventArrived;
                    watcher.Dispose();
                }
            }
        }

        IsWatching = _watchers.Count > 0;
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

    private void OnRuleEventArrived(object sender, EventArrivedEventArgs e)
    {
        lock (_syncRoot)
        {
            if (_isDisposed)
            {
                return;
            }

            _debounceTimer ??= new Timer(OnDebounceElapsed, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _debounceTimer.Change(ChangeDebounceInterval, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnDebounceElapsed(object? state)
    {
        EventHandler? handlers;
        lock (_syncRoot)
        {
            if (_isDisposed)
            {
                return;
            }

            handlers = RulesChanged;
        }

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
        foreach (ManagementEventWatcher watcher in _watchers)
        {
            try
            {
                watcher.EventArrived -= OnRuleEventArrived;
                if (IsWatching)
                {
                    watcher.Stop();
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Ignoring error while stopping Windows Firewall change monitoring.");
            }
            finally
            {
                watcher.Dispose();
            }
        }

        _watchers.Clear();
        _debounceTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _debounceTimer?.Dispose();
        _debounceTimer = null;
        IsWatching = false;
    }

    private sealed class FirewallChangeSubscription : IDisposable
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

