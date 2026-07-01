using System;
using System.Diagnostics;
using System.IO;
using System.Management;
using OneMMC.Core.Features.PCManagement.Models.DiskMgmt;
using OneMMC.Core.Features.PCManagement.Services.DiskMgmt.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace OneMMC.Core.Features.PCManagement.Services.DiskMgmt
{
    /// <summary>
    /// Disk Management Service â€” Uses Windows Storage Management API (MSFT_*)
    /// Modular split:
    ///   Manager        â€” P/Invoke, native types, shared helpers, misc operations
    ///   Queries        â€” Query operations (disks, partitions, volumes, CDROM, pools)
    ///   Operations     â€” Disk/volume/VHD/CD-ROM operations
    ///   Infrastructure â€” System protection, validation, WMI helpers
    /// </summary>
    public class DiskManagementService
    {
        private readonly ILogger<DiskManagementService> _logger;
        private readonly DiskOperations _diskOperations;
        private readonly DiskInformationQueries _diskInformationQueries;
        private readonly DiskManagementInfrastructure _diskManagementInfrastructure;
        private readonly VolumeOperations _volumeOperations;

        public DiskManagementService()
            : this(NullLogger<DiskManagementService>.Instance)
        {
        }

        public DiskManagementService(ILogger<DiskManagementService> logger)
        {
            _logger = logger;
            DiagnosticLogger.ConfigureLogger(logger);
            _diskInformationQueries = new DiskInformationQueries(this);
            _diskManagementInfrastructure = new DiskManagementInfrastructure(_diskInformationQueries);
            _diskOperations = new DiskOperations(this);
            _volumeOperations = new VolumeOperations(this);
        }

        #region Shared Instance Helpers â€” Logging Wrappers

        /// <summary>Log debug message with optional context.</summary>
        private static void LogDebug(string operation, string message,
            uint? diskIndex = null, uint? partitionIndex = null, string? driveLetter = null)
        {
            DiagnosticLogger.LogDebug($"[{operation}] {message}", diskIndex, partitionIndex, driveLetter);
        }

        /// <summary>Log error with exception context.</summary>
        private static void LogError(string operation, Exception ex,
            uint? diskIndex = null, uint? partitionIndex = null, string? driveLetter = null)
        {
            DiagnosticLogger.LogOperationError(operation, ex, diskIndex, partitionIndex, driveLetter);
        }

        /// <summary>Format bytes to human-readable size string.</summary>
        private static string FormatSize(ulong bytes) => FormatHelper.FormatSize(bytes);

        #endregion

        #region Misc Operations (formerly Other.cs)

        /// <summary>
        /// Open the built-in Disk Management console (diskmgmt.msc).
        /// </summary>
        public bool OpenDiskManagementConsole()
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = "diskmgmt.msc", UseShellExecute = true });
                DiagnosticLogger.LogInfo("Opened Disk Management console.");
                return true;
            }
            catch (Exception ex)
            {
                LogError(nameof(OpenDiskManagementConsole), ex);
                return false;
            }
        }

        /// <summary>
        /// Rescan disks by reconnecting to WMI and refreshing disk enumeration.
        /// </summary>
        public void RescanDisks()
        {
            try
            {
                var scope = new ManagementScope(DiskManagementConstants.StorageWmiScope);
                scope.Connect();
                DiagnosticLogger.LogInfo("Disk rescan completed â€” WMI scope reconnected.");
            }
            catch (Exception ex)
            {
                LogError(nameof(RescanDisks), ex);
            }
        }

        /// <summary>
        /// Get volume properties for the specified drive letter.
        /// </summary>
        public VolumeProperties? GetVolumeProperties(string driveLetter)
        {
            try
            {
                var letter = driveLetter?.Replace(":", "").ToUpper();
                if (string.IsNullOrEmpty(letter) || letter.Length != 1) return null;

                var driveInfo = new DriveInfo(letter);
                if (!driveInfo.IsReady) return null;

                return new VolumeProperties
                {
                    DeviceId = letter + ":",
                    DriveLetter = letter + ":",
                    Label = driveInfo.VolumeLabel ?? "",
                    FileSystem = driveInfo.DriveFormat ?? "",
                    Capacity = (ulong)driveInfo.TotalSize,
                    FreeSpace = (ulong)driveInfo.TotalFreeSpace,
                    DriveType = (uint)driveInfo.DriveType
                };
            }
            catch (Exception ex)
            {
                LogError(nameof(GetVolumeProperties), ex, driveLetter: driveLetter);
                return null;
            }
        }

        public OperationResult InitializeDisk(uint diskIndex, bool useGPT = true)
        {
            return _diskOperations.InitializeDisk(diskIndex, useGPT);
        }

        public OperationResult SetDiskOnlineOffline(uint diskIndex, bool online)
        {
            return _diskOperations.SetDiskOnlineOffline(diskIndex, online);
        }

        public bool IsDiskOnline(uint diskIndex)
        {
            return _diskOperations.IsDiskOnline(diskIndex);
        }

        public OperationResult SetDiskReadOnly(uint diskIndex, bool readOnly)
        {
            return _diskOperations.SetDiskReadOnly(diskIndex, readOnly);
        }

        public bool IsDiskReadOnly(uint diskIndex)
        {
            return _diskOperations.IsDiskReadOnly(diskIndex);
        }

        public OperationResult CleanDisk(uint diskIndex)
        {
            return _diskOperations.CleanDisk(diskIndex);
        }

        public bool DiskNeedsInitialization(uint diskIndex)
        {
            return _diskOperations.DiskNeedsInitialization(diskIndex);
        }

        public uint? GetSystemDiskIndex()
        {
            return _diskManagementInfrastructure.GetSystemDiskIndex();
        }

        public bool IsSystemDisk(uint diskIndex)
        {
            return _diskManagementInfrastructure.IsSystemDisk(diskIndex);
        }

        public bool IsSystemPartition(uint diskIndex, uint partitionIndex)
        {
            return _diskManagementInfrastructure.IsSystemPartition(diskIndex, partitionIndex);
        }

        public static bool IsSystemDriveLetter(string driveLetter)
        {
            return DiskManagementInfrastructure.IsSystemDriveLetter(driveLetter);
        }

        public bool DiskContainsCriticalPartitions(uint diskIndex)
        {
            return _diskManagementInfrastructure.DiskContainsCriticalPartitions(diskIndex);
        }

        public string? ValidateDiskOperationSafety(uint diskIndex, bool allowSystemDiskWithWarning = false)
        {
            return _diskManagementInfrastructure.ValidateDiskOperationSafety(diskIndex, allowSystemDiskWithWarning);
        }

        public string? ValidatePartitionOperationSafety(uint diskIndex, uint partitionIndex)
        {
            return _diskManagementInfrastructure.ValidatePartitionOperationSafety(diskIndex, partitionIndex);
        }

        public static bool IsDriveLetterInUse(string driveLetter)
        {
            return DiskManagementInfrastructure.IsDriveLetterInUse(driveLetter);
        }

        public System.Collections.Generic.List<PhysicalDiskInfo> GetPhysicalDisks()
        {
            return _diskInformationQueries.GetPhysicalDisks();
        }

        public System.Collections.Generic.List<VolumeInfo> GetVolumes()
        {
            return _diskInformationQueries.GetVolumes();
        }

        public System.Collections.Generic.List<CDROMInfo> GetCDROMDrives()
        {
            return _diskInformationQueries.GetCDROMDrives();
        }

        public System.Collections.Generic.List<StoragePoolInfo> GetStoragePools()
        {
            return _diskInformationQueries.GetStoragePools();
        }

        public string GetDiskPartitionStyle(uint diskIndex)
        {
            return _diskInformationQueries.GetDiskPartitionStyle(diskIndex);
        }

        public OperationResult CreateSimpleVolume(
            uint diskIndex,
            ulong sizeInMB = 0,
            string? driveLetter = null,
            string fileSystem = "NTFS",
            string label = "",
            bool quickFormat = true,
            ulong? offset = null)
        {
            return _volumeOperations.CreateSimpleVolume(diskIndex, sizeInMB, driveLetter, fileSystem, label, quickFormat, offset);
        }

        public OperationResult DeleteVolume(uint diskIndex, uint partitionIndex)
        {
            return _volumeOperations.DeleteVolume(diskIndex, partitionIndex);
        }

        public OperationResult FormatVolume(
            string driveLetter,
            string fileSystem = "NTFS",
            string label = "",
            bool quickFormat = true)
        {
            return _volumeOperations.FormatVolume(driveLetter, fileSystem, label, quickFormat);
        }

        public OperationResult ExtendVolume(string driveLetter, ulong sizeInMB = 0)
        {
            return _volumeOperations.ExtendVolume(driveLetter, sizeInMB);
        }

        public OperationResult ShrinkVolume(string driveLetter, ulong sizeInMB)
        {
            return _volumeOperations.ShrinkVolume(driveLetter, sizeInMB);
        }

        public QueryResult<ulong> QueryShrinkableSpace(string driveLetter)
        {
            return _volumeOperations.QueryShrinkableSpace(driveLetter);
        }

        public QueryResult<ulong> QueryExtendableSpace(string driveLetter)
        {
            return _volumeOperations.QueryExtendableSpace(driveLetter);
        }

        public QueryResult<ulong> QueryShrinkableSpaceByIndex(uint diskIndex, uint partitionIndex)
        {
            return _volumeOperations.QueryShrinkableSpaceByIndex(diskIndex, partitionIndex);
        }

        public QueryResult<ulong> QueryExtendableSpaceByIndex(uint diskIndex, uint partitionIndex)
        {
            return _volumeOperations.QueryExtendableSpaceByIndex(diskIndex, partitionIndex);
        }

        public OperationResult MarkPartitionActive(uint diskIndex, uint partitionIndex)
        {
            return _volumeOperations.MarkPartitionActive(diskIndex, partitionIndex);
        }

        public OperationResult ChangeDriveLetter(string currentDriveLetter, string newDriveLetter)
        {
            return _volumeOperations.ChangeDriveLetter(currentDriveLetter, newDriveLetter);
        }

        public OperationResult AssignDriveLetter(uint diskIndex, uint partitionIndex, string driveLetter)
        {
            return _volumeOperations.AssignDriveLetter(diskIndex, partitionIndex, driveLetter);
        }

        public OperationResult RemoveDriveLetter(string driveLetter)
        {
            return _volumeOperations.RemoveDriveLetter(driveLetter);
        }

        public OperationResult RemoveDriveLetterByIndex(uint diskIndex, uint partitionIndex)
        {
            return _volumeOperations.RemoveDriveLetterByIndex(diskIndex, partitionIndex);
        }

        public OperationResult MountVolumeToFolder(string driveLetter, string folderPath)
        {
            return _volumeOperations.MountVolumeToFolder(driveLetter, folderPath);
        }

        public System.Collections.Generic.List<char> GetAvailableDriveLetters()
        {
            return _volumeOperations.GetAvailableDriveLetters();
        }

        public System.Collections.Generic.List<UnallocatedSpace> GetUnallocatedSpace(uint diskIndex)
        {
            return _volumeOperations.GetUnallocatedSpace(diskIndex);
        }

        public OperationResult EjectCDROM(string driveLetter)
        {
            return CdromOperations.Eject(driveLetter, LogDebug, LogError);
        }

        public OperationResult ChangeCDROMDriveLetter(string currentDriveLetter, string newDriveLetter)
        {
            return CdromOperations.ChangeDriveLetter(currentDriveLetter, newDriveLetter, LogDebug, LogError);
        }

        public OperationResult RemoveCDROMDriveLetter(string driveLetter)
        {
            return CdromOperations.RemoveDriveLetter(driveLetter, LogDebug, LogError);
        }

        public OperationResult AssignCDROMDriveLetter(string currentDriveLetter, string newDriveLetter)
        {
            return CdromOperations.AssignDriveLetter(currentDriveLetter, newDriveLetter, LogDebug, LogError);
        }


        public OperationResult LoadCDROM(string driveLetter)
        {
            return CdromOperations.Load(driveLetter, LogDebug, LogError);
        }

        public OperationResult CreateVHD(string path, ulong sizeInBytes, bool isVhdx = true, bool isDynamic = true)
        {
            return VirtualDiskOperations.Create(path, sizeInBytes, isVhdx, isDynamic, LogDebug, LogError);
        }

        public OperationResult AttachVHD(string path, bool readOnly = false)
        {
            return VirtualDiskOperations.Attach(path, readOnly, LogDebug, LogError);
        }

        public OperationResult DetachVHD(string path)
        {
            return VirtualDiskOperations.Detach(path, LogDebug, LogError);
        }

        #endregion
    }
}


