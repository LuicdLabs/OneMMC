using System;
using System.Collections.Generic;
using System.Diagnostics;
using Debug = System.Diagnostics.Trace;
using System.IO;
using System.Linq;
using System.Management;
using ManagementTools.Core.Features.PCManagement.Services.DiskMgmt.Common;
using ManagementTools.Core.Features.PCManagement.Models.DiskMgmt;
using ManagementTools.Core.Infrastructure.Wmi;

namespace ManagementTools.Core.Features.PCManagement.Services.DiskMgmt
{
    internal sealed class DiskInformationQueries
    {
        private readonly DiskManagementService _service;

        public DiskInformationQueries(DiskManagementService service)
        {
            _service = service;
        }

        #region Disk Information ??Storage Management API (MSFT_*)

        /// <summary>
        /// Get physical disk information ??merges MSFT_Disk + Win32_DiskDrive data.
        /// </summary>
        public List<PhysicalDiskInfo> GetPhysicalDisks()
        {
            DiagnosticLogger.LogInfo("Starting physical disk enumeration.");
            var disks = new List<PhysicalDiskInfo>();

            try
            {
                var msftDisks = GetMsftDiskInfo();

                using var searcher = new ManagementObjectSearcher(
                    DiskManagementConstants.CimV2WmiScope, "SELECT * FROM Win32_DiskDrive");

                foreach (ManagementObject disk in searcher.GetAndDispose())
                {
                    using (disk)
                    {
                        var index = SafeUInt(disk["Index"]);
                        var diskInfo = new PhysicalDiskInfo
                        {
                            DeviceId = SafeString(disk["DeviceID"]),
                            Model = SafeString(disk["Model"], "Unknown"),
                            SerialNumber = SafeString(disk["SerialNumber"]),
                            InterfaceType = SafeString(disk["InterfaceType"]),
                            MediaType = SafeString(disk["MediaType"]),
                            Size = SafeULong(disk["Size"]),
                            Index = index,
                            Partitions = SafeUInt(disk["Partitions"]),
                            Status = SafeString(disk["Status"], "OK"),
                            Caption = SafeString(disk["Caption"]),
                            FirmwareRevision = SafeString(disk["FirmwareRevision"]),
                            BytesPerSector = SafeUInt(disk["BytesPerSector"], 512),
                            TotalCylinders = SafeULong(disk["TotalCylinders"]),
                            TotalHeads = SafeUInt(disk["TotalHeads"]),
                            TotalSectors = SafeULong(disk["TotalSectors"]),
                            TotalTracks = SafeULong(disk["TotalTracks"]),
                            TracksPerCylinder = SafeUInt(disk["TracksPerCylinder"]),
                            SectorsPerTrack = SafeUInt(disk["SectorsPerTrack"]),
                            PNPDeviceID = SafeString(disk["PNPDeviceID"])
                        };

                        // Merge MSFT_Disk information
                        if (msftDisks.TryGetValue(index, out var msftInfo))
                        {
                            diskInfo.PartitionStyle = msftInfo.PartitionStyle;
                            diskInfo.HealthStatus = msftInfo.HealthStatus;
                            diskInfo.IsOffline = msftInfo.IsOffline;
                            diskInfo.IsReadOnly = msftInfo.IsReadOnly;
                        }
                        else
                        {
                            diskInfo.PartitionStyle = DetermineDiskPartitionStyle(index);
                            diskInfo.HealthStatus = "Healthy";
                        }

                        diskInfo.DiskType = DetermineDiskType(diskInfo);
                        diskInfo.IsVirtualDisk = IsVirtualDisk(diskInfo);

                        diskInfo.PartitionInfos = GetPartitionsForDisk(index);
                        AddUnallocatedSpace(diskInfo);

                        diskInfo.UsedSpace = (ulong)diskInfo.PartitionInfos
                            .Where(p => !p.IsUnallocated)
                            .Sum(p => (long)(p.TotalSize > 0 ? p.TotalSize : p.Size));

                        DiagnosticLogger.LogDebug(
                            $"Disk {index}: {diskInfo.Model}, {diskInfo.FormattedSize}, " +
                            $"{diskInfo.PartitionStyle}, {diskInfo.PartitionInfos.Count} partitions");
                        disks.Add(diskInfo);
                    }
                }
            }
            catch (Exception ex)
            {
                LogError(nameof(GetPhysicalDisks), ex);
            }

            DiagnosticLogger.LogInfo($"Enumerated {disks.Count} physical disk(s).");
            return disks.OrderBy(d => d.Index).ToList();
        }

        /// <summary>
        /// Calculate and insert unallocated space entries between/after partitions.
        /// </summary>
        private void AddUnallocatedSpace(PhysicalDiskInfo disk)
        {
            if (disk.Size == 0) return;

            const ulong ALIGNMENT = DiskManagementConstants.ALIGNMENT_1MB;
            const ulong MIN_GAP = DiskManagementConstants.MIN_UNALLOCATED_GAP;
            const ulong GPT_RESERVE = DiskManagementConstants.GPT_BACKUP_RESERVE;

            var newPartitionList = new List<PartitionInfo>();
            var sortedPartitions = disk.PartitionInfos.OrderBy(p => p.StartingOffset).ToList();

            var usableDiskSize = disk.Size;
            if (disk.PartitionStyle == "GPT" && disk.Size > GPT_RESERVE)
                usableDiskSize -= GPT_RESERVE;

            ulong AlignUp(ulong offset) =>
                offset == 0 ? ALIGNMENT : ((offset + ALIGNMENT - 1) / ALIGNMENT) * ALIGNMENT;

            ulong currentOffset = ALIGNMENT;

            foreach (var partition in sortedPartitions)
            {
                if (partition.StartingOffset > currentOffset)
                {
                    var alignedOffset = AlignUp(currentOffset);
                    if (partition.StartingOffset > alignedOffset)
                    {
                        var adjustedSize = partition.StartingOffset - alignedOffset;
                        if (adjustedSize > MIN_GAP)
                        {
                            newPartitionList.Add(CreateUnallocatedEntry(
                                disk.Index, alignedOffset, adjustedSize, "Gap"));
                        }
                    }
                }

                newPartitionList.Add(partition);

                ulong partSize = partition.TotalSize > 0 ? partition.TotalSize : partition.Size;
                if (partSize == 0) partSize = partition.Size;
                currentOffset = partition.StartingOffset + partSize;
            }

            // Trailing unallocated space
            if (sortedPartitions.Count == 0 && usableDiskSize > MIN_GAP)
            {
                var availableSize = usableDiskSize > ALIGNMENT ? usableDiskSize - ALIGNMENT : 0;
                if (availableSize > MIN_GAP)
                    newPartitionList.Add(CreateUnallocatedEntry(disk.Index, ALIGNMENT, availableSize, "All"));
            }
            else if (currentOffset < usableDiskSize)
            {
                var alignedOffset = AlignUp(currentOffset);
                if (alignedOffset < usableDiskSize)
                {
                    var remaining = usableDiskSize - alignedOffset;
                    if (remaining > MIN_GAP)
                        newPartitionList.Add(CreateUnallocatedEntry(disk.Index, alignedOffset, remaining, "End"));
                }
            }

            disk.PartitionInfos = newPartitionList;
        }

        private static PartitionInfo CreateUnallocatedEntry(uint diskIndex, ulong offset, ulong size, string suffix)
        {
            return new PartitionInfo
            {
                DeviceId = $"Unallocated-{diskIndex}-{suffix}",
                Name = "Unallocated Space",
                Size = size,
                TotalSize = size,
                FreeSpace = size,
                Type = "Unallocated",
                StartingOffset = offset,
                DiskIndex = diskIndex
            };
        }

        #endregion

        #region MSFT_Disk Cache

        private class MsftDiskData
        {
            public string PartitionStyle { get; set; } = "Unknown";
            public string HealthStatus { get; set; } = "Unknown";
            public bool IsOffline { get; set; }
            public bool IsReadOnly { get; set; }
        }

        private Dictionary<uint, MsftDiskData> GetMsftDiskInfo()
        {
            var result = new Dictionary<uint, MsftDiskData>();

            try
            {
                var scope = new ManagementScope(DiskManagementConstants.StorageWmiScope);
                scope.Connect();

                using var searcher = new ManagementObjectSearcher(scope,
                    new ObjectQuery("SELECT * FROM MSFT_Disk"));

                foreach (ManagementObject disk in searcher.GetAndDispose())
                {
                    using (disk)
                    {
                        try
                        {
                            var number = GetWmiPropertySafe<uint>(disk, "Number");
                            var partStyle = GetWmiPropertySafe<ushort>(disk, "PartitionStyle");
                            var health = GetWmiPropertySafe<ushort>(disk, "HealthStatus");

                            result[number] = new MsftDiskData
                            {
                                PartitionStyle = partStyle switch { 1 => "MBR", 2 => "GPT", _ => "RAW" },
                                HealthStatus = health switch { 0 => "Healthy", 1 => "Warning", 2 => "Unhealthy", _ => "Unknown" },
                                IsOffline = GetWmiPropertySafe<bool>(disk, "IsOffline"),
                                IsReadOnly = GetWmiPropertySafe<bool>(disk, "IsReadOnly")
                            };
                        }
                        catch (Exception ex)
                        {
                            DiagnosticLogger.LogDebug($"Error parsing MSFT_Disk entry: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogError(nameof(GetMsftDiskInfo), ex);
            }

            return result;
        }

        #endregion

        #region GPT Partition Type Lookup

        private static string GetPartitionTypeFromGuid(Guid gptGuid)
        {
            if (gptGuid == DiskManagementConstants.EFI_SYSTEM_PARTITION_GUID) return "System";
            if (gptGuid == DiskManagementConstants.MICROSOFT_RESERVED_GUID) return "Reserved";
            if (gptGuid == DiskManagementConstants.BASIC_DATA_PARTITION_GUID) return "Basic";
            if (gptGuid == DiskManagementConstants.WINDOWS_RECOVERY_GUID) return "Recovery";
            if (gptGuid == DiskManagementConstants.WINDOWS_RE_GUID) return "Recovery";
            if (gptGuid == DiskManagementConstants.OEM_RECOVERY_GUID) return "Recovery";
            if (gptGuid == DiskManagementConstants.LDM_METADATA_GUID) return "LDM Metadata";
            if (gptGuid == DiskManagementConstants.LDM_DATA_GUID) return "LDM Data";
            if (gptGuid == DiskManagementConstants.BIOS_BOOT_PARTITION_GUID) return "BIOS Boot";
            return "Unknown";
        }

        #endregion

        #region Partition Enumeration

        /// <summary>
        /// Get partitions for a disk ??primary path uses MSFT_Partition,
        /// falls back to Win32_DiskPartition on failure.
        /// </summary>
        internal List<PartitionInfo> GetPartitionsForDisk(uint diskIndex)
        {
            DiagnosticLogger.LogDebug($"Querying partitions for Disk {diskIndex}.");
            var partitions = new List<PartitionInfo>();

            try
            {
                var scope = new ManagementScope(DiskManagementConstants.StorageWmiScope);
                scope.Connect();

                var query = new ObjectQuery($"SELECT * FROM MSFT_Partition WHERE DiskNumber = {diskIndex}");
                using var searcher = new ManagementObjectSearcher(scope, query);

                foreach (ManagementObject partition in searcher.GetAndDispose())
                {
                    using (partition)
                    {
                        try
                        {
                            var partitionNumber = GetWmiPropertySafe<uint>(partition, "PartitionNumber");
                            var gptTypeStr = GetWmiPropertySafe<string>(partition, "GptType") ?? "";
                            var offset = GetWmiPropertySafe<ulong>(partition, "Offset");
                            var size = GetWmiPropertySafe<ulong>(partition, "Size");
                            var driveLetter = GetWmiPropertySafe<char>(partition, "DriveLetter");
                            var isBoot = GetWmiPropertySafe<bool>(partition, "IsBoot");
                            var isSystem = GetWmiPropertySafe<bool>(partition, "IsSystem");

                            Guid gptGuid = Guid.Empty;
                            string typeStr = "Unknown";
                            if (!string.IsNullOrEmpty(gptTypeStr) && Guid.TryParse(gptTypeStr, out gptGuid))
                            {
                                typeStr = GetPartitionTypeFromGuid(gptGuid);
                            }

                            var partInfo = new PartitionInfo
                            {
                                DeviceId = $"Disk #{diskIndex}, Partition #{partitionNumber}",
                                Name = $"Partition {partitionNumber}",
                                Size = size,
                                Index = partitionNumber - 1, // MSFT uses 1-based
                                Type = typeStr,
                                StartingOffset = offset,
                                DiskIndex = diskIndex,
                                IsBoot = isBoot,
                                IsSystem = isSystem,
                                GptPartitionTypeGuid = gptGuid
                            };

                            if (driveLetter != '\0' && driveLetter != ' ')
                            {
                                partInfo.DriveLetter = $"{driveLetter}:";
                                FillVolumeInfo(partInfo, partInfo.DriveLetter);
                            }

                            partitions.Add(partInfo);
                            DiagnosticLogger.LogDebug(
                                $"  Partition {partitionNumber}: {typeStr}, {FormatSize(size)}, " +
                                $"Drive={partInfo.DriveLetter}");
                        }
                        catch (Exception ex)
                        {
                            DiagnosticLogger.LogDebug($"Error parsing partition entry: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticLogger.LogWarning(
                    $"MSFT_Partition query failed for Disk {diskIndex}, falling back to Win32. Error: {ex.Message}");
                return GetPartitionsForDiskFallback(diskIndex);
            }

            return partitions.OrderBy(p => p.StartingOffset).ToList();
        }

        /// <summary>
        /// Fallback partition enumeration using Win32_DiskPartition.
        /// </summary>
        private List<PartitionInfo> GetPartitionsForDiskFallback(uint diskIndex)
        {
            var partitions = new List<PartitionInfo>();

            try
            {
                using var searcher = new ManagementObjectSearcher(
                    DiskManagementConstants.CimV2WmiScope,
                    $"SELECT * FROM Win32_DiskPartition WHERE DiskIndex = {diskIndex}");

                foreach (ManagementObject partition in searcher.GetAndDispose())
                {
                    using (partition)
                    {
                        var partInfo = new PartitionInfo
                        {
                            DeviceId = SafeString(partition["DeviceID"]),
                            Name = SafeString(partition["Name"]),
                            Size = SafeULong(partition["Size"]),
                            Index = SafeUInt(partition["Index"]),
                            Type = SafeString(partition["Type"]),
                            Bootable = SafeBool(partition["Bootable"]),
                            PrimaryPartition = SafeBool(partition["PrimaryPartition"]),
                            StartingOffset = SafeULong(partition["StartingOffset"]),
                            BlockSize = SafeULong(partition["BlockSize"]),
                            NumberOfBlocks = SafeULong(partition["NumberOfBlocks"]),
                            DiskIndex = diskIndex
                        };
                        partitions.Add(partInfo);
                    }
                }

                // Fill volume information via association
                var volumeMap = BuildVolumeToPartitionMap();
                foreach (var partition in partitions)
                {
                    if (volumeMap.TryGetValue(partition.DeviceId, out var volumeInfo))
                    {
                        partition.DriveLetter = volumeInfo.DriveLetter;
                        partition.VolumeLabel = volumeInfo.Label;
                        partition.FileSystem = volumeInfo.FileSystem;
                        partition.FreeSpace = volumeInfo.FreeSpace;
                        partition.TotalSize = volumeInfo.Capacity;
                        partition.DriveType = volumeInfo.DriveType;
                    }
                }
            }
            catch (Exception ex)
            {
                LogError(nameof(GetPartitionsForDiskFallback), ex, diskIndex: diskIndex);
            }

            return partitions.OrderBy(p => p.StartingOffset).ToList();
        }

        private void FillVolumeInfo(PartitionInfo partInfo, string driveLetter)
        {
            try
            {
                var letter = driveLetter.TrimEnd(':');
                if (string.IsNullOrEmpty(letter)) return;

                var driveInfo = new DriveInfo(letter);
                if (driveInfo.IsReady)
                {
                    partInfo.VolumeLabel = driveInfo.VolumeLabel ?? "";
                    partInfo.FileSystem = driveInfo.DriveFormat ?? "";
                    partInfo.TotalSize = (ulong)driveInfo.TotalSize;
                    partInfo.FreeSpace = (ulong)driveInfo.TotalFreeSpace;
                    partInfo.DriveType = (uint)driveInfo.DriveType;
                }
            }
            catch (Exception ex)
            {
                DiagnosticLogger.LogDebug($"FillVolumeInfo({driveLetter}): {ex.Message}");
            }
        }

        #endregion

        #region Volume / CDROM / StoragePool Enumeration

        public List<VolumeInfo> GetVolumes()
        {
            var volumes = new List<VolumeInfo>();

            try
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    try
                    {
                        var volumeInfo = new VolumeInfo
                        {
                            DeviceId = drive.Name.TrimEnd('\\'),
                            DriveLetter = drive.Name.TrimEnd('\\'),
                            DriveType = (uint)drive.DriveType,
                            DriveTypeDescription = drive.DriveType.ToString(),
                            BootVolume = drive.Name.StartsWith("C", StringComparison.OrdinalIgnoreCase),
                            SystemVolume = drive.Name.StartsWith("C", StringComparison.OrdinalIgnoreCase)
                        };

                        if (drive.IsReady)
                        {
                            volumeInfo.Label = drive.VolumeLabel ?? "";
                            volumeInfo.FileSystem = drive.DriveFormat ?? "";
                            volumeInfo.Capacity = (ulong)drive.TotalSize;
                            volumeInfo.FreeSpace = (ulong)drive.TotalFreeSpace;
                        }

                        volumes.Add(volumeInfo);
                    }
                    catch (Exception ex)
                    {
                        DiagnosticLogger.LogDebug($"Volume info error for {drive.Name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogError(nameof(GetVolumes), ex);
            }

            return volumes.OrderBy(v => v.DriveLetter).ToList();
        }

        public List<CDROMInfo> GetCDROMDrives()
        {
            var drives = new List<CDROMInfo>();

            try
            {
                foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.CDRom))
                {
                    try
                    {
                        drives.Add(new CDROMInfo
                        {
                            DeviceId = drive.Name.TrimEnd('\\'),
                            Name = $"CD-ROM Drive ({drive.Name.TrimEnd('\\')})",
                            Caption = $"CD-ROM Drive ({drive.Name.TrimEnd('\\')})",
                            Drive = drive.Name.TrimEnd('\\'),
                            MediaLoaded = drive.IsReady,
                            VolumeName = drive.IsReady ? (drive.VolumeLabel ?? "") : "",
                            Size = drive.IsReady ? (ulong)drive.TotalSize : 0,
                            Status = "OK"
                        });
                    }
                    catch { /* Non-ready drives may throw */ }
                }

                // Enrich with WMI data (manufacturer, media type, etc.)
                try
                {
                    using var searcher = new ManagementObjectSearcher(
                        DiskManagementConstants.CimV2WmiScope, "SELECT * FROM Win32_CDROMDrive");
                    foreach (ManagementObject cdrom in searcher.GetAndDispose())
                    {
                        using (cdrom)
                        {
                            var driveLetter = SafeString(cdrom["Drive"]);
                            var existing = drives.FirstOrDefault(
                                d => d.Drive.Equals(driveLetter, StringComparison.OrdinalIgnoreCase));

                            if (existing != null)
                            {
                                existing.DeviceId = SafeString(cdrom["DeviceID"]);
                                existing.Name = SafeString(cdrom["Name"]);
                                existing.Caption = SafeString(cdrom["Caption"]);
                                existing.Manufacturer = SafeString(cdrom["Manufacturer"]);
                                existing.MediaType = SafeString(cdrom["MediaType"]);
                            }
                        }
                    }
                }
                catch { /* WMI enrichment is optional */ }
            }
            catch (Exception ex)
            {
                LogError(nameof(GetCDROMDrives), ex);
            }

            return drives;
        }

        public List<StoragePoolInfo> GetStoragePools()
        {
            var pools = new List<StoragePoolInfo>();

            try
            {
                var scope = new ManagementScope(DiskManagementConstants.StorageWmiScope);
                scope.Connect();

                using var searcher = new ManagementObjectSearcher(scope,
                    new ObjectQuery("SELECT * FROM MSFT_StoragePool WHERE IsPrimordial = FALSE"));

                foreach (ManagementObject pool in searcher.GetAndDispose())
                {
                    using (pool)
                    {
                        pools.Add(new StoragePoolInfo
                        {
                            FriendlyName = GetWmiPropertySafe<string>(pool, "FriendlyName") ?? "",
                            HealthStatus = GetWmiPropertySafe<ushort>(pool, "HealthStatus"),
                            OperationalStatus = GetWmiPropertySafe<ushort>(pool, "OperationalStatus"),
                            Size = GetWmiPropertySafe<ulong>(pool, "Size"),
                            AllocatedSize = GetWmiPropertySafe<ulong>(pool, "AllocatedSize"),
                            IsReadOnly = GetWmiPropertySafe<bool>(pool, "IsReadOnly")
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                LogError(nameof(GetStoragePools), ex);
            }

            return pools;
        }

        #endregion

        #region Internal Helpers ??Volume / Disk Type Detection

        private Dictionary<string, VolumeInfo> BuildVolumeToPartitionMap()
        {
            var map = new Dictionary<string, VolumeInfo>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using var assocSearcher = new ManagementObjectSearcher(
                    DiskManagementConstants.CimV2WmiScope,
                    "SELECT * FROM Win32_LogicalDiskToPartition");

                foreach (ManagementObject assoc in assocSearcher.GetAndDispose())
                {
                    using (assoc)
                    {
                        try
                        {
                            var partitionId = ExtractDeviceIdFromPath(SafeString(assoc["Antecedent"]));
                            var driveId = ExtractDeviceIdFromPath(SafeString(assoc["Dependent"]));

                            if (!string.IsNullOrEmpty(partitionId) && !string.IsNullOrEmpty(driveId))
                            {
                                var volumeInfo = GetVolumeInfoByDriveLetter(driveId);
                                if (volumeInfo != null)
                                    map[partitionId] = volumeInfo;
                            }
                        }
                        catch { /* Skip malformed association entries */ }
                    }
                }
            }
            catch { /* Association query is best-effort */ }

            return map;
        }

        private static string ExtractDeviceIdFromPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            try
            {
                var startIndex = path.IndexOf("DeviceID=\"", StringComparison.OrdinalIgnoreCase);
                if (startIndex >= 0)
                {
                    startIndex += 10;
                    var endIndex = path.IndexOf("\"", startIndex);
                    if (endIndex > startIndex)
                        return path.Substring(startIndex, endIndex - startIndex);
                }
            }
            catch { }
            return string.Empty;
        }

        private static VolumeInfo? GetVolumeInfoByDriveLetter(string driveLetter)
        {
            try
            {
                if (string.IsNullOrEmpty(driveLetter)) return null;
                var letter = driveLetter.TrimEnd(':');
                if (letter.Length != 1) return null;

                var driveInfo = new DriveInfo(letter);
                return new VolumeInfo
                {
                    DriveLetter = letter + ":",
                    Label = driveInfo.IsReady ? (driveInfo.VolumeLabel ?? "") : "",
                    FileSystem = driveInfo.IsReady ? (driveInfo.DriveFormat ?? "") : "",
                    Capacity = driveInfo.IsReady ? (ulong)driveInfo.TotalSize : 0,
                    FreeSpace = driveInfo.IsReady ? (ulong)driveInfo.TotalFreeSpace : 0,
                    DriveType = (uint)driveInfo.DriveType,
                    DriveTypeDescription = driveInfo.DriveType.ToString()
                };
            }
            catch { return null; }
        }

        private static string DetermineDiskType(PhysicalDiskInfo disk)
        {
            var iface = disk.InterfaceType?.ToUpperInvariant() ?? "";
            var model = disk.Model?.ToUpperInvariant() ?? "";
            var media = disk.MediaType?.ToUpperInvariant() ?? "";

            if (iface.Contains("NVME") || model.Contains("NVME")) return "NVMe SSD";
            if (media.Contains("SSD") || model.Contains("SSD")) return "SSD";
            if (model.Contains("VIRTUAL") || model.Contains("VHDX") || model.Contains("MSFT")) return "Virtual Disk";
            if (iface.Contains("USB")) return "USB Drive";
            return "HDD";
        }

        private static bool IsVirtualDisk(PhysicalDiskInfo disk)
        {
            var model = disk.Model?.ToUpperInvariant() ?? "";
            return model.Contains("VIRTUAL") || model.Contains("VHDX") || model.Contains("MSFT");
        }

        private string DetermineDiskPartitionStyle(uint diskIndex)
        {
            try
            {
                using var partSearcher = new ManagementObjectSearcher(
                    DiskManagementConstants.CimV2WmiScope,
                    $"SELECT Type FROM Win32_DiskPartition WHERE DiskIndex = {diskIndex}");

                foreach (ManagementObject partition in partSearcher.GetAndDispose())
                {
                    using (partition)
                    {
                        var type = SafeString(partition["Type"]).ToUpperInvariant();
                        if (type.Contains("GPT")) return "GPT";
                        if (type.Contains("MBR") || type.Contains("PRIMARY") ||
                            type.Contains("EXTENDED") || type.Contains("LOGICAL") ||
                            type.Contains("INSTALLABLE")) return "MBR";
                    }
                }

                using var diskSearcher = new ManagementObjectSearcher(
                    DiskManagementConstants.CimV2WmiScope,
                    $"SELECT Partitions FROM Win32_DiskDrive WHERE Index = {diskIndex}");

                foreach (ManagementObject disk in diskSearcher.GetAndDispose())
                {
                    using (disk)
                    {
                        if (SafeUInt(disk["Partitions"]) == 0) return "RAW";
                    }
                }
            }
            catch { }

            return "Unknown";
        }

        public string GetDiskPartitionStyle(uint diskIndex) => DetermineDiskPartitionStyle(diskIndex);

        private static void LogError(string operation, Exception ex,
            uint? diskIndex = null, uint? partitionIndex = null, string? driveLetter = null)
        {
            DiagnosticLogger.LogOperationError(operation, ex, diskIndex, partitionIndex, driveLetter);
        }

        private static string FormatSize(ulong bytes) => FormatHelper.FormatSize(bytes);

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
                {
                    var str = value.ToString();
                    return (T)(object)(!string.IsNullOrEmpty(str) ? str[0] : '\0');
                }

                return (T)Convert.ChangeType(value, underlyingType);
            }
            catch
            {
                return defaultValue;
            }
        }

        private static string SafeString(object? value, string defaultValue = "")
        {
            return value?.ToString()?.Trim() ?? defaultValue;
        }

        private static uint SafeUInt(object? value, uint defaultValue = 0)
        {
            try { return value == null ? defaultValue : Convert.ToUInt32(value); }
            catch { return defaultValue; }
        }

        private static ulong SafeULong(object? value, ulong defaultValue = 0)
        {
            try { return value == null ? defaultValue : Convert.ToUInt64(value); }
            catch { return defaultValue; }
        }

        private static bool SafeBool(object? value, bool defaultValue = false)
        {
            try { return value == null ? defaultValue : Convert.ToBoolean(value); }
            catch { return defaultValue; }
        }

        #endregion
    }
}



