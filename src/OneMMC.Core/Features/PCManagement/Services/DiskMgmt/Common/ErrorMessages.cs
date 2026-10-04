using System.Globalization;
using OneMMC.Core.Localization;

namespace OneMMC.Core.Features.PCManagement.Services.DiskMgmt.Common
{
    /// <summary>
    /// Localized text lookup for disk management operations (DiskManagement.resw).
    /// </summary>
    internal static class DiskMgmtText
    {
        /// <summary>Gets the localized string for <paramref name="key"/>.</summary>
        public static string Get(string key) =>
            LocalizationProvider.Current.GetString(ResourceFileNames.DiskManagement, key);

        /// <summary>Formats the localized string for <paramref name="key"/> with <paramref name="args"/>.</summary>
        public static string Format(string key, params object?[] args) =>
            string.Format(CultureInfo.CurrentCulture, Get(key), args);
    }

    /// <summary>
    /// Centralized, localized error messages for disk management operations.
    /// </summary>
    public static class ErrorMessages
    {
        private static string L(string key) => DiskMgmtText.Get(key);

        #region General Errors
        public static string DriveLetterEmpty => L(DiskMgmtKeys.ErrDriveLetterEmpty);
        public static string SizeRequired => L(DiskMgmtKeys.ErrSizeRequired);
        public static string DiskNotFound => L(DiskMgmtKeys.ErrDiskNotFound);
        public static string PartitionNotFound => L(DiskMgmtKeys.ErrPartitionNotFound);
        public static string VolumeNotFound => L(DiskMgmtKeys.ErrVolumeNotFound);
        public static string DriveLetterInUse => L(DiskMgmtKeys.ErrDriveLetterInUse);
        public static string DriveLetterSame => L(DiskMgmtKeys.ErrDriveLetterSame);
        public static string FolderNotExist => L(DiskMgmtKeys.ErrFolderNotExist);
        public static string DriveNotReady => L(DiskMgmtKeys.ErrDriveNotReady);
        public static string NoExtensionSpace => L(DiskMgmtKeys.ErrNoExtensionSpace);
        public static string NoAccessPath => L(DiskMgmtKeys.ErrNoAccessPath);
        #endregion

        #region System Protection Errors
        /// <summary>Format string; {0} is the system drive letter.</summary>
        public static string SystemDriveFormat => L(DiskMgmtKeys.ErrSystemDriveFormat);
        /// <summary>Format string; {0} is the system drive letter.</summary>
        public static string SystemDriveLetterRemoval => L(DiskMgmtKeys.ErrSystemDriveLetterRemoval);
        public static string SystemDiskClean => L(DiskMgmtKeys.ErrSystemDiskClean);
        public static string CriticalPartitionsOnDisk => L(DiskMgmtKeys.ErrCriticalPartitionsOnDisk);
        /// <summary>Format string; {0} is the system drive letter.</summary>
        public static string CannotDeleteSystemPartition => L(DiskMgmtKeys.ErrCannotDeleteSystemPartition);
        public static string CannotDeleteEfiPartition => L(DiskMgmtKeys.ErrCannotDeleteEfiPartition);
        public static string CannotDeleteRecoveryPartition => L(DiskMgmtKeys.ErrCannotDeleteRecoveryPartition);
        public static string CannotDeleteMsrPartition => L(DiskMgmtKeys.ErrCannotDeleteMsrPartition);
        public static string CriticalSystemPartition => L(DiskMgmtKeys.ErrCriticalSystemPartition);
        #endregion

        #region Operation Specific Errors
        public static string SpecialPartitionOperation => L(DiskMgmtKeys.ErrSpecialPartitionOperation);
        public static string PartitionNotSupportResize => L(DiskMgmtKeys.ErrPartitionNotSupportResize);
        public static string MbrOnly => L(DiskMgmtKeys.ErrMbrOnly);
        public static string DiskMustBeCleanedBeforeConversion => L(DiskMgmtKeys.ErrDiskMustBeCleanedBeforeConversion);
        public static string DiskAlreadyGpt => L(DiskMgmtKeys.ErrDiskAlreadyGpt);
        public static string DiskAlreadyMbr => L(DiskMgmtKeys.ErrDiskAlreadyMbr);
        public static string DiskAlreadyDynamic => L(DiskMgmtKeys.ErrDiskAlreadyDynamic);
        public static string DiskAlreadyBasic => L(DiskMgmtKeys.ErrDiskAlreadyBasic);
        #endregion

        #region VHD Errors
        public static string VhdPathEmpty => L(DiskMgmtKeys.ErrVhdPathEmpty);
        public static string VhdFileNotFound => L(DiskMgmtKeys.ErrVhdFileNotFound);
        public static string VhdFileAlreadyExists => L(DiskMgmtKeys.ErrVhdFileAlreadyExists);
        public static string VhdInsufficientPrivileges => LocalizationProvider.Current.GetString(ResourceFileNames.Common, CommonKeys.AccessDenied_Generic);
        public static string VhdAccessDenied => LocalizationProvider.Current.GetString(ResourceFileNames.Common, CommonKeys.AccessDenied_Generic);
        public static string VhdFileInUse => L(DiskMgmtKeys.ErrVhdFileInUse);
        #endregion

        #region Dynamic Disk
        public static string DynamicDiskDeprecated => L(DiskMgmtKeys.ErrDynamicDiskDeprecated);
        public static string DynamicDiskSystemWarning => L(DiskMgmtKeys.ErrDynamicDiskSystemWarning);
        #endregion

        /// <summary>
        /// Get MSFT_* API error message by error code
        /// </summary>
        public static string GetMsftErrorMessage(uint errorCode)
        {
            return errorCode switch
            {
                0 => L(DiskMgmtKeys.QuerySuccess),
                1 => L(DiskMgmtKeys.Msft1),
                2 => L(DiskMgmtKeys.Msft2),
                3 => L(DiskMgmtKeys.Msft3),
                4 => L(DiskMgmtKeys.Msft4),
                5 => L(DiskMgmtKeys.Msft5),
                6 => LocalizationProvider.Current.GetString(ResourceFileNames.Common, CommonKeys.AccessDenied_Generic),
                40001 => L(DiskMgmtKeys.AccessDenied_AdminRequired),
                40002 => L(DiskMgmtKeys.Msft40002),
                40003 => L(DiskMgmtKeys.Msft40003),
                40004 => L(DiskMgmtKeys.Msft40004),
                41000 => L(DiskMgmtKeys.Msft41000),
                41001 => L(DiskMgmtKeys.Msft41001),
                41002 => L(DiskMgmtKeys.Msft41002),
                41003 => L(DiskMgmtKeys.Msft41003),
                41010 => L(DiskMgmtKeys.Msft41010),
                41011 => L(DiskMgmtKeys.Msft41011),
                41012 => L(DiskMgmtKeys.Msft41012),
                41013 => L(DiskMgmtKeys.Msft41013),
                41014 => L(DiskMgmtKeys.Msft41014),
                41015 => L(DiskMgmtKeys.Msft41015),
                41016 => L(DiskMgmtKeys.Msft41012),
                41017 => L(DiskMgmtKeys.Msft41017),
                42002 => L(DiskMgmtKeys.Msft42002),
                42004 => L(DiskMgmtKeys.Msft42004),
                42007 => L(DiskMgmtKeys.Msft42007),
                42008 => L(DiskMgmtKeys.Msft42008),
                _ => DiskMgmtText.Format(DiskMgmtKeys.MsftUnknownFormat, errorCode)
            };
        }

        /// <summary>
        /// Get VHD API error message by error code
        /// </summary>
        public static string GetVhdErrorMessage(int errorCode)
        {
            return errorCode switch
            {
                0 => L(DiskMgmtKeys.QuerySuccess),
                2 => L(DiskMgmtKeys.Vhd2),
                3 => L(DiskMgmtKeys.Vhd3),
                5 => LocalizationProvider.Current.GetString(ResourceFileNames.Common, CommonKeys.AccessDenied_Generic),
                32 => L(DiskMgmtKeys.Vhd32),
                87 => L(DiskMgmtKeys.Vhd87),
                183 => L(DiskMgmtKeys.Vhd183),
                1314 => LocalizationProvider.Current.GetString(ResourceFileNames.Common, CommonKeys.AccessDenied_Generic),
                _ => DiskMgmtText.Format(DiskMgmtKeys.VhdUnknownFormat, errorCode)
            };
        }
    }
}
