using System;
using System.Collections.Generic;
using System.Diagnostics;
using Debug = System.Diagnostics.Trace;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using OneMMC.Core.Features.PCManagement.Services.DiskMgmt.Common;
using OneMMC.Core.Features.PCManagement.Models.DiskMgmt;
using OneMMC.Core.Infrastructure.Wmi;
using WmiLight;
using Win32PInvoke = Windows.Win32.PInvoke;

namespace OneMMC.Core.Features.PCManagement.Services.DiskMgmt
{
    internal sealed class VolumeOperations
    {
        private readonly DiskManagementService _service;

        public VolumeOperations(DiskManagementService service)
        {
            _service = service;
        }

        #region Volume/Partition Operations

        /// <summary>
        /// Create simple volume ??Using MSFT_Disk.CreatePartition()
        /// </summary>
        public OperationResult CreateSimpleVolume(
            uint diskIndex,
            ulong sizeInMB = 0,
            string? driveLetter = null,
            string fileSystem = "NTFS",
            string label = "",
            bool quickFormat = true,
            ulong? offset = null)
        {
            return ExecuteWmiOperation(() =>
            {
                using var connection = CreateConnection();
                using var disk = GetDisk(connection, diskIndex);

                if (disk == null)
                    return OperationResult.Fail(ErrorMessages.DiskNotFound);

                var createResult = CreatePartitionOnDisk(disk, sizeInMB, driveLetter, offset);
                if (!createResult.Success)
                    return OperationResult.Fail(createResult.Message);

                // Wait for partition to be ready before formatting
                Thread.Sleep(DiskManagementConstants.PARTITION_CREATE_DELAY_MS);

                var formatResult = FormatNewPartition(
                    connection,
                    createResult.DiskNumber,
                    createResult.PartitionNumber,
                    fileSystem,
                    label,
                    quickFormat);

                if (!formatResult.Success)
                    return OperationResult.Partial(
                        $"Partition created, but formatting failed: {formatResult.Message}",
                        formatResult.ErrorCode);

                return OperationResult.Ok($"Successfully created {fileSystem} volume.");
            }, nameof(CreateSimpleVolume), diskIndex: diskIndex);
        }

        /// <summary>
        /// Delete volume ??Using MSFT_Partition.DeleteObject()
        /// </summary>
        public OperationResult DeleteVolume(uint diskIndex, uint partitionIndex)
        {
            var safetyCheck = _service.ValidatePartitionOperationSafety(diskIndex, partitionIndex);
            if (safetyCheck != null)
                return OperationResult.Fail(safetyCheck);

            return ExecuteWmiOperation(() =>
            {
                using var connection = CreateConnection();
                using var partition = GetPartition(connection, diskIndex, partitionIndex);

                if (partition == null)
                    return OperationResult.Fail(ErrorMessages.PartitionNotFound);

                using WmiMethod deleteMethod = partition.GetMethod("DeleteObject");
                var returnValue = partition.ExecuteMethod<uint>(deleteMethod, out WmiMethodParameters deleteOutParams);
                deleteOutParams?.Dispose();

                return returnValue == DiskManagementConstants.WMI_SUCCESS
                    ? OperationResult.Ok("Partition deleted successfully.")
                    : OperationResult.Fail(
                        $"Deletion failed. Error code: {returnValue} - {ErrorMessages.GetMsftErrorMessage(returnValue)}",
                        returnValue);
            }, nameof(DeleteVolume), diskIndex, partitionIndex);
        }

        /// <summary>
        /// Format volume ??Using MSFT_Volume.Format()
        /// </summary>
        public OperationResult FormatVolume(
            string driveLetter,
            string fileSystem = "NTFS",
            string label = "",
            bool quickFormat = true)
        {
            var validation = ValidateDriveLetter(driveLetter);
            if (!validation.IsValid)
                return OperationResult.Fail(validation.ErrorMessage ?? "Invalid drive letter");

            var normalizedLetter = NormalizeDriveLetter(driveLetter);

            if (DiskManagementService.IsSystemDriveLetter(normalizedLetter + ":"))
            {
                var systemDrive = GetSystemDriveLetter();
                return OperationResult.Fail(string.Format(ErrorMessages.SystemDriveFormat, systemDrive));
            }

            return ExecuteWmiOperation(() =>
            {
                using var connection = CreateConnection();
                using var volume = GetVolumeByDriveLetter(connection, normalizedLetter);

                if (volume == null)
                    return OperationResult.Fail($"Volume {normalizedLetter}: not found.");

                return FormatVolumeObject(volume, fileSystem, label, quickFormat);
            }, nameof(FormatVolume), driveLetter: normalizedLetter);
        }

        /// <summary>
        /// Extend volume ??Using MSFT_Partition.Resize()
        /// </summary>
        public OperationResult ExtendVolume(string driveLetter, ulong sizeInMB = 0)
        {
            var validation = ValidateDriveLetter(driveLetter);
            if (!validation.IsValid)
                return OperationResult.Fail(validation.ErrorMessage ?? "Invalid drive letter");

            var normalizedLetter = NormalizeDriveLetter(driveLetter);

            return ExecuteWmiOperation(() =>
            {
                using var connection = CreateConnection();
                using var partition = GetPartitionByDriveLetter(connection, normalizedLetter);

                if (partition == null)
                    return OperationResult.Fail($"Volume {normalizedLetter}: not found.");

                if (IsSpecialPartitionType(partition, out var reason))
                    return OperationResult.Fail(reason ?? ErrorMessages.SpecialPartitionOperation);

                return ResizePartitionExtend(partition, sizeInMB);
            }, nameof(ExtendVolume), driveLetter: normalizedLetter);
        }

        /// <summary>
        /// Shrink volume ??Using MSFT_Partition.Resize()
        /// </summary>
        public OperationResult ShrinkVolume(string driveLetter, ulong sizeInMB)
        {
            var validation = ValidateDriveLetter(driveLetter);
            if (!validation.IsValid)
                return OperationResult.Fail(validation.ErrorMessage ?? "Invalid drive letter");

            if (sizeInMB == 0)
                return OperationResult.Fail(ErrorMessages.SizeRequired);

            var normalizedLetter = NormalizeDriveLetter(driveLetter);

            return ExecuteWmiOperation(() =>
            {
                using var connection = CreateConnection();
                using var partition = GetPartitionByDriveLetter(connection, normalizedLetter);

                if (partition == null)
                    return OperationResult.Fail($"Volume {normalizedLetter}: not found.");

                if (IsSpecialPartitionType(partition, out var reason))
                    return OperationResult.Fail(reason ?? ErrorMessages.SpecialPartitionOperation);

                return ResizePartitionShrink(partition, sizeInMB);
            }, nameof(ShrinkVolume), driveLetter: normalizedLetter);
        }

        /// <summary>
        /// Query shrinkable space
        /// </summary>
        public QueryResult<ulong> QueryShrinkableSpace(string driveLetter)
        {
            var validation = ValidateDriveLetter(driveLetter);
            if (!validation.IsValid)
                return new QueryResult<ulong>(false, 0, validation.ErrorMessage ?? "Invalid drive letter");

            var normalizedLetter = NormalizeDriveLetter(driveLetter);

            return ExecuteWmiQuery(() =>
            {
                using var connection = CreateConnection();
                using var partition = GetPartitionByDriveLetter(connection, normalizedLetter);

                if (partition == null)
                    return new QueryResult<ulong>(false, 0, $"Volume {normalizedLetter}: not found.");

                if (IsSpecialPartitionType(partition, out var reason))
                    return new QueryResult<ulong>(false, 0, reason ?? ErrorMessages.SpecialPartitionOperation);

                var sizeResult = GetPartitionSupportedSize(partition);
                if (!sizeResult.Success)
                    return EstimateShrinkableSpace(normalizedLetter);

                var currentSize = partition.GetPropertySafe<ulong>("Size");

                // BUG-FIX: prevent underflow when SizeMin > currentSize
                if (sizeResult.SizeMin >= currentSize)
                    return new QueryResult<ulong>(true, 0, "No shrinkable space available.");

                var shrinkableMB = (currentSize - sizeResult.SizeMin) / DiskManagementConstants.BYTES_PER_MB;
                return new QueryResult<ulong>(true, shrinkableMB, $"Shrinkable space: {shrinkableMB} MB");
            }, nameof(QueryShrinkableSpace), driveLetter: normalizedLetter);
        }

        /// <summary>
        /// Query extendable space
        /// </summary>
        public QueryResult<ulong> QueryExtendableSpace(string driveLetter)
        {
            var validation = ValidateDriveLetter(driveLetter);
            if (!validation.IsValid)
                return new QueryResult<ulong>(false, 0, validation.ErrorMessage ?? "Invalid drive letter");

            var normalizedLetter = NormalizeDriveLetter(driveLetter);

            return ExecuteWmiQuery(() =>
            {
                using var connection = CreateConnection();
                using var partition = GetPartitionByDriveLetter(connection, normalizedLetter);

                if (partition == null)
                    return new QueryResult<ulong>(false, 0, $"Volume {normalizedLetter}: not found.");

                if (IsSpecialPartitionType(partition, out var reason))
                    return new QueryResult<ulong>(false, 0, reason ?? ErrorMessages.SpecialPartitionOperation);

                var sizeResult = GetPartitionSupportedSize(partition);
                if (!sizeResult.Success)
                    return new QueryResult<ulong>(true, 0,
                        "Unable to query extendable space. No unallocated space may be available.");

                var currentSize = partition.GetPropertySafe<ulong>("Size");

                if (sizeResult.SizeMax <= currentSize)
                    return new QueryResult<ulong>(true, 0, "No unallocated space available for extension.");

                var extendableMB = (sizeResult.SizeMax - currentSize) / DiskManagementConstants.BYTES_PER_MB;
                return new QueryResult<ulong>(true, extendableMB, $"Extendable space: {extendableMB} MB");
            }, nameof(QueryExtendableSpace), driveLetter: normalizedLetter);
        }

        /// <summary>
        /// Query shrinkable space by disk and partition index
        /// </summary>
        public QueryResult<ulong> QueryShrinkableSpaceByIndex(uint diskIndex, uint partitionIndex)
        {
            return ExecuteWmiQuery(() =>
            {
                using var connection = CreateConnection();
                using var partition = GetPartition(connection, diskIndex, partitionIndex);

                if (partition == null)
                    return new QueryResult<ulong>(false, 0, ErrorMessages.PartitionNotFound);

                if (IsSpecialPartitionType(partition, out var reason))
                    return new QueryResult<ulong>(false, 0, reason ?? ErrorMessages.SpecialPartitionOperation);

                var sizeResult = GetPartitionSupportedSize(partition);
                if (!sizeResult.Success)
                    return new QueryResult<ulong>(false, 0, sizeResult.Message);

                var currentSize = partition.GetPropertySafe<ulong>("Size");

                // BUG-FIX: prevent underflow
                if (sizeResult.SizeMin >= currentSize)
                    return new QueryResult<ulong>(true, 0, "No shrinkable space available.");

                var shrinkableMB = (currentSize - sizeResult.SizeMin) / DiskManagementConstants.BYTES_PER_MB;
                return new QueryResult<ulong>(true, shrinkableMB, $"Shrinkable space: {shrinkableMB} MB");
            }, nameof(QueryShrinkableSpaceByIndex), diskIndex, partitionIndex);
        }

        /// <summary>
        /// Query extendable space by disk and partition index
        /// </summary>
        public QueryResult<ulong> QueryExtendableSpaceByIndex(uint diskIndex, uint partitionIndex)
        {
            return ExecuteWmiQuery(() =>
            {
                using var connection = CreateConnection();
                using var partition = GetPartition(connection, diskIndex, partitionIndex);

                if (partition == null)
                    return new QueryResult<ulong>(false, 0, ErrorMessages.PartitionNotFound);

                if (IsSpecialPartitionType(partition, out var reason))
                    return new QueryResult<ulong>(false, 0, reason ?? ErrorMessages.SpecialPartitionOperation);

                var sizeResult = GetPartitionSupportedSize(partition);
                if (!sizeResult.Success)
                    return new QueryResult<ulong>(true, 0,
                        "Unable to query extendable space. No unallocated space may be available.");

                var currentSize = partition.GetPropertySafe<ulong>("Size");

                if (sizeResult.SizeMax <= currentSize)
                    return new QueryResult<ulong>(true, 0, "No unallocated space available for extension.");

                var extendableMB = (sizeResult.SizeMax - currentSize) / DiskManagementConstants.BYTES_PER_MB;
                return new QueryResult<ulong>(true, extendableMB, $"Extendable space: {extendableMB} MB");
            }, nameof(QueryExtendableSpaceByIndex), diskIndex, partitionIndex);
        }

        /// <summary>
        /// Mark partition as active (MBR only)
        /// </summary>
        public OperationResult MarkPartitionActive(uint diskIndex, uint partitionIndex)
        {
            return ExecuteWmiOperation(() =>
            {
                using var connection = CreateConnection();
                using var disk = GetDisk(connection, diskIndex);

                if (disk == null)
                    return OperationResult.Fail(ErrorMessages.DiskNotFound);

                var style = disk.GetPropertySafe<ushort>("PartitionStyle");
                if (style != DiskManagementConstants.PARTITION_STYLE_MBR)
                    return OperationResult.Fail(ErrorMessages.MbrOnly);

                using var partition = GetPartition(connection, diskIndex, partitionIndex);
                if (partition == null)
                    return OperationResult.Fail(ErrorMessages.PartitionNotFound);

                using WmiMethod setAttributesMethod = partition.GetMethod("SetAttributes");
                using WmiMethodParameters inParams = setAttributesMethod.CreateInParameters();
                inParams.SetPropertyValue("IsActive", true);

                var returnValue = partition.ExecuteMethod<uint>(setAttributesMethod, inParams, out WmiMethodParameters setAttributesOutParams);
                setAttributesOutParams?.Dispose();

                return returnValue == DiskManagementConstants.WMI_SUCCESS
                    ? OperationResult.Ok("Partition marked as active.")
                    : OperationResult.Fail($"Operation failed. Error code: {returnValue}", returnValue);
            }, nameof(MarkPartitionActive), diskIndex, partitionIndex);
        }

        #endregion

        #region Drive Letter Operations

        /// <summary>
        /// Change drive letter
        /// </summary>
        public OperationResult ChangeDriveLetter(string currentDriveLetter, string newDriveLetter)
        {
            var currentValidation = ValidateDriveLetter(currentDriveLetter);
            if (!currentValidation.IsValid)
                return OperationResult.Fail(currentValidation.ErrorMessage ?? "Invalid current drive letter");

            var newValidation = ValidateDriveLetter(newDriveLetter);
            if (!newValidation.IsValid)
                return OperationResult.Fail(newValidation.ErrorMessage ?? "Invalid new drive letter");

            var currentNormalized = NormalizeDriveLetter(currentDriveLetter);
            var newNormalized = NormalizeDriveLetter(newDriveLetter);

            if (currentNormalized == newNormalized)
                return OperationResult.Fail(ErrorMessages.DriveLetterSame);

            if (IsDriveLetterInUse(newNormalized))
                return OperationResult.Fail($"{ErrorMessages.DriveLetterInUse}: {newNormalized}:");

            return ExecuteWmiOperation(() =>
            {
                char[] volumeName = new char[50];
                if (!Win32PInvoke.GetVolumeNameForVolumeMountPoint(currentNormalized + ":\\", volumeName))
                {
                    var lastErr = Marshal.GetLastWin32Error();
                    return OperationResult.Fail(
                        $"Unable to get volume information for {currentNormalized}:. Win32 Error: {lastErr}");
                }

                string volumeGuid = new string(volumeName).TrimEnd('\0');

                if (!Win32PInvoke.DeleteVolumeMountPoint(currentNormalized + ":\\"))
                {
                    var lastErr = Marshal.GetLastWin32Error();
                    return OperationResult.Fail(
                        $"Unable to remove old drive letter. Win32 Error: {lastErr}");
                }

                if (!Win32PInvoke.SetVolumeMountPoint(newNormalized + ":\\", volumeGuid))
                {
                    var setError = Marshal.GetLastWin32Error();

                    // Rollback: try to restore original mount point
                    if (!Win32PInvoke.SetVolumeMountPoint(currentNormalized + ":\\", volumeGuid))
                    {
                        var rollbackError = Marshal.GetLastWin32Error();
                        LogError(nameof(ChangeDriveLetter),
                            new InvalidOperationException(
                                $"CRITICAL: Rollback failed! Volume {volumeGuid} has no mount point. " +
                                $"Rollback Win32 Error: {rollbackError}"));

                        return OperationResult.Fail(
                            $"Unable to set new drive letter (Win32 Error: {setError}). " +
                            $"WARNING: Rollback also failed (Win32 Error: {rollbackError}). " +
                            $"Volume GUID: {volumeGuid} ??manual intervention required.");
                    }

                    return OperationResult.Fail(
                        $"Unable to set new drive letter. Win32 Error: {setError}. Original drive letter restored.");
                }

                return OperationResult.Ok(
                    $"Drive letter changed from {currentNormalized}: to {newNormalized}:.");
            }, nameof(ChangeDriveLetter), driveLetter: currentNormalized);
        }

        /// <summary>
        /// Assign drive letter
        /// </summary>
        public OperationResult AssignDriveLetter(uint diskIndex, uint partitionIndex, string driveLetter)
        {
            var validation = ValidateDriveLetter(driveLetter);
            if (!validation.IsValid)
                return OperationResult.Fail(validation.ErrorMessage ?? "Invalid drive letter");

            var normalizedLetter = NormalizeDriveLetter(driveLetter);

            if (IsDriveLetterInUse(normalizedLetter))
                return OperationResult.Fail($"{ErrorMessages.DriveLetterInUse}: {normalizedLetter}:");

            return ExecuteWmiOperation(() =>
            {
                using var connection = CreateConnection();
                using var partition = GetPartition(connection, diskIndex, partitionIndex);

                if (partition == null)
                    return OperationResult.Fail(ErrorMessages.PartitionNotFound);

                using WmiMethod addAccessPathMethod = partition.GetMethod("AddAccessPath");
                using WmiMethodParameters inParams = addAccessPathMethod.CreateInParameters();
                inParams.SetPropertyValue("AccessPath", normalizedLetter + ":");

                DiagnosticLogger.LogDebug($"[AssignDriveLetter] Using AddAccessPath with AccessPath={normalizedLetter}:");

                var returnValue = partition.ExecuteMethod<uint>(addAccessPathMethod, inParams, out WmiMethodParameters addAccessPathOutParams);
                addAccessPathOutParams?.Dispose();

                return returnValue == DiskManagementConstants.WMI_SUCCESS
                    ? OperationResult.Ok($"Drive letter {normalizedLetter}: assigned successfully.")
                    : OperationResult.Fail(
                        $"Assignment failed. Error code: {returnValue} - {ErrorMessages.GetMsftErrorMessage(returnValue)}",
                        returnValue);
            }, nameof(AssignDriveLetter), diskIndex, partitionIndex, normalizedLetter);
        }

        /// <summary>
        /// Remove drive letter
        /// </summary>
        public OperationResult RemoveDriveLetter(string driveLetter)
        {
            var validation = ValidateDriveLetter(driveLetter);
            if (!validation.IsValid)
                return OperationResult.Fail(validation.ErrorMessage ?? "Invalid drive letter");

            var normalizedLetter = NormalizeDriveLetter(driveLetter);

            if (DiskManagementService.IsSystemDriveLetter(normalizedLetter + ":"))
            {
                var systemDrive = GetSystemDriveLetter();
                return OperationResult.Fail(
                    string.Format(ErrorMessages.SystemDriveLetterRemoval, systemDrive));
            }

            return ExecuteWmiOperation(() =>
            {
                using var connection = CreateConnection();
                using var partition = GetPartitionByDriveLetter(connection, normalizedLetter);

                if (partition == null)
                    return OperationResult.Fail($"Volume {normalizedLetter}: not found.");

                if (IsSpecialPartitionType(partition, out var reason))
                    return OperationResult.Fail(reason ?? ErrorMessages.SpecialPartitionOperation);

                return RemoveDriveLetterFromPartition(partition, normalizedLetter);
            }, nameof(RemoveDriveLetter), driveLetter: normalizedLetter);
        }

        /// <summary>
        /// Remove drive letter by disk and partition index
        /// </summary>
        public OperationResult RemoveDriveLetterByIndex(uint diskIndex, uint partitionIndex)
        {
            return ExecuteWmiOperation(() =>
            {
                using var connection = CreateConnection();
                using var partition = GetPartition(connection, diskIndex, partitionIndex);

                if (partition == null)
                    return OperationResult.Fail(ErrorMessages.PartitionNotFound);

                var currentLetter = partition.GetPropertySafe<char>("DriveLetter");
                if (currentLetter == '\0' || currentLetter == ' ')
                    return OperationResult.Fail(ErrorMessages.NoAccessPath);

                if (DiskManagementService.IsSystemDriveLetter($"{currentLetter}:"))
                    return OperationResult.Fail("Removing system drive letter is strictly prohibited!");

                if (IsSpecialPartitionType(partition, out var reason))
                    return OperationResult.Fail(reason ?? ErrorMessages.SpecialPartitionOperation);

                return RemoveDriveLetterFromPartition(partition, currentLetter.ToString());
            }, nameof(RemoveDriveLetterByIndex), diskIndex, partitionIndex);
        }

        /// <summary>
        /// Mount volume to folder
        /// </summary>
        public OperationResult MountVolumeToFolder(string driveLetter, string folderPath)
        {
            var validation = ValidateDriveLetter(driveLetter);
            if (!validation.IsValid)
                return OperationResult.Fail(validation.ErrorMessage ?? "Invalid drive letter");

            if (string.IsNullOrWhiteSpace(folderPath))
                return OperationResult.Fail("Folder path cannot be empty.");

            var normalizedLetter = NormalizeDriveLetter(driveLetter);
            folderPath = folderPath.Trim().TrimEnd('\\');

            if (!Directory.Exists(folderPath))
                return OperationResult.Fail($"{ErrorMessages.FolderNotExist}: {folderPath}");

            // Mount point requires an empty folder
            try
            {
                if (Directory.EnumerateFileSystemEntries(folderPath).Any())
                    return OperationResult.Fail(
                        $"Folder must be empty to be used as a mount point: {folderPath}");
            }
            catch (UnauthorizedAccessException)
            {
                return OperationResult.AccessDenied(nameof(MountVolumeToFolder));
            }

            return ExecuteWmiOperation(() =>
            {
                char[] volumeName = new char[50];
                if (!Win32PInvoke.GetVolumeNameForVolumeMountPoint(normalizedLetter + ":\\", volumeName))
                {
                    var lastErr = Marshal.GetLastWin32Error();
                    return OperationResult.Fail(
                        $"Unable to get volume information for {normalizedLetter}:. Win32 Error: {lastErr}");
                }

                string volumeGuid = new string(volumeName).TrimEnd('\0');

                if (!Win32PInvoke.SetVolumeMountPoint(folderPath + "\\", volumeGuid))
                {
                    var lastErr = Marshal.GetLastWin32Error();
                    return OperationResult.Fail(
                        $"Unable to mount volume to folder. Win32 Error: {lastErr}");
                }

                return OperationResult.Ok($"Volume successfully mounted to {folderPath}.");
            }, nameof(MountVolumeToFolder), driveLetter: normalizedLetter);
        }

        /// <summary>
        /// Get available drive letters
        /// </summary>
        public List<char> GetAvailableDriveLetters()
        {
            var available = new List<char>();

            try
            {
                using var connection = CreateConnection();
                var usedLetters = GetUsedDriveLetters(connection);

                // Skip A, B (reserved for floppy drives)
                for (char letter = 'C'; letter <= 'Z'; letter++)
                {
                    if (!usedLetters.Contains(letter))
                        available.Add(letter);
                }
            }
            catch (Exception ex)
            {
                LogError(nameof(GetAvailableDriveLetters), ex);
            }

            return available;
        }

        /// <summary>
        /// Get unallocated space ??calculated by partition gap analysis
        /// </summary>
        public List<UnallocatedSpace> GetUnallocatedSpace(uint diskIndex)
        {
            var spaces = new List<UnallocatedSpace>();

            try
            {
                using var connection = CreateConnection();
                using var disk = GetDisk(connection, diskIndex);

                if (disk == null)
                    return spaces;

                var diskSize = disk.GetPropertySafe<ulong>("Size");
                if (diskSize == 0)
                    return spaces;

                var partitions = GetAllPartitionsOnDisk(connection, diskIndex);

                // Starting offset: typically 1MB (GPT header / disk protection area)
                ulong currentOffset = DiskManagementConstants.ALIGNMENT_1MB;

                if (partitions.Count > 0 && partitions[0].Offset < DiskManagementConstants.ALIGNMENT_1MB)
                    currentOffset = partitions[0].Offset;

                foreach (var part in partitions)
                {
                    if (part.Offset > currentOffset)
                    {
                        var gapSize = part.Offset - currentOffset;
                        if (gapSize > DiskManagementConstants.ALIGNMENT_1MB)
                        {
                            spaces.Add(new UnallocatedSpace
                            {
                                Offset = currentOffset,
                                Size = gapSize,
                                DiskIndex = diskIndex
                            });
                        }
                    }

                    var partEnd = part.Offset + part.Size;
                    if (partEnd > currentOffset)
                        currentOffset = partEnd;
                }

                // Check trailing unallocated space (reserve 1MB for GPT backup)
                var reservedEnd = DiskManagementConstants.GPT_BACKUP_RESERVE;
                if (currentOffset + reservedEnd < diskSize)
                {
                    var trailingSpace = diskSize - currentOffset - reservedEnd;
                    if (trailingSpace > DiskManagementConstants.ALIGNMENT_1MB)
                    {
                        spaces.Add(new UnallocatedSpace
                        {
                            Offset = currentOffset,
                            Size = trailingSpace,
                            DiskIndex = diskIndex
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                LogError(nameof(GetUnallocatedSpace), ex);
            }

            return spaces;
        }

        #endregion

        #region Helper Methods ??WMI Operations

        private WmiConnection CreateConnection()
        {
            var connection = new WmiConnection(DiskManagementConstants.StorageWmiScope);
            connection.Open();
            return connection;
        }

        private WmiObject? GetDisk(WmiConnection connection, uint diskIndex)
        {
            return connection.CreateQuery($"SELECT * FROM MSFT_Disk WHERE Number = {diskIndex}").FirstOrDefault();
        }

        private WmiObject? GetPartition(WmiConnection connection, uint diskIndex, uint partitionIndex)
        {
            var partitionNumber = partitionIndex + 1;
            return connection.CreateQuery(
                $"SELECT * FROM MSFT_Partition WHERE DiskNumber = {diskIndex} AND PartitionNumber = {partitionNumber}").FirstOrDefault();
        }

        private WmiObject? GetPartitionByDriveLetter(WmiConnection connection, string driveLetter)
        {
            var sanitized = SanitizeWqlValue(driveLetter);
            return connection.CreateQuery($"SELECT * FROM MSFT_Partition WHERE DriveLetter = '{sanitized}'").FirstOrDefault();
        }

        /// <summary>
        /// BUG-FIX: MSFT_Volume.DriveLetter is Char16 (single char) ??query with single char, not "C:"
        /// </summary>
        private WmiObject? GetVolumeByDriveLetter(WmiConnection connection, string driveLetter)
        {
            var sanitized = SanitizeWqlValue(driveLetter);
            return connection.CreateQuery($"SELECT * FROM MSFT_Volume WHERE DriveLetter = '{sanitized}'").FirstOrDefault();
        }

        private HashSet<char> GetUsedDriveLetters(WmiConnection connection)
        {
            var used = new HashSet<char>();

            foreach (WmiObject partition in connection.CreateQuery("SELECT DriveLetter FROM MSFT_Partition").DisposeItems())
            {
                var letter = partition.GetPropertySafe<char>("DriveLetter");
                if (letter != '\0' && letter != ' ' && char.IsLetter(letter))
                    used.Add(char.ToUpperInvariant(letter));
            }

            return used;
        }

        /// <summary>
        /// Get all partitions on a disk, sorted by offset
        /// </summary>
        private List<(ulong Offset, ulong Size)> GetAllPartitionsOnDisk(WmiConnection connection, uint diskIndex)
        {
            var partitions = new List<(ulong Offset, ulong Size)>();

            foreach (WmiObject part in connection.CreateQuery(
                $"SELECT Offset, Size FROM MSFT_Partition WHERE DiskNumber = {diskIndex}").DisposeItems())
            {
                var offset = part.GetPropertySafe<ulong>("Offset");
                var size = part.GetPropertySafe<ulong>("Size");
                partitions.Add((offset, size));
            }

            partitions.Sort((a, b) => a.Offset.CompareTo(b.Offset));
            return partitions;
        }

        #endregion

        #region Helper Methods ??Partition Operations

        /// <summary>
        /// Result from CreatePartition ??carries DiskNumber/PartitionNumber for subsequent formatting
        /// </summary>
        private record CreatePartitionResult(
            bool Success,
            string Message,
            uint? DiskNumber = null,
            uint? PartitionNumber = null);

        private CreatePartitionResult CreatePartitionOnDisk(
            WmiObject disk,
            ulong sizeInMB,
            string? driveLetter,
            ulong? offset)
        {
            try
            {
                using WmiMethod createPartitionMethod = disk.GetMethod("CreatePartition");
                using WmiMethodParameters inParams = createPartitionMethod.CreateInParameters();

                if (sizeInMB > 0)
                    inParams.SetUInt64Parameter("Size", sizeInMB * DiskManagementConstants.BYTES_PER_MB);
                else
                    inParams.SetPropertyValue("UseMaximumSize", true);

                if (offset.HasValue && offset.Value > 0)
                {
                    var alignedOffset = AlignToMB(offset.Value);
                    inParams.SetUInt64Parameter("Offset", alignedOffset);
                    LogDebug(nameof(CreatePartitionOnDisk),
                        $"Using aligned offset {alignedOffset} (original: {offset.Value})");
                }

                if (!string.IsNullOrEmpty(driveLetter))
                {
                    var letter = NormalizeDriveLetter(driveLetter);
                    if (letter.Length == 1)
                        inParams.SetChar16Parameter("DriveLetter", letter[0]);
                }
                else
                {
                    inParams.SetPropertyValue("AssignDriveLetter", true);
                }

                var returnValue = disk.ExecuteMethod<uint>(createPartitionMethod, inParams, out WmiMethodParameters outParams);
                using (outParams)
                {
                    if (returnValue != DiskManagementConstants.WMI_SUCCESS)
                        return new CreatePartitionResult(false,
                            $"Failed to create partition. Error code: {returnValue} - {ErrorMessages.GetMsftErrorMessage(returnValue)}");

                    // Extract DiskNumber and PartitionNumber from embedded object
                    if (outParams?["CreatedPartition"] is WmiObject createdPartition)
                    {
                        using (createdPartition)
                        {
                            var diskNum = Convert.ToUInt32(createdPartition["DiskNumber"]);
                            var partNum = Convert.ToUInt32(createdPartition["PartitionNumber"]);

                            return new CreatePartitionResult(true,
                                "Partition created successfully.",
                                diskNum,
                                partNum);
                        }
                    }

                    return new CreatePartitionResult(true,
                        "Partition created, but unable to get reference for formatting. Please format manually.");
                }
            }
            catch (WmiException wex)
            {
                return new CreatePartitionResult(false, $"Failed to create partition: {wex.Message}");
            }
        }

        /// <summary>
        /// Format a newly created partition by DiskNumber + PartitionNumber lookup
        /// </summary>
        private OperationResult FormatNewPartition(
            WmiConnection connection,
            uint? diskNumber,
            uint? partitionNumber,
            string fileSystem,
            string label,
            bool quickFormat)
        {
            if (!diskNumber.HasValue || !partitionNumber.HasValue)
                return OperationResult.Fail(
                    "Unable to locate partition for formatting ??missing disk/partition number.");

            try
            {
                for (int retry = 0; retry < DiskManagementConstants.MAX_VOLUME_WAIT_RETRIES; retry++)
                {
                    var partObj = connection.CreateQuery(
                        $"SELECT DriveLetter FROM MSFT_Partition " +
                        $"WHERE DiskNumber = {diskNumber.Value} AND PartitionNumber = {partitionNumber.Value}").FirstOrDefault();

                    if (partObj != null)
                    {
                        using (partObj)
                        {
                            var dl = partObj.GetPropertySafe<char>("DriveLetter");
                            if (dl != '\0' && dl != ' ')
                            {
                                using var volume = GetVolumeByDriveLetter(connection, dl.ToString());
                                if (volume != null)
                                    return FormatVolumeObject(volume, fileSystem, label, quickFormat);
                            }
                        }
                    }

                    // Volume may not be ready yet, wait and retry
                    Thread.Sleep(DiskManagementConstants.VOLUME_WAIT_DELAY_MS);
                }

                // Last resort: match volume by partition size
                return TryFormatVolumeByPartitionId(connection, diskNumber.Value, partitionNumber.Value,
                    fileSystem, label, quickFormat);
            }
            catch (Exception ex)
            {
                return OperationResult.Fail($"Error occurred during formatting: {ex.Message}");
            }
        }

        /// <summary>
        /// Fallback format: enumerate all volumes and match by size
        /// </summary>
        private OperationResult TryFormatVolumeByPartitionId(
            WmiConnection connection,
            uint diskNumber,
            uint partitionNumber,
            string fileSystem,
            string label,
            bool quickFormat)
        {
            try
            {
                var partition = connection.CreateQuery(
                    $"SELECT * FROM MSFT_Partition " +
                    $"WHERE DiskNumber = {diskNumber} AND PartitionNumber = {partitionNumber}").FirstOrDefault();

                if (partition == null)
                    return OperationResult.Fail("Unable to find partition for formatting.");

                using (partition)
                {
                    var partSize = partition.GetPropertySafe<ulong>("Size");

                    foreach (WmiObject volume in connection.CreateQuery("SELECT * FROM MSFT_Volume WHERE FileSystemLabel = ''").DisposeItems())
                    {
                        var volSize = volume.GetPropertySafe<ulong>("Size");
                        // Allow 10% size tolerance (filesystem metadata overhead)
                        if (volSize > 0 && Math.Abs((long)volSize - (long)partSize) < (long)(partSize * 0.1))
                        {
                            return FormatVolumeObject(volume, fileSystem, label, quickFormat);
                        }
                    }
                }

                return OperationResult.Fail("Unable to find corresponding volume for formatting.");
            }
            catch (Exception ex)
            {
                return OperationResult.Fail($"Fallback format failed: {ex.Message}");
            }
        }

        private OperationResult FormatVolumeObject(
            WmiObject volume,
            string fileSystem,
            string label,
            bool quickFormat)
        {
            try
            {
                using WmiMethod formatMethod = volume.GetMethod("Format");
                using WmiMethodParameters inParams = formatMethod.CreateInParameters();
                inParams.SetPropertyValue("FileSystem", fileSystem);
                inParams.SetPropertyValue("FileSystemLabel", label ?? "");
                inParams.SetPropertyValue("Full", !quickFormat);
                inParams.SetPropertyValue("Force", true);

                DiagnosticLogger.LogDebug($"[FormatVolumeObject] Formatting with FileSystem={fileSystem}, Label=\"{label ?? ""}\", Full={!quickFormat}, Force=true");

                var returnValue = volume.ExecuteMethod<uint>(formatMethod, inParams, out WmiMethodParameters formatOutParams);
                formatOutParams?.Dispose();

                return returnValue == DiskManagementConstants.WMI_SUCCESS
                    ? OperationResult.Ok("Formatting successful.")
                    : OperationResult.Fail(
                        $"Formatting failed. Error code: {returnValue} - {ErrorMessages.GetMsftErrorMessage(returnValue)}",
                        returnValue);
            }
            catch (WmiException wex)
            {
                return OperationResult.Fail($"Formatting failed: {wex.Message}");
            }
        }

        private OperationResult ResizePartitionExtend(WmiObject partition, ulong sizeInMB)
        {
            try
            {
                var currentSize = partition.GetPropertySafe<ulong>("Size");
                var sizeResult = GetPartitionSupportedSize(partition);

                ulong targetSize;
                if (sizeResult.Success)
                {
                    if (sizeInMB > 0)
                    {
                        targetSize = currentSize + (sizeInMB * DiskManagementConstants.BYTES_PER_MB);
                        if (targetSize > sizeResult.SizeMax)
                            return OperationResult.Fail(
                                $"Requested extension size exceeds available space. " +
                                $"Maximum extendable to: {FormatSize(sizeResult.SizeMax)}");
                    }
                    else
                    {
                        targetSize = sizeResult.SizeMax;
                    }

                    if (targetSize <= currentSize)
                        return OperationResult.Fail(ErrorMessages.NoExtensionSpace);
                }
                else
                {
                    // Fallback: when GetSupportedSize is unavailable, only explicit sizes allowed
                    if (sizeInMB == 0)
                        return OperationResult.Fail(
                            "Unable to determine maximum extendable size. Please specify an explicit size in MB.");

                    targetSize = currentSize + (sizeInMB * DiskManagementConstants.BYTES_PER_MB);
                    LogDebug(nameof(ResizePartitionExtend),
                        $"GetSupportedSize unavailable, attempting resize to {FormatSize(targetSize)} based on request.");
                }

                return ResizePartition(partition, targetSize,
                    $"Volume extended successfully. New size: {FormatSize(targetSize)}");
            }
            catch (WmiException wex)
            {
                return OperationResult.Fail($"Extension failed: {wex.Message}");
            }
        }

        private OperationResult ResizePartitionShrink(WmiObject partition, ulong sizeInMB)
        {
            try
            {
                var currentSize = partition.GetPropertySafe<ulong>("Size");
                var shrinkBytes = sizeInMB * DiskManagementConstants.BYTES_PER_MB;

                // Prevent ulong underflow when shrinkBytes > currentSize
                if (shrinkBytes >= currentSize)
                    return OperationResult.Fail(
                        $"Requested shrink size ({sizeInMB} MB) exceeds current partition size ({currentSize / DiskManagementConstants.BYTES_PER_MB} MB).");

                var targetSize = currentSize - shrinkBytes;

                var sizeResult = GetPartitionSupportedSize(partition);
                if (sizeResult.Success)
                {
                    if (targetSize < sizeResult.SizeMin)
                        return OperationResult.Fail(
                            $"Shrink size exceeds shrinkable space. Minimum size: {FormatSize(sizeResult.SizeMin)}");
                }
                else
                {
                    // Fallback: estimate minimum from DriveInfo when GetSupportedSize is unavailable
                    var driveLetter = partition.GetPropertySafe<char>("DriveLetter");
                    if (driveLetter != '\0' && driveLetter != ' ')
                    {
                        var estimatedMin = EstimateMinPartitionSize(driveLetter);
                        if (estimatedMin > 0 && targetSize < estimatedMin)
                            return OperationResult.Fail(
                                $"Shrink may exceed safe limits. Estimated minimum partition size: {FormatSize(estimatedMin)}.");
                    }
                    LogDebug(nameof(ResizePartitionShrink),
                        $"GetSupportedSize unavailable, attempting resize to {FormatSize(targetSize)} based on estimation.");
                }

                return ResizePartition(partition, targetSize, $"Volume shrunk successfully by {sizeInMB} MB.");
            }
            catch (WmiException wex)
            {
                return OperationResult.Fail($"Shrink failed: {wex.Message}");
            }
        }

        private OperationResult ResizePartition(WmiObject partition, ulong targetSize, string successMessage)
        {
            using WmiMethod resizeMethod = partition.GetMethod("Resize");
            using WmiMethodParameters inParams = resizeMethod.CreateInParameters();
            inParams.SetUInt64Parameter("Size", targetSize);

            var returnValue = partition.ExecuteMethod<uint>(resizeMethod, inParams, out WmiMethodParameters resizeOutParams);
            resizeOutParams?.Dispose();

            return returnValue == DiskManagementConstants.WMI_SUCCESS
                ? OperationResult.Ok(successMessage)
                : OperationResult.Fail(
                    $"Resize failed. Error code: {returnValue} - {ErrorMessages.GetMsftErrorMessage(returnValue)}",
                    returnValue);
        }

        private (bool Success, ulong SizeMin, ulong SizeMax, string Message, uint? ErrorCode)
            GetPartitionSupportedSize(WmiObject partition)
        {
            try
            {
                using WmiMethod getSupportedSizeMethod = partition.GetMethod("GetSupportedSize");
                var returnValue = partition.ExecuteMethod<uint>(getSupportedSizeMethod, out WmiMethodParameters outParams);
                using (outParams)
                {
                    if (returnValue != DiskManagementConstants.WMI_SUCCESS)
                    {
                        if (returnValue == DiskManagementConstants.WMI_NOT_SUPPORTED)
                            return (false, 0, 0, ErrorMessages.PartitionNotSupportResize, returnValue);

                        return (false, 0, 0,
                            $"Unable to query partition size limits. Error code: {returnValue} - {ErrorMessages.GetMsftErrorMessage(returnValue)}",
                            returnValue);
                    }

                    var sizeMin = Convert.ToUInt64(outParams?["SizeMin"] ?? 0UL);
                    var sizeMax = Convert.ToUInt64(outParams?["SizeMax"] ?? 0UL);

                    return (true, sizeMin, sizeMax, "Success", null);
                }
            }
            catch (WmiException ex)
            {
                LogDebug(nameof(GetPartitionSupportedSize),
                    $"GetSupportedSize failed: 0x{ex.HResult:X8} - {ex.Message}");
                return (false, 0, 0, $"Unable to query partition size limits: {ex.Message}", null);
            }
        }

        private OperationResult RemoveDriveLetterFromPartition(WmiObject partition, string driveLetter)
        {
            // Method 1: Use RemoveAccessPath
            try
            {
                using WmiMethod removeAccessPathMethod = partition.GetMethod("RemoveAccessPath");
                using WmiMethodParameters inParams = removeAccessPathMethod.CreateInParameters();
                inParams.SetPropertyValue("AccessPath", driveLetter + ":\\");

                var returnValue = partition.ExecuteMethod<uint>(removeAccessPathMethod, inParams, out WmiMethodParameters removeOutParams);
                removeOutParams?.Dispose();

                if (returnValue == DiskManagementConstants.WMI_SUCCESS)
                    return OperationResult.Ok($"Drive letter {driveLetter}: removed successfully.");

                LogDebug(nameof(RemoveDriveLetterFromPartition),
                    $"RemoveAccessPath returned {returnValue}, falling back to DeleteVolumeMountPoint.");
            }
            catch (WmiException ex)
            {
                LogDebug(nameof(RemoveDriveLetterFromPartition),
                    $"RemoveAccessPath threw 0x{ex.HResult:X8}, falling back to DeleteVolumeMountPoint.");
            }

            // Method 2: Fallback using Win32 DeleteVolumeMountPoint
            try
            {
                if (!Win32PInvoke.DeleteVolumeMountPoint(driveLetter + ":\\"))
                {
                    var lastErr = Marshal.GetLastWin32Error();
                    return OperationResult.Fail(
                        $"Removal failed. Win32 Error: {lastErr}");
                }

                return OperationResult.Ok($"Drive letter {driveLetter}: removed successfully.");
            }
            catch (Exception ex)
            {
                return OperationResult.Fail($"Removal failed: {ex.Message}");
            }
        }

        #endregion

        #region Helper Methods ??Validation & Utility

        private static void LogDebug(string operation, string message,
            uint? diskIndex = null, uint? partitionIndex = null, string? driveLetter = null)
        {
            DiagnosticLogger.LogDebug($"[{operation}] {message}", diskIndex, partitionIndex, driveLetter);
        }

        private static void LogError(string operation, Exception ex,
            uint? diskIndex = null, uint? partitionIndex = null, string? driveLetter = null)
        {
            DiagnosticLogger.LogOperationError(operation, ex, diskIndex, partitionIndex, driveLetter);
        }

        private static string FormatSize(ulong bytes)
        {
            return FormatHelper.FormatSize(bytes);
        }

        private static bool IsDriveLetterInUse(string driveLetter)
        {
            var normalized = driveLetter.Trim().TrimEnd(':').ToUpperInvariant() + ":";
            return DriveInfo.GetDrives().Any(d =>
                d.Name.StartsWith(normalized, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsSpecialPartitionType(WmiObject partition, out string? reason)
        {
            reason = null;
            var gptTypeStr = partition.GetPropertySafe<string>("GptType") ?? "";

            if (!string.IsNullOrEmpty(gptTypeStr) && Guid.TryParse(gptTypeStr, out var gptGuid))
            {
                if (DiskManagementConstants.NonResizablePartitionTypes.Contains(gptGuid))
                {
                    reason = ErrorMessages.SpecialPartitionOperation;
                    return true;
                }
            }

            // Additional check: MBR system partition (non-boot)
            var isBoot = partition.GetPropertySafe<bool>("IsBoot");
            var isSystem = partition.GetPropertySafe<bool>("IsSystem");
            if (isSystem && !isBoot)
            {
                reason = ErrorMessages.SpecialPartitionOperation;
                return true;
            }

            return false;
        }

        private (bool IsValid, string? ErrorMessage) ValidateDriveLetter(string driveLetter)
        {
            if (string.IsNullOrWhiteSpace(driveLetter))
                return (false, ErrorMessages.DriveLetterEmpty);

            var normalized = NormalizeDriveLetter(driveLetter);

            if (normalized.Length != 1 || normalized[0] < 'A' || normalized[0] > 'Z')
                return (false, "Drive letter must be a single letter from A to Z.");

            return (true, null);
        }

        private static string GetSystemDriveLetter()
        {
            var systemDrive = Environment.GetEnvironmentVariable("SystemDrive");
            if (string.IsNullOrWhiteSpace(systemDrive))
                systemDrive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System));

            systemDrive = (systemDrive ?? string.Empty).Trim().TrimEnd('\\', '/');
            if (!systemDrive.EndsWith(":", StringComparison.Ordinal))
                systemDrive += ":";

            return systemDrive.ToUpperInvariant();
        }

        private string NormalizeDriveLetter(string driveLetter)
        {
            return driveLetter.Trim().TrimEnd(':').TrimEnd('\\').ToUpperInvariant();
        }

        private ulong AlignToMB(ulong offset)
        {
            return ((offset + DiskManagementConstants.ALIGNMENT_1MB - 1) / DiskManagementConstants.ALIGNMENT_1MB)
                   * DiskManagementConstants.ALIGNMENT_1MB;
        }

        /// <summary>
        /// Sanitize WQL values to prevent injection
        /// </summary>
        private static string SanitizeWqlValue(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            return value.Replace("'", "").Replace("\\", "").Replace("\"", "");
        }

        private QueryResult<ulong> EstimateShrinkableSpace(string driveLetter)
        {
            try
            {
                var driveInfo = new DriveInfo(driveLetter);
                if (!driveInfo.IsReady)
                    return new QueryResult<ulong>(false, 0, ErrorMessages.DriveNotReady);

                var freeSpace = (ulong)driveInfo.AvailableFreeSpace;
                var shrinkableMB = (ulong)(freeSpace * DiskManagementConstants.SHRINKABLE_SPACE_ESTIMATE_RATIO)
                                   / DiskManagementConstants.BYTES_PER_MB;

                return new QueryResult<ulong>(true, shrinkableMB,
                    $"Estimated shrinkable space: {shrinkableMB} MB (actual may be less due to unmovable files)");
            }
            catch (Exception ex)
            {
                LogError(nameof(EstimateShrinkableSpace), ex);
                return new QueryResult<ulong>(false, 0, "Unable to estimate shrinkable space.");
            }
        }

        /// <summary>
        /// Estimate minimum partition size from used space when GetSupportedSize is unavailable.
        /// Returns 0 on failure (caller should proceed optimistically).
        /// </summary>
        private ulong EstimateMinPartitionSize(char driveLetter)
        {
            try
            {
                var driveInfo = new DriveInfo(driveLetter.ToString());
                if (!driveInfo.IsReady)
                    return 0;

                var usedSpace = (ulong)(driveInfo.TotalSize - driveInfo.AvailableFreeSpace);
                // Add 20% buffer over used space for filesystem metadata, MFT, unmovable files, etc.
                return (ulong)(usedSpace * 1.2);
            }
            catch (Exception ex)
            {
                LogDebug(nameof(EstimateMinPartitionSize), $"Estimation failed: {ex.Message}");
                return 0;
            }
        }

        #endregion

        #region Helper Methods ??Error Handling & Logging

        private OperationResult ExecuteWmiOperation(
            Func<OperationResult> operation,
            string operationName,
            uint? diskIndex = null,
            uint? partitionIndex = null,
            string? driveLetter = null)
        {
            DiagnosticLogger.LogOperationStart(operationName, diskIndex, partitionIndex, driveLetter);

            try
            {
                var result = operation();

                if (result.Success)
                {
                    DiagnosticLogger.LogOperationSuccess(operationName, result.Message,
                        diskIndex, partitionIndex, driveLetter);
                }
                else
                {
                    var level = result.PartialSuccess ? "PARTIAL" : "FAIL";
                    DiagnosticLogger.LogWarning(
                        $"{operationName} [{level}]: {result.Message}",
                        diskIndex, partitionIndex, driveLetter);
                }

                return result;
            }
            catch (WmiException wex)
            {
                DiagnosticLogger.LogOperationError(operationName, wex,
                    diskIndex, partitionIndex, driveLetter,
                    $"WMI Error Code: 0x{wex.HResult:X8}");
                return OperationResult.Fail($"{operationName} failed: {wex.Message}");
            }
            catch (COMException comEx)
            {
                DiagnosticLogger.LogOperationError(operationName, comEx,
                    diskIndex, partitionIndex, driveLetter,
                    $"COM HRESULT: 0x{comEx.HResult:X8}");
                return OperationResult.Fail(
                    $"{operationName} failed with COM error: {comEx.Message} (0x{comEx.HResult:X8})");
            }
            catch (UnauthorizedAccessException uaEx)
            {
                DiagnosticLogger.LogOperationError(operationName, uaEx,
                    diskIndex, partitionIndex, driveLetter);
                return OperationResult.AccessDenied(operationName);
            }
            catch (Exception ex)
            {
                DiagnosticLogger.LogOperationError(operationName, ex,
                    diskIndex, partitionIndex, driveLetter);
                return OperationResult.Fail($"Error during {operationName}: {ex.Message}");
            }
        }

        private QueryResult<T> ExecuteWmiQuery<T>(
            Func<QueryResult<T>> operation,
            string operationName,
            uint? diskIndex = null,
            uint? partitionIndex = null,
            string? driveLetter = null)
        {
            DiagnosticLogger.LogOperationStart(operationName, diskIndex, partitionIndex, driveLetter);

            try
            {
                var result = operation();

                if (result.Success)
                {
                    DiagnosticLogger.LogOperationSuccess(operationName, result.Message,
                        diskIndex, partitionIndex, driveLetter);
                }
                else
                {
                    DiagnosticLogger.LogWarning(
                        $"{operationName} returned failure: {result.Message}",
                        diskIndex, partitionIndex, driveLetter);
                }

                return result;
            }
            catch (WmiException wex)
            {
                DiagnosticLogger.LogOperationError(operationName, wex,
                    diskIndex, partitionIndex, driveLetter,
                    $"WMI Error Code: 0x{wex.HResult:X8}");
                return new QueryResult<T>(false, default(T)!, $"{operationName} failed: {wex.Message}");
            }
            catch (COMException comEx)
            {
                DiagnosticLogger.LogOperationError(operationName, comEx,
                    diskIndex, partitionIndex, driveLetter,
                    $"COM HRESULT: 0x{comEx.HResult:X8}");
                return new QueryResult<T>(false, default(T)!,
                    $"{operationName} failed with COM error: {comEx.Message}");
            }
            catch (UnauthorizedAccessException uaEx)
            {
                DiagnosticLogger.LogOperationError(operationName, uaEx,
                    diskIndex, partitionIndex, driveLetter);
                return QueryResult<T>.AccessDenied(default(T)!, operationName);
            }
            catch (Exception ex)
            {
                DiagnosticLogger.LogOperationError(operationName, ex,
                    diskIndex, partitionIndex, driveLetter);
                return new QueryResult<T>(false, default(T)!, $"Error during {operationName}: {ex.Message}");
            }
        }

        #endregion
    }
}



