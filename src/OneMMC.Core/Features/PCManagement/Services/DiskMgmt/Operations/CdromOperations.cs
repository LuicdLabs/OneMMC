using System;
using System.Linq;
using System.Runtime.InteropServices;
using OneMMC.Core.Features.PCManagement.Services.DiskMgmt.Common;
using OneMMC.Core.Localization;
using Windows.Win32.Storage.FileSystem;
using Win32PInvoke = Windows.Win32.PInvoke;

namespace OneMMC.Core.Features.PCManagement.Services.DiskMgmt
{
    internal static class CdromOperations
    {
        public static OperationResult Eject(
            string driveLetter,
            Action<string, string, uint?, uint?, string?> logDebug,
            Action<string, Exception, uint?, uint?, string?> logError)
        {
            return ExecuteIoctl(
                driveLetter,
                DiskManagementConstants.IOCTL_STORAGE_EJECT_MEDIA,
                DiskMgmtText.Get(DiskMgmtKeys.OkCdromEjected),
                DiskMgmtText.Get(DiskMgmtKeys.Eject),
                "EjectCDROM",
                logDebug,
                logError);
        }

        public static OperationResult Load(
            string driveLetter,
            Action<string, string, uint?, uint?, string?> logDebug,
            Action<string, Exception, uint?, uint?, string?> logError)
        {
            return ExecuteIoctl(
                driveLetter,
                DiskManagementConstants.IOCTL_STORAGE_LOAD_MEDIA,
                DiskMgmtText.Get(DiskMgmtKeys.OkCdromLoaded),
                DiskMgmtText.Get(DiskMgmtKeys.ActionLoad),
                "LoadCDROM",
                logDebug,
                logError);
        }

        public static OperationResult ChangeDriveLetter(
            string currentDriveLetter,
            string newDriveLetter,
            Action<string, string, uint?, uint?, string?> logDebug,
            Action<string, Exception, uint?, uint?, string?> logError)
        {
            if (string.IsNullOrEmpty(currentDriveLetter) || string.IsNullOrEmpty(newDriveLetter))
            {
                return OperationResult.Fail(ErrorMessages.DriveLetterEmpty);
            }

            var currentNormalized = currentDriveLetter.TrimEnd(':').ToUpper();
            var newNormalized = newDriveLetter.TrimEnd(':').ToUpper();

            if (currentNormalized == newNormalized)
            {
                return OperationResult.Fail(DiskMgmtText.Get(DiskMgmtKeys.ErrDriveLetterSameAsCurrent));
            }

            if (System.IO.DriveInfo.GetDrives().Any(d => d.Name.TrimEnd('\\').Equals(newNormalized + ":", StringComparison.OrdinalIgnoreCase)))
            {
                return OperationResult.Fail(DiskMgmtText.Format(DiskMgmtKeys.ErrDriveLetterInUseFormat, newNormalized));
            }

            try
            {
                char[] volumeName = new char[50];
                if (!Win32PInvoke.GetVolumeNameForVolumeMountPoint(currentNormalized + ":\\", volumeName))
                {
                    var lastErr = Marshal.GetLastWin32Error();
                    return OperationResult.Fail(DiskMgmtText.Format(DiskMgmtKeys.ErrVolumeInfoWin32Format, lastErr));
                }

                string volumeGuid = new string(volumeName).TrimEnd('\0');

                if (!Win32PInvoke.DeleteVolumeMountPoint(currentNormalized + ":\\"))
                {
                    var lastErr = Marshal.GetLastWin32Error();
                    return OperationResult.Fail(DiskMgmtText.Format(DiskMgmtKeys.ErrRemoveOldLetterWin32Format, lastErr));
                }

                if (!Win32PInvoke.SetVolumeMountPoint(newNormalized + ":\\", volumeGuid))
                {
                    var setError = Marshal.GetLastWin32Error();
                    Win32PInvoke.SetVolumeMountPoint(currentNormalized + ":\\", volumeGuid);
                    return OperationResult.Fail(DiskMgmtText.Format(DiskMgmtKeys.ErrSetNewLetterWin32Format, setError));
                }

                logDebug("ChangeCDROMDriveLetter", $"Changed from {currentNormalized}: to {newNormalized}:", null, null, null);
                return OperationResult.Ok(DiskMgmtText.Format(DiskMgmtKeys.OkLetterChangedFormat, currentNormalized, newNormalized));
            }
            catch (Exception ex)
            {
                logError("ChangeCDROMDriveLetter", ex, null, null, null);
                return OperationResult.Fail(DiskMgmtText.Format(DiskMgmtKeys.ErrFailedFormat, ex.Message));
            }
        }

        public static OperationResult RemoveDriveLetter(
            string driveLetter,
            Action<string, string, uint?, uint?, string?> logDebug,
            Action<string, Exception, uint?, uint?, string?> logError)
        {
            if (string.IsNullOrEmpty(driveLetter))
            {
                return OperationResult.Fail(ErrorMessages.DriveLetterEmpty);
            }

            var normalizedLetter = driveLetter.TrimEnd(':').ToUpper();

            try
            {
                if (!Win32PInvoke.DeleteVolumeMountPoint(normalizedLetter + ":\\"))
                {
                    var lastErr = Marshal.GetLastWin32Error();
                    return OperationResult.Fail(DiskMgmtText.Format(DiskMgmtKeys.ErrRemovalWin32Format, lastErr));
                }

                logDebug("RemoveCDROMDriveLetter", $"Removed drive letter {normalizedLetter}:", null, null, null);
                return OperationResult.Ok(DiskMgmtText.Format(DiskMgmtKeys.OkLetterRemovedFormat, normalizedLetter));
            }
            catch (Exception ex)
            {
                logError("RemoveCDROMDriveLetter", ex, null, null, null);
                return OperationResult.Fail(DiskMgmtText.Format(DiskMgmtKeys.ErrFailedFormat, ex.Message));
            }
        }

        public static OperationResult AssignDriveLetter(
            string currentDriveLetter,
            string newDriveLetter,
            Action<string, string, uint?, uint?, string?> logDebug,
            Action<string, Exception, uint?, uint?, string?> logError)
        {
            if (string.IsNullOrEmpty(currentDriveLetter) || string.IsNullOrEmpty(newDriveLetter))
            {
                return OperationResult.Fail(ErrorMessages.DriveLetterEmpty);
            }

            var currentNormalized = currentDriveLetter.TrimEnd(':').ToUpper();
            var newNormalized = newDriveLetter.TrimEnd(':').ToUpper();

            if (System.IO.DriveInfo.GetDrives().Any(d => d.Name.TrimEnd('\\').Equals(newNormalized + ":", StringComparison.OrdinalIgnoreCase)))
            {
                return OperationResult.Fail(DiskMgmtText.Format(DiskMgmtKeys.ErrDriveLetterInUseFormat, newNormalized));
            }

            try
            {
                char[] volumeName = new char[50];
                if (!Win32PInvoke.GetVolumeNameForVolumeMountPoint(currentNormalized + ":\\", volumeName))
                {
                    var lastErr = Marshal.GetLastWin32Error();
                    return OperationResult.Fail(DiskMgmtText.Format(DiskMgmtKeys.ErrVolumeInfoWin32Format, lastErr));
                }

                string volumeGuid = new string(volumeName).TrimEnd('\0');

                if (!Win32PInvoke.SetVolumeMountPoint(newNormalized + ":\\", volumeGuid))
                {
                    var setError = Marshal.GetLastWin32Error();
                    return OperationResult.Fail(DiskMgmtText.Format(DiskMgmtKeys.ErrAssignLetterWin32Format, setError));
                }

                logDebug("AssignCDROMDriveLetter", $"Assigned drive letter {newNormalized}:", null, null, null);
                return OperationResult.Ok(DiskMgmtText.Format(DiskMgmtKeys.OkLetterAssignedFormat, newNormalized));
            }
            catch (Exception ex)
            {
                logError("AssignCDROMDriveLetter", ex, null, null, null);
                return OperationResult.Fail(DiskMgmtText.Format(DiskMgmtKeys.ErrFailedFormat, ex.Message));
            }
        }

        private static OperationResult ExecuteIoctl(
            string driveLetter,
            uint ioctlCode,
            string successMessage,
            string actionName,
            string operationName,
            Action<string, string, uint?, uint?, string?> logDebug,
            Action<string, Exception, uint?, uint?, string?> logError)
        {
            if (string.IsNullOrEmpty(driveLetter))
            {
                return OperationResult.Fail(ErrorMessages.DriveLetterEmpty);
            }

            try
            {
                if (!driveLetter.EndsWith(":"))
                {
                    driveLetter += ":";
                }

                var devicePath = $"\\\\.\\{driveLetter}";
                using var handle = Win32PInvoke.CreateFile(
                    devicePath,
                    DiskManagementConstants.GENERIC_READ,
                    (FILE_SHARE_MODE)(DiskManagementConstants.FILE_SHARE_READ | DiskManagementConstants.FILE_SHARE_WRITE),
                    null,
                    FILE_CREATION_DISPOSITION.OPEN_EXISTING,
                    0,
                    null);

                if (handle.IsInvalid)
                {
                    var error = Marshal.GetLastWin32Error();
                    logDebug(operationName, $"Failed to open device {devicePath}: Win32 Error {error}", null, null, null);
                    return OperationResult.Fail(DiskMgmtText.Format(DiskMgmtKeys.ErrOpenDeviceWin32Format, error));
                }

                bool result;
                unsafe
                {
                    result = Win32PInvoke.DeviceIoControl(
                        handle,
                        ioctlCode,
                        ReadOnlySpan<byte>.Empty,
                        Span<byte>.Empty,
                        out _,
                        null);
                }

                if (result)
                {
                    logDebug(operationName, $"Successfully completed {actionName}: {driveLetter}", null, null, null);
                    return OperationResult.Ok(successMessage);
                }

                var deviceError = Marshal.GetLastWin32Error();
                return OperationResult.Fail(DiskMgmtText.Format(DiskMgmtKeys.ErrActionWin32Format, actionName, deviceError));
            }
            catch (Exception ex)
            {
                logError(operationName, ex, null, null, null);
                return OperationResult.Fail(DiskMgmtText.Format(DiskMgmtKeys.ErrOperationFailedFormat, operationName, ex.Message));
            }
        }
    }
}
