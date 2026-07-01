using OneMMC.Core.Localization;
namespace OneMMC.Localization
{
    /// <summary>
    /// Localized strings for Disk Management.
    /// Resources are loaded from DiskManagement.resw file.
    /// </summary>
    public partial class LocalizedStrings
    {
        // Disk Management Strings
        public string DiskMgmt_VirtualHardDisk => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_VirtualHardDisk");
        public string DiskMgmt_VirtualHardDiskDescription => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_VirtualHardDiskDescription");
        public string DiskMgmt_VHDOperations => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_VHDOperations");
        public string DiskMgmt_Attach => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_Attach");
        public string DiskMgmt_Detach => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_Detach");
        public string DiskMgmt_OpenDiskManagement => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_OpenDiskManagement");
        public string DiskMgmt_Properties => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_Properties");
        public string DiskMgmt_InitializeDisk => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_InitializeDisk");
        public string DiskMgmt_CreateSimpleVolume => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_CreateSimpleVolume");
        public string DiskMgmt_Online => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_Online");
        public string DiskMgmt_Offline => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_Offline");
        public string DiskMgmt_SetReadOnly => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_SetReadOnly");
        public string DiskMgmt_ClearReadOnly => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_ClearReadOnly");
        public string DiskMgmt_CleanDisk => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_CleanDisk");
        public string DiskMgmt_SystemProtected => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_SystemProtected");
        public string DiskMgmt_OpenInFileExplorer => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_OpenInFileExplorer");
        public string DiskMgmt_NewSimpleVolume => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_NewSimpleVolume");
        public string DiskMgmt_ManageDriveLetter => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_ManageDriveLetter");
        public string DiskMgmt_RemoveDriveLetter => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_RemoveDriveLetter");
        public string DiskMgmt_MountToFolder => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_MountToFolder");
        public string DiskMgmt_ExtendVolume => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_ExtendVolume");
        public string DiskMgmt_ShrinkVolume => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_ShrinkVolume");
        public string DiskMgmt_Format => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_Format");
        public string DiskMgmt_MarkAsActive => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_MarkAsActive");
        public string DiskMgmt_DeleteVolume => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_DeleteVolume");
        public string DiskMgmt_Eject => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_Eject");
        public string DiskMgmt_EjectDiscTray => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_EjectDiscTray");

        // Format Volume Dialog
        public string DiskMgmt_FormatVolume => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_FormatVolume");
        public string DiskMgmt_FormatWarning => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_FormatWarning");
        public string DiskMgmt_Drive => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_Drive");
        public string DiskMgmt_VolumeLabel => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_VolumeLabel");
        public string DiskMgmt_FileSystem => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_FileSystem");
        public string DiskMgmt_AllocationUnitSize => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_AllocationUnitSize");
        public string DiskMgmt_Default => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_Default");
        public string DiskMgmt_PerformQuickFormat => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_PerformQuickFormat");
        public string DiskMgmt_EnableCompression => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_EnableCompression");

        // Create VHD Dialog
        public string DiskMgmt_CreateVHD => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_CreateVHD");
        public string DiskMgmt_Location => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_Location");
        public string DiskMgmt_VHDSize => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_VHDSize");
        public string DiskMgmt_VHDFormat => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_VHDFormat");
        public string DiskMgmt_VHDType => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_VHDType");
        public string DiskMgmt_FixedSize => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_FixedSize");
        public string DiskMgmt_DynamicallyExpanding => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_DynamicallyExpanding");

        // Attach VHD Dialog
        public string DiskMgmt_AttachVHD => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_AttachVHD");
        public string DiskMgmt_VHDLocation => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_VHDLocation");
        public string DiskMgmt_ReadOnly => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_ReadOnly");

        // Initialize Disk Dialog
        public string DiskMgmt_InitializeDiskTitle => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_InitializeDiskTitle");
        public string DiskMgmt_SelectPartitionStyle => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_SelectPartitionStyle");
        public string DiskMgmt_MBR => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_MBR");
        public string DiskMgmt_GPT => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_GPT");
        public string DiskMgmt_MBRDescription => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_MBRDescription");
        public string DiskMgmt_GPTDescription => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_GPTDescription");

        // Shrink Volume Dialog
        public string DiskMgmt_ShrinkVolumeTitle => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_ShrinkVolumeTitle");
        public string DiskMgmt_TotalSizeBeforeShrink => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_TotalSizeBeforeShrink");
        public string DiskMgmt_SizeOfAvailableShrinkSpace => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_SizeOfAvailableShrinkSpace");
        public string DiskMgmt_EnterAmountToShrink => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_EnterAmountToShrink");
        public string DiskMgmt_TotalSizeAfterShrink => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_TotalSizeAfterShrink");
        public string DiskMgmt_Shrink => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_Shrink");

        // Extend Volume Dialog
        public string DiskMgmt_ExtendVolumeTitle => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_ExtendVolumeTitle");
        public string DiskMgmt_SelectAmountOfSpace => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_SelectAmountOfSpace");
        public string DiskMgmt_MaximumAvailableSpace => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_MaximumAvailableSpace");
        public string DiskMgmt_SelectedSpace => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_SelectedSpace");
        public string DiskMgmt_Extend => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_Extend");

        // Manage Drive Letter Dialog
        public string DiskMgmt_ManageDriveLetterTitle => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_ManageDriveLetterTitle");
        public string DiskMgmt_AssignDriveLetter => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_AssignDriveLetter");
        public string DiskMgmt_DoNotAssignDriveLetter => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_DoNotAssignDriveLetter");
        public string DiskMgmt_MountInEmptyNTFSFolder => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_MountInEmptyNTFSFolder");

        // Volume Properties Dialog
        public string DiskMgmt_VolumeProperties => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_VolumeProperties");
        public string DiskMgmt_General => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_General");
        public string DiskMgmt_Type => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_Type");
        public string DiskMgmt_UsedSpace => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_UsedSpace");
        public string DiskMgmt_FreeSpace => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_FreeSpace");
        public string DiskMgmt_Capacity => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_Capacity");

        // Disk Properties Dialog
        public string DiskMgmt_DiskProperties => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_DiskProperties");
        public string DiskMgmt_Model => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_Model");
        public string DiskMgmt_Interface => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_Interface");
        public string DiskMgmt_Status => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_Status");
        public string DiskMgmt_PartitionStyle => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_PartitionStyle");

        // Create Simple Volume Dialog
        public string DiskMgmt_CreateSimpleVolumeTitle => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_CreateSimpleVolumeTitle");
        public string DiskMgmt_Disk => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_Disk");
        public string DiskMgmt_VolumeSizeMB => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_VolumeSizeMB");
        public string DiskMgmt_DriveLetter => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_DriveLetter");
        public string DiskMgmt_NewVolume => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_NewVolume");

        // Delete Volume Dialog
        public string DiskMgmt_DeleteVolumeTitle => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_DeleteVolumeTitle");
        public string DiskMgmt_DeleteVolumeWarning => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_DeleteVolumeWarning");
        public string DiskMgmt_Volume => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_Volume");
        public string DiskMgmt_ConfirmDeleteVolume => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_ConfirmDeleteVolume");

        // Clean Disk Dialog
        public string DiskMgmt_CleanDiskTitle => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_CleanDiskTitle");
        public string DiskMgmt_CleanDiskWarning => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_CleanDiskWarning");
        public string DiskMgmt_ConfirmCleanDisk => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_ConfirmCleanDisk");

        // CD-ROM Properties Dialog
        public string DiskMgmt_CDROMPropertiesTitle => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_CDROMPropertiesTitle");
        public string DiskMgmt_Name => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_Name");
        public string DiskMgmt_Manufacturer => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_Manufacturer");
        public string DiskMgmt_Media => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_Media");
        public string DiskMgmt_MediaLoaded => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_MediaLoaded");
        public string DiskMgmt_VolumeName => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_VolumeName");
        public string DiskMgmt_MediaType => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_MediaType");
        public string DiskMgmt_Size => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_Size");
        public string DiskMgmt_Hardware => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_Hardware");
        public string DiskMgmt_SCSIBus => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_SCSIBus");
        public string DiskMgmt_SCSIPort => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_SCSIPort");
        public string DiskMgmt_SCSITarget => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_SCSITarget");
        public string DiskMgmt_SCSILUN => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_SCSILUN");

        // Mark Partition Active Dialog
        public string DiskMgmt_MarkPartitionActiveTitle => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_MarkPartitionActiveTitle");
        public string DiskMgmt_MarkActive => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_MarkActive");
        public string DiskMgmt_MarkActiveWarning => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_MarkActiveWarning");
        public string DiskMgmt_MarkActiveConfirm => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_MarkActiveConfirm");

        // Mount to Folder Dialog
        public string DiskMgmt_MountToFolderTitle => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_MountToFolderTitle");
        public string DiskMgmt_Mount => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_Mount");
        public string DiskMgmt_MountPoint => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_MountPoint");
        public string DiskMgmt_MountPointPlaceholder => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_MountPointPlaceholder");
        public string DiskMgmt_MountInfo => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_MountInfo");

        // Remove Drive Letter Dialog
        public string DiskMgmt_RemoveDriveLetterTitle => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_RemoveDriveLetterTitle");
        public string DiskMgmt_RemoveDriveLetterConfirm => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_RemoveDriveLetterConfirm");

        // Common
        public string DiskMgmt_Warning => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_Warning");

        // Additional labels and status strings
        public string DiskMgmt_Usage => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_Usage");
        public string DiskMgmt_Health => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_Health");
        public string DiskMgmt_Bootable => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_Bootable");
        public string DiskMgmt_Primary => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_Primary");
        public string DiskMgmt_Advanced => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_Advanced");
        public string DiskMgmt_DeviceId => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_DeviceId");
        public string DiskMgmt_StartingOffset => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_StartingOffset");
        public string DiskMgmt_BlockSize => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_BlockSize");
        public string DiskMgmt_VolumeSerial => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_VolumeSerial");
        public string DiskMgmt_SerialNumber => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_SerialNumber");
        public string DiskMgmt_Partitions => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_Partitions");
        public string DiskMgmt_Firmware => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_Firmware");
        public string DiskMgmt_BytesPerSector => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_BytesPerSector");
        public string DiskMgmt_TotalSectors => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_TotalSectors");
        public string DiskMgmt_TotalCylinders => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_TotalCylinders");
        public string DiskMgmt_TotalHeads => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_TotalHeads");
        public string DiskMgmt_VirtualDisk => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_VirtualDisk");
        public string DiskMgmt_IsVirtual => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_IsVirtual");
        public string DiskMgmt_Yes => GetResource(ResourceFileNames.DiskManagement, "DiskMgmt_Yes");
    }
}
