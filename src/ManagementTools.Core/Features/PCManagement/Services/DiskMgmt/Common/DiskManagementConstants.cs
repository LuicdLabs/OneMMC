using System;
using System.Collections.Generic;

namespace ManagementTools.Core.Features.PCManagement.Services.DiskMgmt.Common
{
    /// <summary>
    /// Constants used throughout the disk management service
    /// </summary>
    public static class DiskManagementConstants
    {
        #region WMI Scopes
        public const string StorageWmiScope = @"\\.\root\Microsoft\Windows\Storage";
        public const string CimV2WmiScope = @"\\.\root\CIMV2";
        #endregion

        #region Size Constants
        public const ulong BYTES_PER_KB = 1024;
        public const ulong BYTES_PER_MB = 1024 * 1024;
        public const ulong BYTES_PER_GB = 1024 * 1024 * 1024;
        public const ulong ALIGNMENT_1MB = 1024 * 1024;
        public const ulong MIN_UNALLOCATED_GAP = 8 * 1024 * 1024; // 8 MB
        public const ulong GPT_BACKUP_RESERVE = 1024 * 1024; // 1 MB
        #endregion

        #region Timing Constants
        public const int MAX_VOLUME_WAIT_RETRIES = 5;
        public const int VOLUME_WAIT_DELAY_MS = 1000;
        public const int PARTITION_CREATE_DELAY_MS = 2000;
        #endregion

        #region WMI Return Codes
        public const uint WMI_SUCCESS = 0;
        public const uint WMI_NOT_SUPPORTED = 4;
        public const uint WMI_ACCESS_DENIED = 5;
        public const uint WMI_METHOD_NOT_SUPPORTED = 40004;
        public const uint WMI_DISK_NOT_INITIALIZED = 41000;
        public const uint WMI_DISK_IN_USE = 41002;
        public const uint WMI_CANNOT_CONVERT_WITH_DATA = 41013;
        public const uint WMI_ACCESS_PATH_IN_USE = 42002;
        public const uint WMI_CANNOT_DELETE_SYSTEM_PARTITION = 42008;
        #endregion

        #region Partition Style
        public const ushort PARTITION_STYLE_RAW = 0;
        public const ushort PARTITION_STYLE_MBR = 1;
        public const ushort PARTITION_STYLE_GPT = 2;
        #endregion

        #region GPT Partition Type GUIDs
        public static readonly Guid EFI_SYSTEM_PARTITION_GUID = new("C12A7328-F81F-11D2-BA4B-00A0C93EC93B");
        public static readonly Guid MICROSOFT_RESERVED_GUID = new("E3C9E316-0B5C-4DB8-817D-F92DF00215AE");
        public static readonly Guid BASIC_DATA_PARTITION_GUID = new("EBD0A0A2-B9E5-4433-87C0-68B6B72699C7");
        public static readonly Guid WINDOWS_RECOVERY_GUID = new("DE94BBA4-06D1-4D40-A16A-BFD50179D6AC");
        public static readonly Guid WINDOWS_RE_GUID = new("8DA63339-0007-60C0-C436-083AC8230908");
        public static readonly Guid OEM_RECOVERY_GUID = new("F0FD8DC9-0438-4741-8E12-7E0C412A9930");
        public static readonly Guid LDM_METADATA_GUID = new("5808C8AA-7E8F-42E0-85D2-E1E90434CFB3");
        public static readonly Guid LDM_DATA_GUID = new("AF9B60A0-1431-4F62-BC68-3311714A69AD");
        public static readonly Guid BIOS_BOOT_PARTITION_GUID = new("21686148-6449-6E6F-744E-656564454649");

        public static readonly HashSet<Guid> NonResizablePartitionTypes = new()
        {
            MICROSOFT_RESERVED_GUID,
            EFI_SYSTEM_PARTITION_GUID,
            WINDOWS_RECOVERY_GUID,
            WINDOWS_RE_GUID
        };

        public static readonly HashSet<Guid> CriticalSystemPartitionTypes = new()
        {
            EFI_SYSTEM_PARTITION_GUID,
            MICROSOFT_RESERVED_GUID,
            WINDOWS_RECOVERY_GUID,
            WINDOWS_RE_GUID,
            BIOS_BOOT_PARTITION_GUID
        };
        #endregion

        #region Device I/O Control Codes
        public const uint IOCTL_STORAGE_EJECT_MEDIA = 0x2D4808;
        public const uint IOCTL_STORAGE_LOAD_MEDIA = 0x2D480C;
        public const uint IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS = 0x560000;
        #endregion

        #region File Access Constants
        public const uint GENERIC_READ = 0x80000000;
        public const uint GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 0x00000001;
        public const uint FILE_SHARE_WRITE = 0x00000002;
        public const uint OPEN_EXISTING = 3;
        #endregion

        #region Estimation Ratios
        public const double SHRINKABLE_SPACE_ESTIMATE_RATIO = 0.8;
        #endregion
    }
}


