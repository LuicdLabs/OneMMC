using OneMMC.Core.Localization;

namespace OneMMC.Core.Features.PCManagement.Services.DiskMgmt.Common
{
    /// <summary>
    /// Centralized error messages for disk management operations
    /// TODO: Move to resource files for localization
    /// </summary>
    public static class ErrorMessages
    {
        #region General Errors
        public const string DriveLetterEmpty = "Drive letter cannot be empty.";
        public const string SizeRequired = "Size must be greater than 0.";
        public const string DiskNotFound = "Specified disk not found.";
        public const string PartitionNotFound = "Specified partition not found.";
        public const string VolumeNotFound = "Volume not found.";
        public const string DriveLetterInUse = "Drive letter is already in use.";
        public const string DriveLetterSame = "New and old drive letters are the same.";
        public const string FolderNotExist = "Folder does not exist.";
        public const string DriveNotReady = "Drive is not ready.";
        public const string NoExtensionSpace = "No available space for extension.";
        public const string NoAccessPath = "This partition does not have a drive letter assigned.";
        #endregion

        #region System Protection Errors
        public const string SystemDriveFormat = "âš ï¸ Formatting system drive is strictly prohibited! Drive '{0}' contains the Windows operating system. Formatting it will make the system unbootable.";
        public const string SystemDriveLetterRemoval = "âš ï¸ Removing system drive letter is strictly prohibited! Drive '{0}' contains the Windows operating system. Removing it will cause system malfunction.";
        public const string SystemDiskClean = "âš ï¸ Strictly forbidden to clean system disk! This disk contains the Windows operating system, cleaning will make the system unbootable.";
        public const string CriticalPartitionsOnDisk = "âš ï¸ This disk contains critical system partitions (EFI, Recovery partition), cleaning may cause system boot failure.";
        public const string CannotDeleteSystemPartition = "âš ï¸ Cannot perform this operation on system partition. Drive \"{0}\" contains the Windows operating system.";
        public const string CannotDeleteEfiPartition = "âš ï¸ Cannot delete EFI System Partition. This partition is required for boot.";
        public const string CannotDeleteRecoveryPartition = "âš ï¸ Cannot delete Recovery Partition. This partition is used for system recovery.";
        public const string CannotDeleteMsrPartition = "âš ï¸ Cannot delete Microsoft Reserved Partition. This partition is required for system operation.";
        public const string CriticalSystemPartition = "âš ï¸ Cannot perform this operation on this critical system partition.";
        #endregion

        #region Operation Specific Errors
        public const string SpecialPartitionOperation = "This partition type (System/Reserved/Recovery) does not support this operation.";
        public const string PartitionNotSupportResize = "This partition does not support resizing. It may be a system, recovery, or special partition type.";
        public const string MbrOnly = "Only MBR disks support marking active partition. GPT disks use EFI system partition for boot.";
        public const string DiskMustBeCleanedBeforeConversion = "Disk must be cleaned of all partitions before conversion. Please use 'Clean Disk' function first.";
        public const string DiskAlreadyGpt = "Disk is already GPT format.";
        public const string DiskAlreadyMbr = "Disk is already MBR format.";
        public const string DiskAlreadyDynamic = "Disk is already a dynamic disk.";
        public const string DiskAlreadyBasic = "Disk is already a basic disk.";
        #endregion

        #region VHD Errors
        public const string VhdPathEmpty = "VHD path cannot be empty.";
        public const string VhdFileNotFound = "VHD file not found.";
        public const string VhdFileAlreadyExists = "VHD file already exists.";
        public static string VhdInsufficientPrivileges => LocalizationProvider.Current.GetString(ResourceFileNames.Common, CommonKeys.AccessDenied_Generic);
        public static string VhdAccessDenied => LocalizationProvider.Current.GetString(ResourceFileNames.Common, CommonKeys.AccessDenied_Generic);
        public const string VhdFileInUse = "File is being used by another process.";
        #endregion

        #region Dynamic Disk
        public const string DynamicDiskDeprecated = "Dynamic disk conversion feature is not available in this version.\n\n" +
                                                    "Tips:\n" +
                                                    "â€¢ Please use Windows Disk Management console (diskmgmt.msc) for conversion\n" +
                                                    "â€¢ Microsoft has marked dynamic disks as deprecated technology\n" +
                                                    "â€¢ Consider using Storage Spaces as an alternative solution";
        public const string DynamicDiskSystemWarning = "âš ï¸ Not recommended to convert system disk to dynamic disk. This operation may cause system boot failure or prevent system upgrades.";
        #endregion

        /// <summary>
        /// Get MSFT_* API error message by error code
        /// </summary>
        public static string GetMsftErrorMessage(uint errorCode)
        {
            return errorCode switch
            {
                0 => "Success",
                1 => "Unsupported operation",
                2 => "Unspecified error",
                3 => "Timeout",
                4 => "Operation failed",
                5 => "Invalid parameter",
                6 => LocalizationProvider.Current.GetString(ResourceFileNames.Common, CommonKeys.AccessDenied_Generic),
                40001 => LocalizationProvider.Current.GetString(ResourceFileNames.DiskManagement, DiskMgmtKeys.AccessDenied_AdminRequired),
                40002 => "Insufficient resources",
                40003 => "Object not found",
                40004 => "Method not supported (The operation is not supported on this object or in this configuration)",
                41000 => "Disk not initialized",
                41001 => "Disk not ready",
                41002 => "Disk in use",
                41003 => "Volume in use",
                41010 => "The specified partition type isn't valid",
                41011 => "Only the first 2 TB are usable on MBR disks",
                41012 => "The specified offset isn't valid",
                41013 => "Can't convert the style of a disk with data or other known partitions on it",
                41014 => "The disk isn't large enough to support a GPT partition style",
                41015 => "There is no media in the device",
                41016 => "The specified offset isn't valid",
                41017 => "The specified partition layout is invalid",
                42002 => "The requested access path is already in use",
                42004 => "Cannot assign access paths to hidden partitions",
                42007 => "The access path isn't valid",
                42008 => "Cannot delete system or boot partition",
                _ => $"Error code {errorCode}"
            };
        }

        /// <summary>
        /// Get VHD API error message by error code
        /// </summary>
        public static string GetVhdErrorMessage(int errorCode)
        {
            return errorCode switch
            {
                0 => "Success",
                2 => "File not found",
                3 => "Path not found",
                5 => LocalizationProvider.Current.GetString(ResourceFileNames.Common, CommonKeys.AccessDenied_Generic),
                32 => "File is being used by another process",
                87 => "Invalid parameter",
                183 => "File already exists",
                1314 => LocalizationProvider.Current.GetString(ResourceFileNames.Common, CommonKeys.AccessDenied_Generic),
                _ => $"Error code: {errorCode}"
            };
        }
    }
}


