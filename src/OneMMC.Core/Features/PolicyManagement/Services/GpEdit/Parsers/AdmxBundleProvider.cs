using System;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Windows.System;

namespace OneMMC.Core.Features.PolicyManagement.Services.GpEdit.Parsers;

/// <summary>
/// Supplies a single shared <see cref="AdmxBundle"/> for the whole process.
/// </summary>
/// <remarks>
/// Parsing <c>%SYSTEMROOT%\PolicyDefinitions</c> yields roughly 250-300 ADMX/ADML file pairs, thousands
/// of policies, and every localized string for them — tens of megabytes of long-lived dictionaries.
/// Both the Group Policy editor and the RSoP service used to build their own bundle, so visiting either
/// page allocated a fresh copy and visiting both held two at once.
/// <para>
/// The definitions are read-only operating-system data, so one shared instance is enough. Callers must
/// treat the returned bundle as immutable and must never dispose or clear it; call
/// <see cref="Invalidate"/> if the on-disk definitions are known to have changed.
/// </para>
/// </remarks>
public sealed partial class AdmxBundleProvider : IDisposable
{
    private static readonly TimeSpan StrongCacheIdleTimeout = TimeSpan.FromMinutes(10);

    private readonly ILogger<AdmxBundleProvider> _logger;
    private readonly object _gate = new();
    private readonly Timer _idleTimer;

    private AdmxBundle? _bundle;
    private string? _loadedCulture;
    private WeakReference<AdmxBundle>? _weakBundle;
    private string? _weakCulture;
    private bool _disposed;

    public AdmxBundleProvider(ILogger<AdmxBundleProvider> logger)
    {
        _logger = logger;
        _idleTimer = new Timer(OnIdleTimer, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        MemoryManager.AppMemoryUsageIncreased += OnAppMemoryUsageIncreased;
    }

    /// <summary>
    /// Returns the shared bundle, parsing the policy definitions on first use.
    /// </summary>
    /// <param name="cultureName">
    /// Culture whose ADML strings should be loaded. Defaults to the current culture when omitted. A
    /// request for a culture other than the one already loaded reloads the bundle.
    /// </param>
    /// <returns>The shared, fully loaded <see cref="AdmxBundle"/>.</returns>
    public AdmxBundle GetOrLoad(string? cultureName = null)
    {
        string culture = cultureName ?? CultureInfo.CurrentCulture.Name;

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_bundle is not null && string.Equals(_loadedCulture, culture, StringComparison.OrdinalIgnoreCase))
            {
                ResetIdleTimer();
                return _bundle;
            }

            if (_weakBundle is not null &&
                string.Equals(_weakCulture, culture, StringComparison.OrdinalIgnoreCase) &&
                _weakBundle.TryGetTarget(out AdmxBundle? cachedBundle))
            {
                _bundle = cachedBundle;
                _loadedCulture = culture;
                ResetIdleTimer();
                _logger.LogDebug("Promoted the weak ADMX bundle cache for culture {Culture}.", culture);
                return cachedBundle;
            }

            string policyDefinitionsPath = Environment.ExpandEnvironmentVariables(@"%SYSTEMROOT%\PolicyDefinitions");
            var bundle = new AdmxBundle();
            bundle.LoadFolder(policyDefinitionsPath, culture);

            _bundle = bundle;
            _loadedCulture = culture;
            _weakBundle = new WeakReference<AdmxBundle>(bundle);
            _weakCulture = culture;
            ResetIdleTimer();

            _logger.LogInformation(
                "Loaded shared ADMX bundle for culture {Culture}: {PolicyCount} policies, {CategoryCount} categories.",
                culture,
                bundle.Policies.Count,
                bundle.FlatCategories.Count);

            return bundle;
        }
    }

    /// <summary>
    /// Drops the cached bundle so the next <see cref="GetOrLoad"/> re-reads the policy definitions.
    /// </summary>
    public void Invalidate()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            _bundle = null;
            _loadedCulture = null;
            _weakBundle = null;
            _weakCulture = null;
            _idleTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>
    /// Releases the process-lifetime timer and memory-pressure subscription.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _bundle = null;
            _loadedCulture = null;
            _weakBundle = null;
            _weakCulture = null;
        }

        MemoryManager.AppMemoryUsageIncreased -= OnAppMemoryUsageIncreased;
        _idleTimer.Dispose();
    }

    private void OnIdleTimer(object? state)
    {
        string? culture = DropStrongReference();
        if (culture is not null)
        {
            _logger.LogInformation(
                "Released the strong ADMX bundle cache for culture {Culture} after {IdleMinutes} idle minutes.",
                culture,
                StrongCacheIdleTimeout.TotalMinutes);
        }
    }

    private void OnAppMemoryUsageIncreased(object? sender, object e)
    {
        AppMemoryUsageLevel level = MemoryManager.AppMemoryUsageLevel;
        if (level is not (AppMemoryUsageLevel.High or AppMemoryUsageLevel.OverLimit))
        {
            return;
        }

        string? culture = DropStrongReference();
        if (culture is null)
        {
            return;
        }

        _logger.LogWarning(
            "Released the strong ADMX bundle cache for culture {Culture} because memory usage is {MemoryLevel}.",
            culture,
            level);

        // This is the non-blocking pressure-only hint recommended by the Windows App SDK guidance.
        // Never run it on window hide/minimize or on the idle timer.
        GC.Collect(2, GCCollectionMode.Optimized, blocking: false);
    }

    private string? DropStrongReference()
    {
        lock (_gate)
        {
            if (_disposed || _bundle is null)
            {
                return null;
            }

            string? culture = _loadedCulture;
            _weakBundle = new WeakReference<AdmxBundle>(_bundle);
            _weakCulture = culture;
            _bundle = null;
            _loadedCulture = null;
            return culture;
        }
    }

    private void ResetIdleTimer() =>
        _idleTimer.Change(StrongCacheIdleTimeout, Timeout.InfiniteTimeSpan);
}
