using System;
using System.IO;
using ManagementTools.Core.Features.PCManagement.Services.DiskMgmt.Common;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.Vhd;
using Win32PInvoke = Windows.Win32.PInvoke;

namespace ManagementTools.Core.Features.PCManagement.Services.DiskMgmt
{
    internal static class VirtualDiskOperations
    {
        private static readonly Guid VirtualStorageTypeVendorMicrosoft =
            new("EC984AEC-A0F9-47E9-901F-71415A66345B");

        private const uint VirtualStorageTypeDeviceVhd = 2;
        private const uint VirtualStorageTypeDeviceVhdx = 3;

        public static OperationResult Create(
            string path,
            ulong sizeInBytes,
            bool isVhdx,
            bool isDynamic,
            Action<string, string, uint?, uint?, string?> logDebug,
            Action<string, Exception, uint?, uint?, string?> logError)
        {
            if (string.IsNullOrEmpty(path))
            {
                return OperationResult.Fail("Path cannot be empty.");
            }

            if (sizeInBytes < DiskManagementConstants.BYTES_PER_MB)
            {
                return OperationResult.Fail("Size must be at least 1 MB.");
            }

            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (File.Exists(path))
                {
                    return OperationResult.Fail("File already exists.");
                }

                var storageType = new VIRTUAL_STORAGE_TYPE
                {
                    DeviceId = isVhdx ? VirtualStorageTypeDeviceVhdx : VirtualStorageTypeDeviceVhd,
                    VendorId = VirtualStorageTypeVendorMicrosoft
                };

                var flags = isDynamic
                    ? CREATE_VIRTUAL_DISK_FLAG.CREATE_VIRTUAL_DISK_FLAG_NONE
                    : CREATE_VIRTUAL_DISK_FLAG.CREATE_VIRTUAL_DISK_FLAG_FULL_PHYSICAL_ALLOCATION;

                var parameters = new CREATE_VIRTUAL_DISK_PARAMETERS
                {
                    Version = CREATE_VIRTUAL_DISK_VERSION.CREATE_VIRTUAL_DISK_VERSION_1
                };
                parameters.Anonymous.Version1.UniqueId = Guid.Empty;
                parameters.Anonymous.Version1.MaximumSize = sizeInBytes;
                parameters.Anonymous.Version1.BlockSizeInBytes = 0;
                parameters.Anonymous.Version1.SectorSizeInBytes = 512;

                WIN32_ERROR result;
                Microsoft.Win32.SafeHandles.SafeFileHandle handle;
                unsafe
                {
                    result = Win32PInvoke.CreateVirtualDisk(
                        in storageType,
                        path,
                        VIRTUAL_DISK_ACCESS_MASK.VIRTUAL_DISK_ACCESS_ALL,
                        default,
                        flags,
                        0,
                        in parameters,
                        null,
                        out handle);
                }

                using (handle)
                {
                    if (result != WIN32_ERROR.ERROR_SUCCESS)
                    {
                        int errorCode = (int)result;
                        return errorCode is 5 or 1314
                            ? OperationResult.AccessDenied("CreateVHD")
                            : OperationResult.Fail(ErrorMessages.GetVhdErrorMessage(errorCode));
                    }

                    logDebug("CreateVHD", $"Virtual hard disk created: {path}", null, null, null);
                    return OperationResult.Ok("Virtual hard disk created successfully.");
                }
            }
            catch (Exception ex)
            {
                logError("CreateVHD", ex, null, null, null);
                return OperationResult.Fail($"CreateVHD failed: {ex.Message}");
            }
        }

        public static OperationResult Attach(
            string path,
            bool readOnly,
            Action<string, string, uint?, uint?, string?> logDebug,
            Action<string, Exception, uint?, uint?, string?> logError)
        {
            if (string.IsNullOrEmpty(path))
            {
                return OperationResult.Fail("Path cannot be empty.");
            }

            if (!File.Exists(path))
            {
                return OperationResult.Fail($"File not found: {path}");
            }

            try
            {
                bool isVhdx = path.EndsWith(".vhdx", StringComparison.OrdinalIgnoreCase);

                var storageType = new VIRTUAL_STORAGE_TYPE
                {
                    DeviceId = isVhdx ? VirtualStorageTypeDeviceVhdx : VirtualStorageTypeDeviceVhd,
                    VendorId = VirtualStorageTypeVendorMicrosoft
                };

                var openParams = new OPEN_VIRTUAL_DISK_PARAMETERS
                {
                    Version = OPEN_VIRTUAL_DISK_VERSION.OPEN_VIRTUAL_DISK_VERSION_1
                };
                openParams.Anonymous.Version1.RWDepth = 1;

                WIN32_ERROR openResult = Win32PInvoke.OpenVirtualDisk(
                    in storageType,
                    path,
                    readOnly ? VIRTUAL_DISK_ACCESS_MASK.VIRTUAL_DISK_ACCESS_ATTACH_RO : VIRTUAL_DISK_ACCESS_MASK.VIRTUAL_DISK_ACCESS_ALL,
                    OPEN_VIRTUAL_DISK_FLAG.OPEN_VIRTUAL_DISK_FLAG_NONE,
                    openParams,
                    out var handle);

                using (handle)
                {
                    if (openResult != WIN32_ERROR.ERROR_SUCCESS)
                    {
                        int errorCode = (int)openResult;
                        return errorCode is 5 or 1314
                            ? OperationResult.AccessDenied("AttachVHD")
                            : OperationResult.Fail(ErrorMessages.GetVhdErrorMessage(errorCode));
                    }

                    var attachParams = new ATTACH_VIRTUAL_DISK_PARAMETERS
                    {
                        Version = ATTACH_VIRTUAL_DISK_VERSION.ATTACH_VIRTUAL_DISK_VERSION_1
                    };

                    var attachFlags = readOnly
                        ? ATTACH_VIRTUAL_DISK_FLAG.ATTACH_VIRTUAL_DISK_FLAG_READ_ONLY
                        : ATTACH_VIRTUAL_DISK_FLAG.ATTACH_VIRTUAL_DISK_FLAG_NONE;
                    attachFlags |= ATTACH_VIRTUAL_DISK_FLAG.ATTACH_VIRTUAL_DISK_FLAG_PERMANENT_LIFETIME;

                    WIN32_ERROR attachResult;
                    unsafe
                    {
                        attachResult = Win32PInvoke.AttachVirtualDisk(
                            handle,
                            default,
                            attachFlags,
                            0,
                            attachParams,
                            null);
                    }

                    if (attachResult != WIN32_ERROR.ERROR_SUCCESS)
                    {
                        int errorCode = (int)attachResult;
                        return errorCode is 5 or 1314
                            ? OperationResult.AccessDenied("AttachVHD")
                            : OperationResult.Fail(ErrorMessages.GetVhdErrorMessage(errorCode));
                    }

                    logDebug("AttachVHD", $"Virtual hard disk attached: {path}", null, null, null);
                    return OperationResult.Ok("Virtual hard disk attached successfully.");
                }
            }
            catch (Exception ex)
            {
                logError("AttachVHD", ex, null, null, null);
                return OperationResult.Fail($"AttachVHD failed: {ex.Message}");
            }
        }

        public static OperationResult Detach(
            string path,
            Action<string, string, uint?, uint?, string?> logDebug,
            Action<string, Exception, uint?, uint?, string?> logError)
        {
            if (string.IsNullOrEmpty(path))
            {
                return OperationResult.Fail("Path cannot be empty.");
            }

            if (!File.Exists(path))
            {
                return OperationResult.Fail($"File not found: {path}");
            }

            try
            {
                bool isVhdx = path.EndsWith(".vhdx", StringComparison.OrdinalIgnoreCase);

                var storageType = new VIRTUAL_STORAGE_TYPE
                {
                    DeviceId = isVhdx ? VirtualStorageTypeDeviceVhdx : VirtualStorageTypeDeviceVhd,
                    VendorId = VirtualStorageTypeVendorMicrosoft
                };

                var openParams = new OPEN_VIRTUAL_DISK_PARAMETERS
                {
                    Version = OPEN_VIRTUAL_DISK_VERSION.OPEN_VIRTUAL_DISK_VERSION_1
                };
                openParams.Anonymous.Version1.RWDepth = 1;

                WIN32_ERROR openResult = Win32PInvoke.OpenVirtualDisk(
                    in storageType,
                    path,
                    VIRTUAL_DISK_ACCESS_MASK.VIRTUAL_DISK_ACCESS_DETACH,
                    OPEN_VIRTUAL_DISK_FLAG.OPEN_VIRTUAL_DISK_FLAG_NONE,
                    openParams,
                    out var handle);

                using (handle)
                {
                    if (openResult != WIN32_ERROR.ERROR_SUCCESS)
                    {
                        int errorCode = (int)openResult;
                        return errorCode is 5 or 1314
                            ? OperationResult.AccessDenied("DetachVHD")
                            : OperationResult.Fail(ErrorMessages.GetVhdErrorMessage(errorCode));
                    }

                    WIN32_ERROR detachResult = Win32PInvoke.DetachVirtualDisk(
                        handle,
                        DETACH_VIRTUAL_DISK_FLAG.DETACH_VIRTUAL_DISK_FLAG_NONE,
                        0);

                    if (detachResult != WIN32_ERROR.ERROR_SUCCESS)
                    {
                        int errorCode = (int)detachResult;
                        return errorCode is 5 or 1314
                            ? OperationResult.AccessDenied("DetachVHD")
                            : OperationResult.Fail(ErrorMessages.GetVhdErrorMessage(errorCode));
                    }

                    logDebug("DetachVHD", $"Virtual hard disk detached: {path}", null, null, null);
                    return OperationResult.Ok("Virtual hard disk detached successfully.");
                }
            }
            catch (Exception ex)
            {
                logError("DetachVHD", ex, null, null, null);
                return OperationResult.Fail($"DetachVHD failed: {ex.Message}");
            }
        }
    }
}
