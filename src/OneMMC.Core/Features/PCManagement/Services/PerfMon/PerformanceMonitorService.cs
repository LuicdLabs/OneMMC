// ============================================================================
// Performance Monitor Service
// ============================================================================
// File Description:
//   This service encapsulates all operations related to Windows performance counters, including:
//   - Category & Counter Discovery
//   - Counter Management
//   - Common Counters
//   - Configuration Save/Load
//
// Architecture Position: Service Layer (Service layer in MVVM architecture)
// Thread Safety: Uses SemaphoreSlim and ConcurrentDictionary to ensure thread safety
// Dependencies: PDH (Performance Data Helper) via CsWin32 - the Native-AOT-compatible
//               replacement for System.Diagnostics.PerformanceCounter (M4 migration)
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OneMMC.Core.Features.PCManagement.Models.PerfMon;
using Microsoft.Extensions.Logging;
using Windows.Win32.Foundation;
using Windows.Win32.System.Performance;
using Win32PInvoke = Windows.Win32.PInvoke;

namespace OneMMC.Core.Features.PCManagement.Services.PerfMon
{
    /// <summary>
    /// Performance Monitor Service class.
    /// Encapsulates all Windows performance counter related operations, providing thread-safe counter access.
    /// </summary>
    /// <remarks>
    /// Design principles:
    /// 1. Thread safety - Use SemaphoreSlim to protect counter access
    /// 2. Fault tolerance - Automatically rebuild counters when they fail
    /// 3. Resource management - Implement IDisposable to ensure proper resource cleanup
    /// 4. Asynchronous operations - Use async/await for time-consuming operations to avoid blocking UI
    ///
    /// PDH topology: one PDH_HQUERY per tracked counter (per-counter query). This mirrors the
    /// previous per-counter PerformanceCounter.NextValue() semantics exactly - each read samples
    /// only its own counter and a failing counter rebuilds alone - at the cost of one
    /// PdhCollectQueryData per counter per tick, negligible at the 1 Hz UI polling rate.
    /// (A single shared query collected once per tick would be the optimization if the tick
    /// boundary ever becomes visible to this service.)
    /// </remarks>
    public partial class PerformanceMonitorService : IDisposable
    {
        private readonly ILogger<PerformanceMonitorService> _logger;

        public PerformanceMonitorService(ILogger<PerformanceMonitorService> logger)
        {
            _logger = logger;
        }

        // ====================================================================
        // PDH status codes (pdhmsg.h) with dedicated handling
        // ====================================================================
        private const uint PdhOk = 0;
        private const uint PdhMoreData = 0x800007D2;          // PDH_MORE_DATA - buffer sizing round-trip
        private const uint PdhCStatusValidData = 0;           // PDH_CSTATUS_VALID_DATA
        private const uint PdhCStatusNewData = 1;             // PDH_CSTATUS_NEW_DATA
        private const uint PdhCStatusNoObject = 0xC0000BB8;   // PDH_CSTATUS_NO_OBJECT - localized name miss
        private const uint PdhCStatusNoCounter = 0xC0000BB9;  // PDH_CSTATUS_NO_COUNTER - localized name miss

        // ====================================================================
        // Private Fields
        // ====================================================================

        /// <summary>
        /// An open PDH query owning exactly one counter (see the class remarks for why the
        /// topology is per-counter).
        /// </summary>
        private sealed class PdhCounter
        {
            public PDH_HQUERY Query;
            public PDH_HCOUNTER Counter;
        }

        /// <summary>
        /// Active performance counter cache.
        /// Key: Counter unique identifier (MachineName\CategoryName\CounterName\InstanceName)
        /// Value: PDH query + counter handle pair
        /// </summary>
        private readonly ConcurrentDictionary<string, PdhCounter> _activeCounters = new();

        /// <summary>
        /// Cache of counter last read values.
        /// When counter reading fails, return this cached value as fallback.
        /// </summary>
        private readonly ConcurrentDictionary<string, float> _lastValues = new();

        /// <summary>
        /// Semaphore for protecting counter operations.
        /// Ensures only one thread can access counters at a time.
        /// </summary>
        private readonly SemaphoreSlim _semaphore = new(1, 1);

        /// <summary>
        /// Flag indicating whether the service has been disposed.
        /// </summary>
        private bool _disposed;

        // ====================================================================
        // Category & Counter Discovery
        // ====================================================================
        #region Category & Counter Discovery

        /// <summary>
        /// Asynchronously get all performance counter categories.
        /// </summary>
        /// <param name="machineName">Target machine name, "." represents local machine</param>
        /// <returns>List of category information, sorted by name</returns>
        /// <remarks>
        /// Counter objects are enumerated with <c>PdhEnumObjects</c>. PDH exposes no per-object
        /// help text through enumeration, so <see cref="PerformanceCounterCategoryInfo.Description"/>
        /// is empty (a documented behavior change from the System.Diagnostics backend), and
        /// <see cref="PerformanceCounterCategoryInfo.IsMultiInstance"/> is not pre-probed - instance
        /// presence is determined per selection via <see cref="GetInstancesAsync"/>.
        /// </remarks>
        public async Task<List<PerformanceCounterCategoryInfo>> GetCategoriesAsync(string machineName = ".")
        {
            return await Task.Run(() =>
            {
                var categories = new List<PerformanceCounterCategoryInfo>();
                try
                {
                    foreach (var name in EnumObjectNames(machineName).OrderBy(n => n))
                    {
                        categories.Add(new PerformanceCounterCategoryInfo
                        {
                            Name = name,
                            Description = string.Empty,
                            MachineName = machineName
                        });
                    }
                }
                catch (Exception ex) { _logger.LogError(ex, "Failed to get performance categories for machine {MachineName}.", machineName); }
                return categories;
            });
        }

        /// <summary>
        /// Asynchronously get all counters in the specified category.
        /// </summary>
        /// <param name="categoryName">Category name</param>
        /// <param name="machineName">Target machine name</param>
        /// <returns>List of counter information, sorted by name</returns>
        public async Task<List<CounterInfo>> GetCountersAsync(string categoryName, string machineName = ".")
        {
            return await Task.Run(() =>
            {
                var counters = new List<CounterInfo>();
                try
                {
                    var (counterNames, _) = EnumObjectItems(categoryName, machineName);
                    counters.AddRange(counterNames.Select(n => new CounterInfo { Name = n, Description = string.Empty }));
                }
                catch (Exception ex) { _logger.LogError(ex, "Failed to get counters for category {CategoryName} on machine {MachineName}.", categoryName, machineName); }
                return counters.OrderBy(c => c.Name).ToList();
            });
        }

        /// <summary>
        /// Asynchronously get all instance names for multi-instance categories.
        /// </summary>
        /// <param name="categoryName">Category name</param>
        /// <param name="machineName">Target machine name</param>
        /// <returns>List of instance names, sorted by name; empty for single-instance categories</returns>
        public async Task<List<string>> GetInstancesAsync(string categoryName, string machineName = ".")
        {
            return await Task.Run(() =>
            {
                try
                {
                    var (_, instances) = EnumObjectItems(categoryName, machineName);
                    return instances.OrderBy(i => i).ToList();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to get instances for category {CategoryName} on machine {MachineName}.", categoryName, machineName);
                    return new List<string>();
                }
            });
        }

        /// <summary>
        /// Check if the specified category requires an instance name.
        /// </summary>
        /// <param name="categoryName">Category name</param>
        /// <param name="machineName">Target machine name</param>
        /// <returns>true if instance name is required</returns>
        public bool CategoryRequiresInstance(string categoryName, string machineName = ".")
        {
            try
            {
                return EnumObjectItems(categoryName, machineName).Instances.Count > 0;
            }
            catch { return false; }
        }

        /// <summary>
        /// Get the first instance name of a category.
        /// Prioritizes returning "_Total" (if it exists), otherwise returns the first instance.
        /// </summary>
        /// <param name="categoryName">Category name</param>
        /// <param name="machineName">Target machine name</param>
        /// <returns>Instance name, or null if no instances exist</returns>
        public string? GetFirstInstance(string categoryName, string machineName = ".")
        {
            try
            {
                var (_, instances) = EnumObjectItems(categoryName, machineName);
                // Prioritize _Total instance as it represents the sum of all instances
                return instances.Contains("_Total") ? "_Total" : instances.FirstOrDefault();
            }
            catch { return null; }
        }

        #endregion

        // ====================================================================
        // Counter Management
        // ====================================================================
        #region Counter Management

        /// <summary>
        /// Create (and cache) the PDH counter behind <paramref name="counterInfo"/>.
        /// </summary>
        /// <param name="counterInfo">Counter information</param>
        /// <returns>true if the counter exists or was created successfully</returns>
        /// <remarks>
        /// This method is thread-safe, protected by a semaphore.
        /// If the counter already exists in cache, it is reused.
        /// </remarks>
        public bool CreateCounter(PerformanceCounterInfo counterInfo)
        {
            _semaphore.Wait();
            try { return CreateCounterCore(counterInfo) is not null; }
            finally { _semaphore.Release(); }
        }

        /// <summary>
        /// Read the current value of a counter.
        /// </summary>
        /// <param name="counterInfo">Counter information</param>
        /// <returns>Counter value, returns the last successfully read value if reading fails</returns>
        /// <remarks>
        /// Fault tolerance mechanism:
        /// 1. If counter doesn't exist, attempt to create it
        /// 2. If reading fails (stale handle, vanished instance), attempt to rebuild the counter
        /// 3. If still fails, return the cached last value
        /// </remarks>
        public float ReadCounterValue(PerformanceCounterInfo counterInfo)
        {
            var key = GetCounterKey(counterInfo);
            _semaphore.Wait();
            try
            {
                // Try to get counter from cache
                if (!_activeCounters.TryGetValue(key, out var counter))
                {
                    counter = CreateCounterCore(counterInfo);
                    if (counter is null) return _lastValues.GetValueOrDefault(key, 0);
                }

                if (TryReadValue(counter, out float value))
                {
                    _lastValues[key] = value;
                    return value;
                }

                // Counter may have become invalid (e.g., instance disappeared), attempt to rebuild
                RemoveCounterCore(key);
                var newCounter = CreateCounterCore(counterInfo);
                if (newCounter is not null && TryReadValue(newCounter, out value))
                {
                    _lastValues[key] = value;
                    return value;
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "Failed reading counter {CounterDisplayName}.", counterInfo.DisplayName); }
            finally { _semaphore.Release(); }

            // Return the last successfully read value as fallback
            return _lastValues.GetValueOrDefault(key, 0);
        }

        /// <summary>
        /// Collect the counter's query and format the current value as a double.
        /// </summary>
        private unsafe bool TryReadValue(PdhCounter counter, out float value)
        {
            value = 0;
            if (Win32PInvoke.PdhCollectQueryData(counter.Query) != PdhOk)
            {
                return false;
            }

            uint status = Win32PInvoke.PdhGetFormattedCounterValue(counter.Counter, PDH_FMT.PDH_FMT_DOUBLE, out _, out PDH_FMT_COUNTERVALUE formatted);
            if (status != PdhOk || (formatted.CStatus != PdhCStatusValidData && formatted.CStatus != PdhCStatusNewData))
            {
                return false;
            }

            value = (float)formatted.Anonymous.doubleValue;
            return true;
        }

        /// <summary>
        /// Core implementation for creating counters (internal method).
        /// </summary>
        /// <param name="counterInfo">Counter information</param>
        /// <returns>Created counter handle pair, or null if creation fails</returns>
        /// <remarks>
        /// Processing flow:
        /// 1. Check if already exists in cache
        /// 2. If multi-instance category but no instance specified, automatically fill in the first instance
        /// 3. Open a query, add the counter (localized name first, then English fallback), prime it
        /// 4. Add counter to cache
        /// </remarks>
        private unsafe PdhCounter? CreateCounterCore(PerformanceCounterInfo counterInfo)
        {
            try
            {
                var key = GetCounterKey(counterInfo);

                // If already exists in cache, return directly
                if (_activeCounters.TryGetValue(key, out var existing)) return existing;

                // Automatically fill in instance name (if required but not specified)
                if (string.IsNullOrEmpty(counterInfo.InstanceName) && CategoryRequiresInstance(counterInfo.CategoryName, counterInfo.MachineName))
                {
                    var instance = GetFirstInstance(counterInfo.CategoryName, counterInfo.MachineName);
                    if (string.IsNullOrEmpty(instance)) return null;
                    counterInfo.InstanceName = instance;
                }

                string? path = MakeCounterPath(counterInfo);
                if (path is null) return null;

                PDH_HQUERY query = default;
                uint status = Win32PInvoke.PdhOpenQuery(default(PCWSTR), 0, &query);
                if (status != PdhOk)
                {
                    _logger.LogError("PdhOpenQuery failed with 0x{Status:X8} for {Path}.", status, path);
                    return null;
                }

                PDH_HCOUNTER counterHandle;
                fixed (char* pPath = path)
                {
                    // Try the name as-is first (localized names picked from enumeration resolve here).
                    // Hardcoded English names (GetCommonCounters, saved configs from other locales)
                    // fall back to the locale-independent English lookup - which the old
                    // System.Diagnostics backend could not do at all on non-English systems.
                    status = Win32PInvoke.PdhAddCounter(query, pPath, 0, &counterHandle);
                    if (status is PdhCStatusNoObject or PdhCStatusNoCounter)
                    {
                        status = Win32PInvoke.PdhAddEnglishCounter(query, pPath, 0, &counterHandle);
                    }
                }

                if (status != PdhOk)
                {
                    _logger.LogError("PdhAddCounter failed with 0x{Status:X8} for {Path}.", status, path);
                    _ = Win32PInvoke.PdhCloseQuery(query);
                    return null;
                }

                // Perform initial collection (rate counters need two samples for a valid value)
                _ = Win32PInvoke.PdhCollectQueryData(query);

                var counter = new PdhCounter { Query = query, Counter = counterHandle };
                _activeCounters[key] = counter;
                _lastValues[key] = 0;
                return counter;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed creating counter {CategoryName}\\{CounterName}.", counterInfo.CategoryName, counterInfo.CounterName);
                return null;
            }
        }

        /// <summary>
        /// Remove the specified counter and release resources.
        /// </summary>
        /// <param name="counterInfo">Counter information to remove</param>
        public void RemoveCounter(PerformanceCounterInfo counterInfo)
        {
            var key = GetCounterKey(counterInfo);
            _semaphore.Wait();
            try
            {
                RemoveCounterCore(key);
                _lastValues.TryRemove(key, out _);
            }
            finally { _semaphore.Release(); }
        }

        /// <summary>
        /// Dispose all active counters.
        /// </summary>
        public void DisposeAllCounters()
        {
            _semaphore.Wait();
            try
            {
                foreach (var key in _activeCounters.Keys.ToList())
                {
                    RemoveCounterCore(key);
                }
                _lastValues.Clear();
            }
            finally { _semaphore.Release(); }
        }

        /// <summary>
        /// Remove a cache entry and close its PDH query (which releases its counter too).
        /// Caller must hold the semaphore.
        /// </summary>
        private void RemoveCounterCore(string key)
        {
            if (_activeCounters.TryRemove(key, out var counter))
            {
                _ = Win32PInvoke.PdhCloseQuery(counter.Query);
            }
        }

        #endregion

        // ====================================================================
        // Common Counters
        // ====================================================================
        #region Common Counters

        /// <summary>
        /// Get a list of predefined common performance counters.
        /// </summary>
        /// <returns>List of common counters with colors and scaling configured</returns>
        /// <remarks>
        /// Included counters:
        /// - Processor\% Processor Time (_Total) - CPU usage
        /// - Memory\% Committed Bytes In Use - Memory usage
        /// - Memory\Available MBytes - Available memory (scale 0.01)
        /// - PhysicalDisk\% Disk Time (_Total) - Disk usage
        /// - System\Processor Queue Length - Processor queue length (scale 10.0)
        /// - Paging File\% Usage (_Total) - Paging file usage
        /// - Process\% Processor Time (_Total) - All processes CPU usage
        /// - Memory\Pages/sec - Pages per second
        /// These are English names; they resolve on any OS locale through the
        /// PdhAddEnglishCounter fallback in counter creation.
        /// </remarks>
        public List<PerformanceCounterInfo> GetCommonCounters()
        {
            var colorIndex = 0;
            return new List<PerformanceCounterInfo>
            {
                new() { CategoryName = "Processor", CounterName = "% Processor Time", InstanceName = "_Total", ColorHex = PerfMonColors.GetColor(colorIndex++), Scale = 1.0 },
                new() { CategoryName = "Memory", CounterName = "% Committed Bytes In Use", InstanceName = "", ColorHex = PerfMonColors.GetColor(colorIndex++), Scale = 1.0 },
                new() { CategoryName = "Memory", CounterName = "Available MBytes", InstanceName = "", ColorHex = PerfMonColors.GetColor(colorIndex++), Scale = 0.01 },
                new() { CategoryName = "PhysicalDisk", CounterName = "% Disk Time", InstanceName = "_Total", ColorHex = PerfMonColors.GetColor(colorIndex++), Scale = 1.0 },
                new() { CategoryName = "System", CounterName = "Processor Queue Length", InstanceName = "", ColorHex = PerfMonColors.GetColor(colorIndex++), Scale = 10.0 },
                new() { CategoryName = "Paging File", CounterName = "% Usage", InstanceName = "_Total", ColorHex = PerfMonColors.GetColor(colorIndex++), Scale = 1.0 },
                new() { CategoryName = "Process", CounterName = "% Processor Time", InstanceName = "_Total", ColorHex = PerfMonColors.GetColor(colorIndex++), Scale = 1.0 },
                new() { CategoryName = "Memory", CounterName = "Pages/sec", InstanceName = "", ColorHex = PerfMonColors.GetColor(colorIndex), Scale = 1.0 }
            };
        }

        /// <summary>
        /// Asynchronously search for counters matching the criteria.
        /// </summary>
        /// <param name="searchTerm">Search keyword</param>
        /// <param name="machineName">Target machine name</param>
        /// <returns>List of matching counters (maximum 50 results)</returns>
        /// <remarks>
        /// Search logic:
        /// 1. Search if category name contains the keyword
        /// 2. Search if counter name contains the keyword
        /// 3. Multi-instance categories will create separate results for each instance (maximum 5 instances)
        /// 4. Result limit is 50 to avoid performance impact from too many results
        /// </remarks>
        public async Task<List<PerformanceCounterInfo>> SearchCountersAsync(string searchTerm, string machineName = ".")
        {
            if (string.IsNullOrWhiteSpace(searchTerm)) return new List<PerformanceCounterInfo>();

            return await Task.Run(() =>
            {
                var results = new List<PerformanceCounterInfo>();
                var colorIndex = 0;
                var searchLower = searchTerm.ToLowerInvariant();

                try
                {
                    foreach (var categoryName in EnumObjectNames(machineName))
                    {
                        // Stop searching when limit is reached
                        if (results.Count >= 50) break;
                        try
                        {
                            bool categoryMatches = categoryName.ToLowerInvariant().Contains(searchLower);
                            var (counterNames, instances) = EnumObjectItems(categoryName, machineName);
                            bool isMulti = instances.Count > 0;

                            foreach (var counterName in counterNames)
                            {
                                // Category or counter name matches search criteria
                                if (categoryMatches || counterName.ToLowerInvariant().Contains(searchLower))
                                {
                                    if (isMulti)
                                    {
                                        // Multi-instance category: create results for each instance (maximum 5)
                                        foreach (var instance in instances.Take(5))
                                        {
                                            results.Add(new PerformanceCounterInfo
                                            {
                                                CategoryName = categoryName,
                                                CounterName = counterName,
                                                InstanceName = instance,
                                                MachineName = machineName,
                                                ColorHex = PerfMonColors.GetColor(colorIndex++)
                                            });
                                            if (results.Count >= 50) break;
                                        }
                                    }
                                    else
                                    {
                                        // Single-instance category
                                        results.Add(new PerformanceCounterInfo
                                        {
                                            CategoryName = categoryName,
                                            CounterName = counterName,
                                            InstanceName = "",
                                            MachineName = machineName,
                                            ColorHex = PerfMonColors.GetColor(colorIndex++)
                                        });
                                    }
                                }
                                if (results.Count >= 50) break;
                            }
                        }
                        catch (Exception ex) { _logger.LogWarning(ex, "Failed searching counters in category {CategoryName}.", categoryName); }
                    }
                }
                catch (Exception ex) { _logger.LogError(ex, "Failed searching performance counters."); }
                return results;
            });
        }

        #endregion

        // ====================================================================
        // Configuration Save/Load
        // ====================================================================
        #region Configuration Save/Load

        /// <summary>
        /// Asynchronously save counter configuration to file.
        /// </summary>
        /// <param name="filePath">File path (.pmcfg format)</param>
        /// <param name="counters">List of counters to save</param>
        /// <returns>true if save is successful</returns>
        /// <remarks>
        /// Configuration file format is JSON, containing:
        /// - Version: Configuration version
        /// - SavedDate: Save date
        /// - Counters: Counter array (including all settings)
        /// </remarks>
        public async Task<bool> SaveConfigurationAsync(string filePath, List<PerformanceCounterInfo> counters)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Create configuration object
                    var config = new PerfMonConfigDto
                    {
                        Version = "1.0",
                        SavedDate = DateTime.Now,
                        Counters = counters.Select(c => new PerfMonCounterDto
                        {
                            CategoryName = c.CategoryName, CounterName = c.CounterName, InstanceName = c.InstanceName, MachineName = c.MachineName,
                            Scale = c.Scale, ColorHex = c.ColorHex, IsVisible = c.IsVisible, Width = c.Width, LineStyle = c.LineStyle
                        }).ToList()
                    };

                    // Serialize to JSON and write to file
                    System.IO.File.WriteAllText(filePath, System.Text.Json.JsonSerializer.Serialize(config, PerfMonJsonContext.Default.PerfMonConfigDto));
                    return true;
                }
                catch (Exception ex) { _logger.LogError(ex, "Failed saving performance monitor configuration to {Path}.", filePath); return false; }
            });
        }

        /// <summary>
        /// Asynchronously load counter configuration from file.
        /// </summary>
        /// <param name="filePath">File path (.pmcfg format)</param>
        /// <returns>List of loaded counters, returns empty list if failed</returns>
        public async Task<List<PerformanceCounterInfo>> LoadConfigurationAsync(string filePath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (!System.IO.File.Exists(filePath)) return new List<PerformanceCounterInfo>();

                    // Read and parse JSON (JsonDocument.Parse is reflection-free and AOT safe)
                    using var config = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(filePath));

                    // Convert JSON to counter objects
                    return config.RootElement.GetProperty("Counters").EnumerateArray().Select(item => new PerformanceCounterInfo
                    {
                        CategoryName = item.GetProperty("CategoryName").GetString() ?? "",
                        CounterName = item.GetProperty("CounterName").GetString() ?? "",
                        InstanceName = item.GetProperty("InstanceName").GetString() ?? "",
                        MachineName = item.GetProperty("MachineName").GetString() ?? ".",
                        // Use TryGetProperty to handle optional properties, providing default values
                        Scale = item.TryGetProperty("Scale", out var s) ? s.GetDouble() : 1.0,
                        ColorHex = item.TryGetProperty("ColorHex", out var c) ? c.GetString() ?? "#F7B500" : "#F7B500",
                        IsVisible = !item.TryGetProperty("IsVisible", out var v) || v.GetBoolean(),
                        Width = item.TryGetProperty("Width", out var w) ? w.GetInt32() : 1,
                        LineStyle = item.TryGetProperty("LineStyle", out var ls) ? ls.GetInt32() : 0
                    }).ToList();
                }
                catch (Exception ex) { _logger.LogError(ex, "Failed loading performance monitor configuration from {Path}.", filePath); return new List<PerformanceCounterInfo>(); }
            });
        }

        #endregion

        // ====================================================================
        // PDH Helpers
        // ====================================================================
        #region PDH Helpers

        /// <summary>
        /// Translate the model's machine name ("." = local) into the PDH form
        /// (null = local, "\\name" = remote).
        /// </summary>
        private static string? ToPdhMachineName(string machineName) =>
            string.IsNullOrEmpty(machineName) || machineName == "."
                ? null
                : machineName.StartsWith(@"\\", StringComparison.Ordinal) ? machineName : @"\\" + machineName;

        /// <summary>
        /// Enumerate performance object (category) names via PdhEnumObjects.
        /// </summary>
        private unsafe List<string> EnumObjectNames(string machineName)
        {
            string? machine = ToPdhMachineName(machineName);

            // Sizing call (also refreshes the object cache), then fetch. The required size can
            // grow between the two calls, so retry a few times on PDH_MORE_DATA.
            uint cch = 0;
            uint status = Win32PInvoke.PdhEnumObjects(null, machine, ref cch, PERF_DETAIL.PERF_DETAIL_WIZARD, true);
            for (int attempt = 0; status == PdhMoreData && attempt < 3; attempt++)
            {
                var buffer = new char[(int)cch];
                fixed (char* pBuffer = buffer)
                {
                    status = Win32PInvoke.PdhEnumObjects(null, machine, new PZZWSTR(pBuffer), ref cch, PERF_DETAIL.PERF_DETAIL_WIZARD, false);
                    if (status == PdhOk)
                    {
                        return ParseMultiSz(buffer, (int)cch);
                    }
                }
            }

            if (status != PdhOk)
            {
                throw new InvalidOperationException($"PdhEnumObjects failed with 0x{status:X8}.");
            }

            return new List<string>();
        }

        /// <summary>
        /// Enumerate counter and instance names of one performance object via PdhEnumObjectItems.
        /// An empty instance list means the object is single-instance.
        /// </summary>
        private unsafe (List<string> Counters, List<string> Instances) EnumObjectItems(string categoryName, string machineName)
        {
            string? machine = ToPdhMachineName(machineName);

            uint cchCounters = 0;
            uint cchInstances = 0;
            uint status = Win32PInvoke.PdhEnumObjectItems(null, machine, categoryName, ref cchCounters, ref cchInstances, PERF_DETAIL.PERF_DETAIL_WIZARD, 0);
            for (int attempt = 0; status == PdhMoreData && attempt < 3; attempt++)
            {
                var counterBuffer = new char[(int)Math.Max(cchCounters, 1)];
                var instanceBuffer = new char[(int)Math.Max(cchInstances, 1)];
                fixed (char* pCounters = counterBuffer)
                fixed (char* pInstances = instanceBuffer)
                {
                    status = Win32PInvoke.PdhEnumObjectItems(
                        null,
                        machine,
                        categoryName,
                        new PZZWSTR(pCounters),
                        ref cchCounters,
                        new PZZWSTR(pInstances),
                        ref cchInstances,
                        PERF_DETAIL.PERF_DETAIL_WIZARD,
                        0);
                    if (status == PdhOk)
                    {
                        return (ParseMultiSz(counterBuffer, (int)cchCounters),
                                DecorateDuplicateInstances(ParseMultiSz(instanceBuffer, (int)cchInstances)));
                    }
                }
            }

            if (status != PdhOk)
            {
                throw new InvalidOperationException($"PdhEnumObjectItems failed for '{categoryName}' with 0x{status:X8}.");
            }

            return (new List<string>(), new List<string>());
        }

        /// <summary>
        /// Split a REG_MULTI_SZ style buffer (null-separated strings, double-null terminated).
        /// </summary>
        private static List<string> ParseMultiSz(char[] buffer, int length)
        {
            var result = new List<string>();
            int start = 0;
            for (int i = 0; i < length && i < buffer.Length; i++)
            {
                if (buffer[i] == '\0')
                {
                    if (i > start)
                    {
                        result.Add(new string(buffer, start, i - start));
                    }
                    start = i + 1;
                }
            }
            return result;
        }

        /// <summary>
        /// PDH enumerates duplicate instances (e.g. several processes with the same name) as
        /// repeated raw names; decorate repeats as "name#1", "name#2"… - the same convention the
        /// System.Diagnostics backend surfaced and the PDH counter-path syntax accepts.
        /// </summary>
        private static List<string> DecorateDuplicateInstances(List<string> instances)
        {
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>(instances.Count);
            foreach (var instance in instances)
            {
                if (seen.TryGetValue(instance, out int count))
                {
                    seen[instance] = count + 1;
                    result.Add($"{instance}#{count}");
                }
                else
                {
                    seen[instance] = 1;
                    result.Add(instance);
                }
            }
            return result;
        }

        /// <summary>
        /// Build the full PDH counter path (\\Machine\Object(Instance)\Counter) with proper
        /// escaping via PdhMakeCounterPath.
        /// </summary>
        private unsafe string? MakeCounterPath(PerformanceCounterInfo counterInfo)
        {
            string? machine = ToPdhMachineName(counterInfo.MachineName);
            string? instance = string.IsNullOrEmpty(counterInfo.InstanceName) ? null : counterInfo.InstanceName;

            fixed (char* pMachine = machine)
            fixed (char* pObject = counterInfo.CategoryName)
            fixed (char* pInstance = instance)
            fixed (char* pCounter = counterInfo.CounterName)
            {
                var elements = new PDH_COUNTER_PATH_ELEMENTS_W
                {
                    szMachineName = pMachine,
                    szObjectName = pObject,
                    szInstanceName = pInstance,
                    szParentInstance = null,
                    dwInstanceIndex = 0,
                    szCounterName = pCounter
                };

                uint cch = 0;
                uint status = Win32PInvoke.PdhMakeCounterPath(in elements, ref cch, PDH_PATH_FLAGS.PDH_PATH_WBEM_NONE);
                if (status != PdhMoreData)
                {
                    _logger.LogError("PdhMakeCounterPath sizing failed with 0x{Status:X8} for {Category}\\{Counter}.", status, counterInfo.CategoryName, counterInfo.CounterName);
                    return null;
                }

                Span<char> buffer = cch <= 256 ? stackalloc char[(int)cch] : new char[(int)cch];
                status = Win32PInvoke.PdhMakeCounterPath(in elements, buffer, ref cch, PDH_PATH_FLAGS.PDH_PATH_WBEM_NONE);
                if (status != PdhOk)
                {
                    _logger.LogError("PdhMakeCounterPath failed with 0x{Status:X8} for {Category}\\{Counter}.", status, counterInfo.CategoryName, counterInfo.CounterName);
                    return null;
                }

                int terminator = buffer.IndexOf('\0');
                return new string(terminator >= 0 ? buffer[..terminator] : buffer);
            }
        }

        #endregion

        // ====================================================================
        // Helper Methods
        // ====================================================================

        /// <summary>
        /// Generate unique identifier for a counter.
        /// Format: MachineName\CategoryName\CounterName\InstanceName
        /// </summary>
        /// <param name="c">Counter information</param>
        /// <returns>Unique identifier string</returns>
        private static string GetCounterKey(PerformanceCounterInfo c) => $"{c.MachineName}\\{c.CategoryName}\\{c.CounterName}\\{c.InstanceName}";

        // ====================================================================
        // IDisposable Implementation
        // ====================================================================

        /// <summary>
        /// Release all resources used by the service.
        /// </summary>
        /// <remarks>
        /// Disposal process:
        /// 1. Check if already disposed
        /// 2. Dispose all active counters (closes every PDH query)
        /// 3. Dispose semaphore
        /// 4. Mark as disposed
        /// </remarks>
        public void Dispose()
        {
            if (_disposed) return;
            DisposeAllCounters();
            _semaphore?.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Serialization shape of a saved performance monitor configuration file (.pmcfg).
    /// </summary>
    internal sealed class PerfMonConfigDto
    {
        public string Version { get; set; } = "1.0";
        public DateTime SavedDate { get; set; }
        public List<PerfMonCounterDto> Counters { get; set; } = [];
    }

    /// <summary>
    /// Serialization shape of a single counter entry in a .pmcfg file.
    /// </summary>
    internal sealed class PerfMonCounterDto
    {
        public string CategoryName { get; set; } = string.Empty;
        public string CounterName { get; set; } = string.Empty;
        public string InstanceName { get; set; } = string.Empty;
        public string MachineName { get; set; } = ".";
        public double Scale { get; set; } = 1.0;
        public string ColorHex { get; set; } = "#F7B500";
        public bool IsVisible { get; set; } = true;
        public int Width { get; set; } = 1;
        public int LineStyle { get; set; }
    }

    /// <summary>
    /// Source-generated JSON context so .pmcfg serialization works without reflection (Native AOT compatible).
    /// </summary>
    [System.Text.Json.Serialization.JsonSourceGenerationOptions(WriteIndented = true)]
    [System.Text.Json.Serialization.JsonSerializable(typeof(PerfMonConfigDto))]
    internal sealed partial class PerfMonJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
}
