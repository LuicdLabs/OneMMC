using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using ManagementTools.Core.Features.PCManagement.Models.DiskMgmt;
using ManagementTools.Core.Features.PCManagement.Services.DiskMgmt.Common;

namespace ManagementTools.Core.Features.PCManagement.Services.DiskMgmt
{
    internal sealed class DiskManagementInfrastructure
    {
        private readonly DiskInformationQueries _diskInformationQueries;

        public DiskManagementInfrastructure(DiskInformationQueries diskInformationQueries)
        {
            _diskInformationQueries = diskInformationQueries;
        }

        #region System Disk Protection

        private static uint? _cachedSystemDiskIndex;
        private static string? _cachedSystemDriveLetter;

        /// <summary>
        /// Get system drive letter (from environment variable/system path), format: "C:".
        /// </summary>
        private static string GetSystemDriveLetter()
        {
            if (!string.IsNullOrWhiteSpace(_cachedSystemDriveLetter))
                return _cachedSystemDriveLetter!;

            var systemDrive = Environment.GetEnvironmentVariable("SystemDrive");
            if (string.IsNullOrWhiteSpace(systemDrive))
                systemDrive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System));

            systemDrive = (systemDrive ?? string.Empty).Trim().TrimEnd('\\', '/');

            if (!systemDrive.EndsWith(":", StringComparison.Ordinal))
                systemDrive += ":";

            _cachedSystemDriveLetter = systemDrive.ToUpperInvariant();
            return _cachedSystemDriveLetter;
        }

        /// <summary>
        /// Get system disk index (disk containing the system drive)
        /// </summary>
        public uint? GetSystemDiskIndex()
        {
            if (_cachedSystemDiskIndex.HasValue)
                return _cachedSystemDiskIndex;

            try
            {
                var scope = new ManagementScope(DiskManagementConstants.StorageWmiScope);
                scope.Connect();

                var systemDriveLetter = GetSystemDriveLetter();
                var driveLetterChar = systemDriveLetter.TrimEnd(':');
                
                // Method 1: Query MSFT_Partition directly by DriveLetter (char format)
                try
                {
                    var partitionQuery = new ObjectQuery($"SELECT DiskNumber FROM MSFT_Partition WHERE DriveLetter = '{driveLetterChar}'");
                    using var partitionSearcher = new ManagementObjectSearcher(scope, partitionQuery);

                    foreach (ManagementObject partition in partitionSearcher.Get())
                    {
                        using (partition) // Ensure proper disposal
                        {
                            var diskNumber = GetWmiPropertySafe<uint>(partition, "DiskNumber");
                            _cachedSystemDiskIndex = diskNumber;
                            return diskNumber;
                        }
                    }
                }
                catch (Exception ex)
                {
                    DiagnosticLogger.LogDebug($"[{nameof(GetSystemDiskIndex)}] Method 1 (MSFT_Partition) failed: {ex.Message}");
                }

                // Method 2: Fallback - Use Win32_LogicalDiskToPartition association
                try
                {
                    using var assocSearcher = new ManagementObjectSearcher(
                        DiskManagementConstants.CimV2WmiScope,
                        $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{systemDriveLetter}'}} WHERE AssocClass=Win32_LogicalDiskToPartition");

                    foreach (ManagementObject partition in assocSearcher.Get())
                    {
                        using (partition) // Ensure proper disposal
                        {
                            var diskIndex = GetWmiPropertySafe<uint>(partition, "DiskIndex");
                            _cachedSystemDiskIndex = diskIndex;
                            return diskIndex;
                        }
                    }
                }
                catch (Exception ex)
                {
                    DiagnosticLogger.LogDebug($"[{nameof(GetSystemDiskIndex)}] Method 2 (Win32_LogicalDiskToPartition) failed: {ex.Message}");
                }

                // Method 3: Last resort - scan all partitions
                try
                {
                    using var allPartitionsSearcher = new ManagementObjectSearcher(scope, 
                        new ObjectQuery("SELECT DiskNumber, DriveLetter FROM MSFT_Partition"));

                    foreach (ManagementObject partition in allPartitionsSearcher.Get())
                    {
                        using (partition) // Ensure proper disposal
                        {
                            var letter = GetWmiPropertySafe<char>(partition, "DriveLetter");
                            if (letter != '\0' && $"{letter}:" == systemDriveLetter)
                            {
                                var diskNumber = GetWmiPropertySafe<uint>(partition, "DiskNumber");
                                _cachedSystemDiskIndex = diskNumber;
                                return diskNumber;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    DiagnosticLogger.LogDebug($"[{nameof(GetSystemDiskIndex)}] Method 3 (scan all partitions) failed: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                DiagnosticLogger.LogOperationError(nameof(GetSystemDiskIndex), ex);
            }

            return null;
        }

        /// <summary>
        /// Check if disk is system disk
        /// </summary>
        public bool IsSystemDisk(uint diskIndex)
        {
            var systemDiskIndex = GetSystemDiskIndex();
            return systemDiskIndex.HasValue && systemDiskIndex.Value == diskIndex;
        }

        /// <summary>
        /// Check if partition is system partition
        /// </summary>
        public bool IsSystemPartition(uint diskIndex, uint partitionIndex)
        {
            try
            {
                var partitions = _diskInformationQueries.GetPartitionsForDisk(diskIndex);
                if (partitionIndex < partitions.Count)
                {
                    var partition = partitions[(int)partitionIndex];
                    return IsSystemDriveLetter(partition.DriveLetter);
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Check if drive letter is system drive
        /// </summary>
        public static bool IsSystemDriveLetter(string driveLetter)
        {
            if (string.IsNullOrWhiteSpace(driveLetter)) return false;

            var normalized = driveLetter.Trim().TrimEnd(':').ToUpperInvariant() + ":";
            var systemDrive = GetSystemDriveLetter();

            return string.Equals(normalized, systemDrive, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Check if disk contains critical system partitions
        /// Note: Only returns true for disks that contain partitions critical to system boot
        /// Reserved partitions (MSR) alone do not make a disk critical unless it's the system disk
        /// </summary>
        public bool DiskContainsCriticalPartitions(uint diskIndex)
        {
            // If this is the system disk, it definitely contains critical partitions
            if (IsSystemDisk(diskIndex))
                return true;

            try
            {
                var partitions = _diskInformationQueries.GetPartitionsForDisk(diskIndex);
                foreach (var partition in partitions)
                {
                    // System drive - always critical
                    if (IsSystemDriveLetter(partition.DriveLetter))
                        return true;

                    // EFI System Partition - critical for boot (but only on system disk)
                    // Recovery partition - important but not critical on non-system disks
                    // MSR (Reserved) partition - NOT critical on non-system disks, can be cleaned
                    
                    // Only EFI partition on a disk with system drive is truly critical
                    if (partition.IsEfiSystemPartition)
                    {
                        // Check if this disk also contains the system drive
                        foreach (var p in partitions)
                        {
                            if (IsSystemDriveLetter(p.DriveLetter))
                                return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Validate disk operation safety
        /// </summary>
        public string? ValidateDiskOperationSafety(uint diskIndex, bool allowSystemDiskWithWarning = false)
        {
            if (IsSystemDisk(diskIndex))
            {
                if (allowSystemDiskWithWarning)
                    return null;
                return "Cannot perform this operation on system disk. This disk contains the Windows operating system.";
            }

            if (DiskContainsCriticalPartitions(diskIndex))
            {
                if (allowSystemDiskWithWarning)
                    return null;
                return "This disk contains critical system partitions (EFI, Recovery partition). Operation may cause system boot failure.";
            }

            return null;
        }

        /// <summary>
        /// Validate partition operation safety
        /// </summary>
        public string? ValidatePartitionOperationSafety(uint diskIndex, uint partitionIndex)
        {
            try
            {
                var partitions = _diskInformationQueries.GetPartitionsForDisk(diskIndex);
                if (partitionIndex < partitions.Count)
                {
                    var partition = partitions[(int)partitionIndex];

                    if (partition.IsSystemDrive)
                        return $"Cannot perform this operation on system partition. Drive \"{GetSystemDriveLetter()}\" contains the Windows operating system.";

                    if (partition.IsEfiSystemPartition)
                        return "Cannot delete EFI System Partition. This partition is required for boot.";

                    if (partition.IsRecoveryPartition)
                        return "Cannot delete Recovery Partition. This partition is used for system recovery.";

                    if (partition.IsMsrPartition)
                        return "Cannot delete Microsoft Reserved Partition. This partition is required for system operation.";

                    if (partition.IsCriticalSystemPartition)
                        return "Cannot perform this operation on this critical system partition.";
                }
            }
            catch { }

            return null;
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Safely get WMI property value
        /// </summary>
        private static T GetWmiPropertySafe<T>(ManagementBaseObject obj, string propertyName, T defaultValue = default!)
        {
            try
            {
                var value = obj[propertyName];
                if (value == null) return defaultValue;

                var targetType = typeof(T);
                var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

                if (underlyingType == typeof(string))
                    return (T)(object)(value.ToString()?.Trim() ?? "");
                
                if (underlyingType == typeof(bool))
                    return (T)(object)Convert.ToBoolean(value);
                
                if (underlyingType == typeof(uint))
                    return (T)(object)Convert.ToUInt32(value);
                
                if (underlyingType == typeof(ushort))
                    return (T)(object)Convert.ToUInt16(value);
                
                if (underlyingType == typeof(ulong))
                    return (T)(object)Convert.ToUInt64(value);
                
                if (underlyingType == typeof(int))
                    return (T)(object)Convert.ToInt32(value);

                if (underlyingType == typeof(char))
                    return (T)(object)Convert.ToChar(value);

                return (T)Convert.ChangeType(value, underlyingType);
            }
            catch
            {
                return defaultValue;
            }
        }

        private static string SafeString(object? value, string defaultValue = "")
        {
            try { return value?.ToString()?.Trim() ?? defaultValue; }
            catch { return defaultValue; }
        }

        private static ulong SafeULong(object? value, ulong defaultValue = 0)
        {
            try
            {
                if (value == null) return defaultValue;
                if (value is ulong ulongVal) return ulongVal;
                if (value is long longVal && longVal >= 0) return (ulong)longVal;
                if (value is uint uintVal) return uintVal;
                if (value is int intVal && intVal >= 0) return (ulong)intVal;
                if (value is string strVal && ulong.TryParse(strVal, out var parsed)) return parsed;
                return Convert.ToUInt64(value);
            }
            catch { return defaultValue; }
        }

        private static uint SafeUInt(object? value, uint defaultValue = 0)
        {
            try
            {
                if (value == null) return defaultValue;
                if (value is uint uintVal) return uintVal;
                if (value is int intVal && intVal >= 0) return (uint)intVal;
                if (value is long longVal && longVal >= 0 && longVal <= uint.MaxValue) return (uint)longVal;
                if (value is string strVal && uint.TryParse(strVal, out var parsed)) return parsed;
                return Convert.ToUInt32(value);
            }
            catch { return defaultValue; }
        }

        private static bool SafeBool(object? value, bool defaultValue = false)
        {
            try { return Convert.ToBoolean(value ?? defaultValue); }
            catch { return defaultValue; }
        }

        /// <summary>
        /// Check if drive letter is already in use
        /// </summary>
        public static bool IsDriveLetterInUse(string driveLetter)
        {
            if (string.IsNullOrWhiteSpace(driveLetter)) return false;

            driveLetter = driveLetter.Trim().TrimEnd(':').ToUpperInvariant() + ":";

            try
            {
                // Check if drive exists
                return Directory.Exists(driveLetter);
            }
            catch
            {
                return false;
            }
        }

        #endregion
    }
}


