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
// Dependencies: System.Diagnostics.PerformanceCounter
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OneMMC.Core.Features.PCManagement.Models.PerfMon;
using Microsoft.Extensions.Logging;

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
    /// </remarks>
    public class PerformanceMonitorService : IDisposable
    {
        private readonly ILogger<PerformanceMonitorService> _logger;

        public PerformanceMonitorService(ILogger<PerformanceMonitorService> logger)
        {
            _logger = logger;
        }

        // ====================================================================
        // Private Fields
        // ====================================================================

        /// <summary>
        /// Active performance counter cache.
        /// Key: Counter unique identifier (MachineName\CategoryName\CounterName\InstanceName)
        /// Value: PerformanceCounter instance
        /// </summary>
        private readonly ConcurrentDictionary<string, PerformanceCounter> _activeCounters = new();
        
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
        /// This method iterates through all performance counter categories in the system.
        /// Some categories may be inaccessible due to insufficient permissions and will be skipped.
        /// </remarks>
        public async Task<List<PerformanceCounterCategoryInfo>> GetCategoriesAsync(string machineName = ".")
        {
            return await Task.Run(() =>
            {
                var categories = new List<PerformanceCounterCategoryInfo>();
                try
                {
                    // Get all categories and sort by name
                    foreach (var category in PerformanceCounterCategory.GetCategories(machineName).OrderBy(c => c.CategoryName))
                    {
                        try
                        {
                            categories.Add(new PerformanceCounterCategoryInfo
                            {
                                Name = category.CategoryName,
                                Description = category.CategoryHelp,
                                MachineName = machineName,
                                // Determine if it's a multi-instance category (e.g., Processor has _Total, 0, 1, etc. instances)
                                IsMultiInstance = category.CategoryType == PerformanceCounterCategoryType.MultiInstance
                            });
                        }
                        catch { /* Skip inaccessible categories */ }
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
                    var category = new PerformanceCounterCategory(categoryName, machineName);
                    
                    // Multi-instance categories require an instance name to get counters
                    var perfCounters = category.CategoryType == PerformanceCounterCategoryType.MultiInstance
                        ? category.GetCounters(category.GetInstanceNames().FirstOrDefault() ?? "")
                        : category.GetCounters();

                    foreach (var counter in perfCounters)
                    {
                        counters.Add(new CounterInfo { Name = counter.CounterName, Description = counter.CounterHelp });
                        counter.Dispose(); // Dispose temporarily created counter
                    }
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
        /// <returns>List of instance names, sorted by name</returns>
        public async Task<List<string>> GetInstancesAsync(string categoryName, string machineName = ".")
        {
            return await Task.Run(() =>
            {
                try
                {
                    var category = new PerformanceCounterCategory(categoryName, machineName);
                    return category.CategoryType == PerformanceCounterCategoryType.MultiInstance
                        ? category.GetInstanceNames().OrderBy(i => i).ToList()
                        : new List<string>();
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
                return new PerformanceCounterCategory(categoryName, machineName).CategoryType == PerformanceCounterCategoryType.MultiInstance;
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
                var instances = new PerformanceCounterCategory(categoryName, machineName).GetInstanceNames();
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
        /// Create a performance counter instance.
        /// </summary>
        /// <param name="counterInfo">Counter information</param>
        /// <returns>Created PerformanceCounter instance, or null if creation fails</returns>
        /// <remarks>
        /// This method is thread-safe, protected by a semaphore.
        /// If the counter already exists in cache, the cached instance will be returned directly.
        /// </remarks>
        public PerformanceCounter? CreateCounter(PerformanceCounterInfo counterInfo)
        {
            _semaphore.Wait();
            try { return CreateCounterCore(counterInfo); }
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
        /// 2. If reading fails (InvalidOperationException), attempt to rebuild the counter
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
                    if (counter == null) return _lastValues.GetValueOrDefault(key, 0);
                }

                try
                {
                    // Read counter value and update cache
                    var value = counter.NextValue();
                    _lastValues[key] = value;
                    return value;
                }
                catch (InvalidOperationException)
                {
                    // Counter may have become invalid (e.g., instance disappeared), attempt to rebuild
                    _activeCounters.TryRemove(key, out _);
                    var newCounter = CreateCounterCore(counterInfo);
                    if (newCounter != null)
                    {
                        var value = newCounter.NextValue();
                        _lastValues[key] = value;
                        return value;
                    }
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "Failed reading counter {CounterDisplayName}.", counterInfo.DisplayName); }
            finally { _semaphore.Release(); }

            // Return the last successfully read value as fallback
            return _lastValues.GetValueOrDefault(key, 0);
        }

        /// <summary>
        /// Core implementation for creating counters (internal method).
        /// </summary>
        /// <param name="counterInfo">Counter information</param>
        /// <returns>Created PerformanceCounter instance</returns>
        /// <remarks>
        /// Processing flow:
        /// 1. Check if already exists in cache
        /// 2. If multi-instance category but no instance specified, automatically fill in the first instance
        /// 3. Create PerformanceCounter and perform initial read
        /// 4. Add counter to cache
        /// </remarks>
        private PerformanceCounter? CreateCounterCore(PerformanceCounterInfo counterInfo)
        {
            try
            {
                var key = GetCounterKey(counterInfo);
                
                // If already exists in cache, return directly
                if (_activeCounters.TryGetValue(key, out var existing)) return existing;

                // Automatically fill in instance name (if required but not specified)
                if (CategoryRequiresInstance(counterInfo.CategoryName, counterInfo.MachineName) && string.IsNullOrEmpty(counterInfo.InstanceName))
                {
                    var instance = GetFirstInstance(counterInfo.CategoryName, counterInfo.MachineName);
                    if (string.IsNullOrEmpty(instance)) return null;
                    counterInfo.InstanceName = instance;
                }

                // Create PerformanceCounter instance
                // Third parameter true indicates read-only mode
                var counter = string.IsNullOrEmpty(counterInfo.InstanceName)
                    ? new PerformanceCounter(counterInfo.CategoryName, counterInfo.CounterName, true)
                    : new PerformanceCounter(counterInfo.CategoryName, counterInfo.CounterName, counterInfo.InstanceName, true);

                // Set remote machine (if not local)
                if (!string.IsNullOrEmpty(counterInfo.MachineName) && counterInfo.MachineName != ".")
                    counter.MachineName = counterInfo.MachineName;

                // Perform initial read (some counters require two reads to get valid values)
                try { _ = counter.NextValue(); } catch { /* Initial read may fail, ignore */ }

                // Add to cache
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
                if (_activeCounters.TryRemove(key, out var counter))
                    try { counter.Dispose(); } catch { /* Ignore disposal errors */ }
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
                    if (_activeCounters.TryRemove(key, out var counter))
                        try { counter.Dispose(); } catch { /* Ignore disposal errors */ }
                _lastValues.Clear();
            }
            finally { _semaphore.Release(); }
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
                    foreach (var category in PerformanceCounterCategory.GetCategories(machineName))
                    {
                        // Stop searching when limit is reached
                        if (results.Count >= 50) break;
                        try
                        {
                            bool categoryMatches = category.CategoryName.ToLowerInvariant().Contains(searchLower);
                            var isMulti = category.CategoryType == PerformanceCounterCategoryType.MultiInstance;
                            var instances = isMulti ? category.GetInstanceNames() : Array.Empty<string>();
                            var counters = isMulti && instances.Length > 0 ? category.GetCounters(instances[0]) : (!isMulti ? category.GetCounters() : Array.Empty<PerformanceCounter>());

                            foreach (var counter in counters)
                            {
                                // Category or counter name matches search criteria
                                if (categoryMatches || counter.CounterName.ToLowerInvariant().Contains(searchLower))
                                {
                                    if (isMulti)
                                    {
                                        // Multi-instance category: create results for each instance (maximum 5)
                                        foreach (var instance in instances.Take(5))
                                        {
                                            results.Add(new PerformanceCounterInfo
                                            {
                                                CategoryName = category.CategoryName,
                                                CounterName = counter.CounterName,
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
                                            CategoryName = category.CategoryName,
                                            CounterName = counter.CounterName,
                                            InstanceName = "",
                                            MachineName = machineName,
                                            ColorHex = PerfMonColors.GetColor(colorIndex++)
                                        });
                                    }
                                }
                                counter.Dispose();
                                if (results.Count >= 50) break;
                            }
                        }
                        catch (Exception ex) { _logger.LogWarning(ex, "Failed searching counters in category {CategoryName}.", category.CategoryName); }
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
                    var config = new
                    {
                        Version = "1.0",
                        SavedDate = DateTime.Now,
                        Counters = counters.Select(c => new
                        {
                            c.CategoryName, c.CounterName, c.InstanceName, c.MachineName,
                            c.Scale, c.ColorHex, c.IsVisible, c.Width, c.LineStyle
                        }).ToList()
                    };
                    
                    // Serialize to JSON and write to file
                    System.IO.File.WriteAllText(filePath, System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
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

                    // Read and parse JSON
                    var config = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonDocument>(System.IO.File.ReadAllText(filePath));
                    if (config == null) return new List<PerformanceCounterInfo>();

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
        /// 2. Dispose all active counters
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
}


