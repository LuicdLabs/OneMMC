using System;
using System.Collections.Generic;
using System.IO;
using OneMMC.Core.Localization;

namespace OneMMC.Core.Features.PCManagement.Models.DiskMgmt
{
    /// <summary>
    /// Common interface for disk items displayed in the unified disk list
    /// </summary>
    public interface IDiskItem
    {
        string ItemHeader { get; }
        string ItemDescription { get; }
        string ItemIcon { get; }
        DiskItemType ItemType { get; }
    }

    /// <summary>
    /// Type of disk item for template selection
    /// </summary>
    public enum DiskItemType
    {
        PhysicalDisk,
        CDROM
    }

    /// <summary>
    /// Shared utility class for disk-related formatting operations
    /// </summary>
    public static class DiskFormatHelper
    {
        /// <summary>
        /// Format byte size to human-readable string (e.g., "1.5 GB")
        /// </summary>
        public static string FormatSize(ulong bytes)
        {
            if (bytes == 0) return "0 B";
            string[] sizes = { "B", "KB", "MB", "GB", "TB", "PB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1) { order++; len /= 1024; }
            return $"{len:0.##} {sizes[order]}";
        }
    }

    public class PhysicalDiskInfo : IDiskItem
    {
        private static string GetString(string key) => LocalizationProvider.Current.GetString(ResourceFileNames.DiskManagement, key);

        public string DeviceId { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
        public string InterfaceType { get; set; } = string.Empty;
        public string MediaType { get; set; } = string.Empty;
        public string DiskType { get; set; } = string.Empty;
        public ulong Size { get; set; }
        public ulong UsedSpace { get; set; }
        public uint Index { get; set; }
        public uint Partitions { get; set; }
        public string Status { get; set; } = string.Empty;
        public string HealthStatus { get; set; } = string.Empty;
        public string PartitionStyle { get; set; } = string.Empty;
        public string Caption { get; set; } = string.Empty;
        public string FirmwareRevision { get; set; } = string.Empty;
        public uint BytesPerSector { get; set; }
        public ulong TotalCylinders { get; set; }
        public uint TotalHeads { get; set; }
        public ulong TotalSectors { get; set; }
        public ulong TotalTracks { get; set; }
        public uint TracksPerCylinder { get; set; }
        public uint SectorsPerTrack { get; set; }
        public string PNPDeviceID { get; set; } = string.Empty;
        public bool IsVirtualDisk { get; set; }
        public bool IsOffline { get; set; }
        public bool IsReadOnly { get; set; }
        public List<PartitionInfo> PartitionInfos { get; set; } = new();
        public bool IsExpanded { get; set; }

        public string FormattedSize => DiskFormatHelper.FormatSize(Size);
        public string DisplayName => string.IsNullOrEmpty(Model) || Model == "Unknown" ? $"{GetString("DiskMgmt_DiskLabel")} {Index}" : Model.Trim();
        public string LocalizedHealthStatus => HealthStatus switch
        {
            "Healthy" => GetString("DiskMgmt_Healthy"),
            "Warning" => GetString("DiskMgmt_HealthWarning"),
            "Unhealthy" => GetString("DiskMgmt_HealthUnhealthy"),
            _ => GetString("DiskMgmt_HealthUnknown")
        };
        public string StatusLine => $"{GetString("DiskMgmt_DiskLabel")} {Index} | {(IsOffline ? GetString("DiskMgmt_Offline") : GetString("DiskMgmt_Online"))} | {LocalizedHealthStatus}";
        public string DiskIcon => "\uEDA2";
        public double UsagePercentage => Size > 0 ? (double)UsedSpace / Size * 100 : 0;

        // IDiskItem implementation
        public string ItemHeader => DisplayName;
        public string ItemDescription => StatusLine;
        public string ItemIcon => DiskIcon;
        public DiskItemType ItemType => DiskItemType.PhysicalDisk;
    }

    public class PartitionInfo
    {
        private static string GetString(string key) => LocalizationProvider.Current.GetString(ResourceFileNames.DiskManagement, key);

        public string DeviceId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public ulong Size { get; set; }
        public uint Index { get; set; }
        public string Type { get; set; } = string.Empty;
        public bool Bootable { get; set; }
        public bool PrimaryPartition { get; set; }
        public ulong StartingOffset { get; set; }
        public ulong BlockSize { get; set; }
        public ulong NumberOfBlocks { get; set; }
        public uint DiskIndex { get; set; }
        public string DriveLetter { get; set; } = string.Empty;
        public string VolumeLabel { get; set; } = string.Empty;
        public string FileSystem { get; set; } = string.Empty;
        public ulong FreeSpace { get; set; }
        public ulong TotalSize { get; set; }
        public uint DriveType { get; set; }
        public string VolumeSerialNumber { get; set; } = string.Empty;
        public bool SupportsFileCompression { get; set; }
        
        /// <summary>
        /// GPT Partition Type GUID for identifying system partitions
        /// </summary>
        public Guid GptPartitionTypeGuid { get; set; } = Guid.Empty;
        
        /// <summary>
        /// Indicates if this partition contains boot files (from MSFT_Partition.IsBoot)
        /// </summary>
        public bool IsBoot { get; set; }
        
        /// <summary>
        /// Indicates if this is a system partition (from MSFT_Partition.IsSystem)
        /// </summary>
        public bool IsSystem { get; set; }

        public bool IsUnallocated => Type?.Equals("Unallocated", StringComparison.OrdinalIgnoreCase) == true;
        public bool IsAllocated => !IsUnallocated;

        public string FormattedSize => DiskFormatHelper.FormatSize(TotalSize > 0 ? TotalSize : Size);
        public string FormattedFreeSpace => IsUnallocated ? FormattedSize : DiskFormatHelper.FormatSize(FreeSpace);
        public string FormattedUsedSpace => IsUnallocated ? "0 B" : DiskFormatHelper.FormatSize(TotalSize > FreeSpace ? TotalSize - FreeSpace : 0);

        public string DisplayName
        {
            get
            {
                if (IsUnallocated) return GetString("DiskMgmt_UnallocatedSpace");
                // Provide meaningful names for system partitions without drive letters
                if (IsEfiSystemPartition) return "EFI System Partition";
                if (IsRecoveryPartition) return "Recovery Partition";
                if (IsMsrPartition) return "Microsoft Reserved Partition";
                if (!string.IsNullOrEmpty(VolumeLabel)) return VolumeLabel;
                if (!string.IsNullOrEmpty(DriveLetter)) return $"{GetString("DiskMgmt_LocalDiskLabel")} ({DriveLetter})";
                return GetString("DiskMgmt_NoLabel");
            }
        }

        public string StatusLine => IsUnallocated 
            ? $"{FormattedSize} | {GetString("DiskMgmt_Unallocated")}" 
            : $"{FormattedSize} | {(string.IsNullOrEmpty(FileSystem) ? GetString("DiskMgmt_HealthUnknown") : FileSystem)} | {LocalizedStatus} | {LocalizedHealthStatus}";
        public string Status => IsUnallocated ? "Unallocated" : "Online";
        public string LocalizedStatus => IsUnallocated ? GetString("DiskMgmt_Unallocated") : GetString("DiskMgmt_Online");
        public string PartitionType => Type ?? "";
        public string HealthStatus => IsUnallocated ? string.Empty : "Healthy";
        public string LocalizedHealthStatus => IsUnallocated ? string.Empty : GetString("DiskMgmt_Healthy");
        public bool HasDriveLetter => !string.IsNullOrEmpty(DriveLetter);
        public double UsagePercentage => IsUnallocated ? 0 : (TotalSize > 0 ? (double)(TotalSize - FreeSpace) / TotalSize * 100 : 0);
        
        #region System Partition Identification
        
        // Consolidated partition type definitions with metadata
        private static readonly Dictionary<Guid, PartitionTypeInfo> KnownPartitionTypes = new()
        {
            [new("C12A7328-F81F-11D2-BA4B-00A0C93EC93B")] = new("EFI System Partition", "SYSTEM", DangerLevel.Blocked, 50UL * 1024 * 1024, 550UL * 1024 * 1024),
            [new("E3C9E316-0B5C-4DB8-817D-F92DF00215AE")] = new("Microsoft Reserved Partition", "RESERVED", DangerLevel.Blocked),
            [new("DE94BBA4-06D1-4D40-A16A-BFD50179D6AC")] = new("Recovery Partition", "RECOVERY", DangerLevel.Blocked, 350UL * 1024 * 1024, 20UL * 1024 * 1024 * 1024),
            [new("8DA63339-0007-60C0-C436-083AC8230908")] = new("Windows RE Partition", "RECOVERY", DangerLevel.Blocked, 350UL * 1024 * 1024, 20UL * 1024 * 1024 * 1024),
            [new("F0FD8DC9-0438-4741-8E12-7E0C412A9930")] = new("OEM Recovery Partition", "RECOVERY", DangerLevel.Blocked, 350UL * 1024 * 1024, 20UL * 1024 * 1024 * 1024),
            [new("21686148-6449-6E6F-744E-656564454649")] = new("BIOS Boot Partition", "BIOS BOOT", DangerLevel.Blocked),
            [new("EBD0A0A2-B9E5-4433-87C0-68B6B72699C7")] = new("Basic Data Partition", "BASIC", DangerLevel.Safe)
        };

        private record PartitionTypeInfo(string DisplayName, string TypeKeyword, DangerLevel DangerLevel, ulong? MinSize = null, ulong? MaxSize = null);
        
        /// <summary>
        /// Gets the partition type information based on GUID and other properties
        /// </summary>
        private PartitionTypeInfo? GetPartitionTypeInfo()
        {
            if (IsUnallocated) return new("Unallocated Space", "UNALLOCATED", DangerLevel.Safe);

            // Primary: Check by GUID
            if (GptPartitionTypeGuid != Guid.Empty && KnownPartitionTypes.TryGetValue(GptPartitionTypeGuid, out var typeInfo))
                return typeInfo;
            
            // Secondary: Check by type string
            var typeUpper = Type?.ToUpperInvariant() ?? "";
            foreach (var kvp in KnownPartitionTypes.Values)
            {
                if (typeUpper == kvp.TypeKeyword)
                {
                    // Validate size constraints if specified
                    if (kvp.MinSize.HasValue || kvp.MaxSize.HasValue)
                    {
                        var partitionSize = TotalSize > 0 ? TotalSize : Size;
                        if (kvp.MinSize.HasValue && partitionSize < kvp.MinSize.Value) continue;
                        if (kvp.MaxSize.HasValue && partitionSize > kvp.MaxSize.Value) continue;
                    }
                    return kvp;
                }
            }
            
            // Heuristic: Recovery partition detection for unknown GUIDs
            if (IsRecoveryPartitionHeuristic()) 
                return new("Recovery Partition", "RECOVERY", DangerLevel.Blocked);
            
            return null;
        }
        
        /// <summary>
        /// Heuristic detection for recovery partitions with unknown GUIDs
        /// </summary>
        private bool IsRecoveryPartitionHeuristic()
        {
            if (IsUnallocated) return false;

            // Label-based detection
            var label = VolumeLabel?.ToUpperInvariant() ?? "";
            if (label.Contains("RECOVERY") || label.Contains("WINRE")) return true;
            
            // Hidden partition with recovery-like characteristics
            if (string.IsNullOrEmpty(DriveLetter) && 
                GptPartitionTypeGuid != Guid.Empty &&
                !KnownPartitionTypes.ContainsKey(GptPartitionTypeGuid))
            {
                const ulong MIN_RECOVERY_SIZE = 350UL * 1024 * 1024;
                const ulong MAX_RECOVERY_SIZE = 20UL * 1024 * 1024 * 1024;
                var partitionSize = TotalSize > 0 ? TotalSize : Size;
                
                if (partitionSize >= MIN_RECOVERY_SIZE && partitionSize <= MAX_RECOVERY_SIZE)
                {
                    var typeUpper = Type?.ToUpperInvariant() ?? "";
                    return typeUpper.Contains("UNKNOWN") || string.IsNullOrEmpty(FileSystem);
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// Checks if this is an EFI System Partition (ESP)
        /// </summary>
        public bool IsEfiSystemPartition => GetPartitionTypeInfo()?.DisplayName == "EFI System Partition";
        
        /// <summary>
        /// Checks if this is a Recovery Partition
        /// </summary>
        public bool IsRecoveryPartition => GetPartitionTypeInfo()?.TypeKeyword == "RECOVERY";
        
        /// <summary>
        /// Checks if this is a Microsoft Reserved Partition (MSR)
        /// </summary>
        public bool IsMsrPartition => GetPartitionTypeInfo()?.DisplayName == "Microsoft Reserved Partition";
        
        /// <summary>
        /// Checks if this is a BIOS Boot Partition (used by GRUB on GPT disks with BIOS boot)
        /// </summary>
        public bool IsBiosBootPartition => GetPartitionTypeInfo()?.DisplayName == "BIOS Boot Partition";
        
        /// <summary>
        /// System drive letter, sourced from environment variable or system folder root directory, format "X:"
        /// </summary>
        private static string SystemDriveLetter => _systemDriveLazy.Value;

        private static readonly Lazy<string> _systemDriveLazy = new(() =>
        {
            var systemDrive = Environment.GetEnvironmentVariable("SystemDrive");
            if (string.IsNullOrWhiteSpace(systemDrive))
                systemDrive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System));

            systemDrive = (systemDrive ?? string.Empty).Trim().TrimEnd('\\', '/');
            if (!systemDrive.EndsWith(":", StringComparison.Ordinal))
                systemDrive += ":";

            return systemDrive.ToUpperInvariant();
        });

        /// <summary>
        /// Checks if this is the system drive (resolved at runtime instead of hardcoded C:)
        /// </summary>
        public bool IsSystemDrive => !string.IsNullOrWhiteSpace(DriveLetter) &&
                                     DriveLetter.Trim().TrimEnd(':').ToUpperInvariant() + ":" == SystemDriveLetter;
        
        /// <summary>
        /// Checks if this partition is absolutely critical and operations should be blocked
        /// (All system partitions including EFI, MSR, BIOS Boot, Recovery)
        /// </summary>
        public bool IsCriticalSystemPartition => 
            IsEfiSystemPartition || 
            IsMsrPartition || 
            IsBiosBootPartition ||
            IsRecoveryPartition;
        
        /// <summary>
        /// Checks if this partition is important and operations should be blocked
        /// (All system-related partitions)
        /// </summary>
        public bool IsImportantPartition =>
            IsCriticalSystemPartition ||
            IsBoot ||       // Contains boot files (from WMI)
            IsSystem ||     // Is system partition (from WMI)
            IsSystemDrive;  // Is system drive
        
        /// <summary>
        /// Checks if this partition needs blocking before dangerous operations
        /// (All system-related partitions for maximum safety)
        /// </summary>
        public bool IsWarningPartition =>
            IsImportantPartition;
        
        /// <summary>
        /// Gets the danger level for this partition to determine appropriate user warnings
        /// </summary>
        public DangerLevel GetDangerLevel()
        {
            // Check partition type first (most reliable)
            var partitionTypeInfo = GetPartitionTypeInfo();
            if (partitionTypeInfo != null)
                return partitionTypeInfo.DangerLevel;
            
            // Block all WMI-detected system properties for maximum safety
            if (IsSystem || IsBoot || IsSystemDrive) 
                return DangerLevel.Blocked;
            
            return DangerLevel.Safe;
        }
        
        /// <summary>
        /// For backward compatibility - checks if operations should be blocked
        /// Use GetDangerLevel() for more nuanced control
        /// Now returns false for all system partitions for maximum safety
        /// </summary>
        public bool AllowDangerousOperations => !IsUnallocated && GetDangerLevel() != DangerLevel.Blocked;
        
        /// <summary>
        /// Checks if drive letter operations (assign/change/remove) should be allowed
        /// All system partitions should NOT have drive letters assigned as it can cause system issues
        /// </summary>
        public bool AllowDriveLetterOperations => 
            !IsUnallocated &&
            !IsCriticalSystemPartition && 
            !IsRecoveryPartition &&
            !IsBoot &&
            !IsSystem;
        
        /// <summary>
        /// Gets a warning message for system-protected partitions
        /// Returns null if no warning is needed
        /// </summary>
        public string? SystemProtectionWarning
        {
            get
            {
                var partitionTypeInfo = GetPartitionTypeInfo();
                if (partitionTypeInfo != null)
                {
                    return partitionTypeInfo.DisplayName switch
                    {
                        "EFI System Partition" => "âš ï¸ EFI System Partition - This partition is required for boot, do not modify",
                        "Microsoft Reserved Partition" => "âš ï¸ Microsoft Reserved Partition - This partition is required for system operation",
                        "BIOS Boot Partition" => "âš ï¸ BIOS Boot Partition - This partition is required for boot",
                        var name when name.Contains("Recovery") => "âš ï¸ Recovery Partition - This partition is used for system recovery, do not modify",
                        _ => null
                    };
                }
                
                if (IsSystemDrive) return $"âš ï¸ System Drive ({SystemDriveLetter}) - Contains Windows operating system";
                if (IsBoot || IsSystem) return "âš ï¸ Boot/System Partition - Contains boot files";
                return null;
            }
        }
        
        #endregion
    }


    public class VolumeInfo
    {
        public string DeviceId { get; set; } = string.Empty;
        public string DriveLetter { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string FileSystem { get; set; } = string.Empty;
        public ulong Capacity { get; set; }
        public ulong FreeSpace { get; set; }
        public uint DriveType { get; set; }
        public string DriveTypeDescription { get; set; } = string.Empty;
        public uint SerialNumber { get; set; }
        public bool BootVolume { get; set; }
        public bool SystemVolume { get; set; }
        public bool Compressed { get; set; }
        public bool IndexingEnabled { get; set; }

        public string FormattedCapacity => DiskFormatHelper.FormatSize(Capacity);
        public string FormattedFreeSpace => DiskFormatHelper.FormatSize(FreeSpace);
        public double UsedPercentage => Capacity > 0 ? ((double)(Capacity - FreeSpace) / Capacity) * 100 : 0;
    }

    public class CDROMInfo : IDiskItem
    {
        private static string GetString(string key) => LocalizationProvider.Current.GetString(ResourceFileNames.DiskManagement, key);

        public string DeviceId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Caption { get; set; } = string.Empty;
        public string Drive { get; set; } = string.Empty;
        public string Manufacturer { get; set; } = string.Empty;
        public bool MediaLoaded { get; set; }
        public string MediaType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public uint SCSIBus { get; set; }
        public ushort SCSIPort { get; set; }
        public ushort SCSITargetId { get; set; }
        public ushort SCSILogicalUnit { get; set; }
        public string VolumeName { get; set; } = string.Empty;
        public ulong Size { get; set; }
        public ulong MaxMediaSize { get; set; }

        public string StatusLine => MediaLoaded ? $"CD-ROM | {MediaType}" : $"CD-ROM | {GetString("DiskMgmt_NoMedia")}";
        public string DisplayStatus => MediaLoaded ? $"{GetString("DiskMgmt_MediaLoadedLabel")} {VolumeName}" : GetString("DiskMgmt_NoMedia");
        public string FormattedSize => MediaLoaded ? DiskFormatHelper.FormatSize(Size) : string.Empty;

        // IDiskItem implementation
        public string ItemHeader => Caption;
        public string ItemDescription => StatusLine;
        public string ItemIcon => "\uE958";
        public DiskItemType ItemType => DiskItemType.CDROM;
    }

    public class VolumeProperties
    {
        public string DeviceId { get; set; } = string.Empty;
        public string DriveLetter { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string FileSystem { get; set; } = string.Empty;
        public ulong Capacity { get; set; }
        public ulong FreeSpace { get; set; }
        public uint DriveType { get; set; }
        public uint SerialNumber { get; set; }
        public bool BootVolume { get; set; }
        public bool SystemVolume { get; set; }
        public bool PageFilePresent { get; set; }
        public bool Compressed { get; set; }
        public bool IndexingEnabled { get; set; }
        public ulong BlockSize { get; set; }
        public uint MaximumFileNameLength { get; set; }
        public bool SupportsDiskQuotas { get; set; }
        public bool SupportsFileBasedCompression { get; set; }
        public bool Automount { get; set; }
    }

    public class StoragePoolInfo
    {
        public string FriendlyName { get; set; } = string.Empty;
        public ushort HealthStatus { get; set; }
        public ushort OperationalStatus { get; set; }
        public ulong Size { get; set; }
        public ulong AllocatedSize { get; set; }
        public bool IsReadOnly { get; set; }

        public string HealthStatusText => HealthStatus switch { 0 => "Healthy", 1 => "Warning", 2 => "Unhealthy", _ => "Unknown" };
    }

    public class UnallocatedSpace
    {
        public uint DiskIndex { get; set; }
        public ulong Offset { get; set; }
        public ulong Size { get; set; }

        public string FormattedSize => DiskFormatHelper.FormatSize(Size);
        public string FormattedOffset => DiskFormatHelper.FormatSize(Offset);
    }
    
    /// <summary>
    /// Danger level for partition operations - used for graduated protection
    /// </summary>
    public enum DangerLevel
    {
        /// <summary>Safe to perform operations without warnings</summary>
        Safe,
        /// <summary>Should warn user before operation (currently unused - all system partitions are blocked)</summary>
        Warning,
        /// <summary>Operations should be blocked (All system partitions for maximum safety)</summary>
        Blocked
    }
}


