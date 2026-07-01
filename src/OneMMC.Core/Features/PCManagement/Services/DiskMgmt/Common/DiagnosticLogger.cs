using System;
using System.Diagnostics;
using Debug = System.Diagnostics.Trace;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace OneMMC.Core.Features.PCManagement.Services.DiskMgmt.Common
{
    /// <summary>
    /// Enhanced diagnostic logger for disk management operations
    /// Provides detailed context information for debugging
    /// </summary>
    public static class DiagnosticLogger
    {
        private const string LogPrefix = "[DiskMgmt]";
        private static ILogger _logger = NullLogger.Instance;

        public static void ConfigureLogger(ILogger logger)
        {
            _logger = logger ?? NullLogger.Instance;
        }

        /// <summary>
        /// Log operation start with context
        /// </summary>
        public static void LogOperationStart(
            string operation,
            uint? diskIndex = null,
            uint? partitionIndex = null,
            string? driveLetter = null,
            [CallerMemberName] string? callerName = null)
        {
            var context = BuildContext(diskIndex, partitionIndex, driveLetter);
            _logger.LogDebug($"{LogPrefix} [{callerName}] Starting: {operation}{context}");
        }

        /// <summary>
        /// Log operation success with context
        /// </summary>
        public static void LogOperationSuccess(
            string operation,
            string? message = null,
            uint? diskIndex = null,
            uint? partitionIndex = null,
            string? driveLetter = null,
            [CallerMemberName] string? callerName = null)
        {
            var context = BuildContext(diskIndex, partitionIndex, driveLetter);
            var msg = string.IsNullOrEmpty(message) ? "" : $" - {message}";
            _logger.LogDebug($"{LogPrefix} [{callerName}] ??Success: {operation}{context}{msg}");
        }

        /// <summary>
        /// Log operation error with full context
        /// </summary>
        public static void LogOperationError(
            string operation,
            Exception ex,
            uint? diskIndex = null,
            uint? partitionIndex = null,
            string? driveLetter = null,
            string? additionalContext = null,
            [CallerMemberName] string? callerName = null)
        {
            var context = BuildContext(diskIndex, partitionIndex, driveLetter);
            var additional = string.IsNullOrEmpty(additionalContext) ? "" : $" | {additionalContext}";
            
            _logger.LogDebug($"{LogPrefix} [{callerName}] ??Error: {operation}{context}{additional}");
            _logger.LogDebug($"{LogPrefix} [{callerName}]   Exception Type: {ex.GetType().Name}");
            _logger.LogDebug($"{LogPrefix} [{callerName}]   Message: {ex.Message}");
            
            if (ex.InnerException != null)
            {
                _logger.LogDebug($"{LogPrefix} [{callerName}]   Inner Exception: {ex.InnerException.Message}");
            }
            
            // Log stack trace for debugging (first 3 lines)
            var stackLines = ex.StackTrace?.Split('\n');
            if (stackLines != null && stackLines.Length > 0)
            {
                _logger.LogDebug($"{LogPrefix} [{callerName}]   Stack Trace:");
                for (int i = 0; i < Math.Min(3, stackLines.Length); i++)
                {
                    _logger.LogDebug($"{LogPrefix} [{callerName}]     {stackLines[i].Trim()}");
                }
            }
        }

        /// <summary>
        /// Log WMI operation error with error code
        /// </summary>
        public static void LogWmiError(
            string operation,
            uint errorCode,
            uint? diskIndex = null,
            uint? partitionIndex = null,
            string? driveLetter = null,
            [CallerMemberName] string? callerName = null)
        {
            var context = BuildContext(diskIndex, partitionIndex, driveLetter);
            var errorMessage = ErrorMessages.GetMsftErrorMessage(errorCode);
            
            _logger.LogDebug($"{LogPrefix} [{callerName}] ??WMI Error: {operation}{context}");
            _logger.LogDebug($"{LogPrefix} [{callerName}]   Error Code: {errorCode}");
            _logger.LogDebug($"{LogPrefix} [{callerName}]   Error Message: {errorMessage}");
        }

        /// <summary>
        /// Log warning message
        /// </summary>
        public static void LogWarning(
            string message,
            uint? diskIndex = null,
            uint? partitionIndex = null,
            string? driveLetter = null,
            [CallerMemberName] string? callerName = null)
        {
            var context = BuildContext(diskIndex, partitionIndex, driveLetter);
            _logger.LogDebug($"{LogPrefix} [{callerName}] ??Warning: {message}{context}");
        }

        /// <summary>
        /// Log informational message
        /// </summary>
        public static void LogInfo(
            string message,
            uint? diskIndex = null,
            uint? partitionIndex = null,
            string? driveLetter = null,
            [CallerMemberName] string? callerName = null)
        {
            var context = BuildContext(diskIndex, partitionIndex, driveLetter);
            _logger.LogDebug($"{LogPrefix} [{callerName}] ??Info: {message}{context}");
        }

        /// <summary>
        /// Log debug message
        /// </summary>
        public static void LogDebug(
            string message,
            uint? diskIndex = null,
            uint? partitionIndex = null,
            string? driveLetter = null,
            [CallerMemberName] string? callerName = null)
        {
            var context = BuildContext(diskIndex, partitionIndex, driveLetter);
            _logger.LogDebug($"{LogPrefix} [{callerName}] Debug: {message}{context}");
        }

        /// <summary>
        /// Log disk state for diagnostics
        /// </summary>
        public static void LogDiskState(
            uint diskIndex,
            string partitionStyle,
            bool isOffline,
            bool isReadOnly,
            string healthStatus,
            [CallerMemberName] string? callerName = null)
        {
            _logger.LogDebug($"{LogPrefix} [{callerName}] Disk {diskIndex} State:");
            _logger.LogDebug($"{LogPrefix} [{callerName}]   Partition Style: {partitionStyle}");
            _logger.LogDebug($"{LogPrefix} [{callerName}]   Is Offline: {isOffline}");
            _logger.LogDebug($"{LogPrefix} [{callerName}]   Is ReadOnly: {isReadOnly}");
            _logger.LogDebug($"{LogPrefix} [{callerName}]   Health Status: {healthStatus}");
        }

        /// <summary>
        /// Log partition state for diagnostics
        /// </summary>
        public static void LogPartitionState(
            uint diskIndex,
            uint partitionIndex,
            string? driveLetter,
            string type,
            ulong size,
            [CallerMemberName] string? callerName = null)
        {
            var letter = string.IsNullOrEmpty(driveLetter) ? "None" : driveLetter;
            _logger.LogDebug($"{LogPrefix} [{callerName}] Partition State:");
            _logger.LogDebug($"{LogPrefix} [{callerName}]   Disk: {diskIndex}, Partition: {partitionIndex}");
            _logger.LogDebug($"{LogPrefix} [{callerName}]   Drive Letter: {letter}");
            _logger.LogDebug($"{LogPrefix} [{callerName}]   Type: {type}");
            _logger.LogDebug($"{LogPrefix} [{callerName}]   Size: {FormatHelper.FormatSize(size)}");
        }

        /// <summary>
        /// Build context string from parameters
        /// </summary>
        private static string BuildContext(uint? diskIndex, uint? partitionIndex, string? driveLetter)
        {
            var parts = new System.Collections.Generic.List<string>();

            if (diskIndex.HasValue)
                parts.Add($"Disk={diskIndex.Value}");

            if (partitionIndex.HasValue)
                parts.Add($"Partition={partitionIndex.Value}");

            if (!string.IsNullOrEmpty(driveLetter))
                parts.Add($"Drive={driveLetter}");

            return parts.Count > 0 ? $" [{string.Join(", ", parts)}]" : "";
        }
    }
}



