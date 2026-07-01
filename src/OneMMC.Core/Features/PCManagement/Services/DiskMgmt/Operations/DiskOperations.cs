using System;
using System.Management;
using System.Runtime.InteropServices;
using OneMMC.Core.Features.PCManagement.Services.DiskMgmt.Common;
using OneMMC.Core.Infrastructure.Wmi;

namespace OneMMC.Core.Features.PCManagement.Services.DiskMgmt
{
    internal sealed class DiskOperations
    {
        private readonly DiskManagementService _service;

        public DiskOperations(DiskManagementService service)
        {
            _service = service;
        }

        public OperationResult InitializeDisk(uint diskIndex, bool useGpt = true)
        {
            var safetyCheck = _service.ValidateDiskOperationSafety(diskIndex);
            if (safetyCheck != null)
                return OperationResult.Fail(safetyCheck);

            return ExecuteWmiOperation(nameof(InitializeDisk), () =>
            {
                var scope = CreateConnectedScope();
                using var disk = GetDisk(scope, diskIndex);

                if (disk == null)
                    return OperationResult.Fail(ErrorMessages.DiskNotFound);

                var inParams = disk.GetMethodParameters("Initialize");
                inParams["PartitionStyle"] = useGpt
                    ? DiskManagementConstants.PARTITION_STYLE_GPT
                    : DiskManagementConstants.PARTITION_STYLE_MBR;

                var outParams = disk.InvokeMethod("Initialize", inParams, null);
                var rv = Convert.ToUInt32(outParams["ReturnValue"]);

                return rv == DiskManagementConstants.WMI_SUCCESS
                    ? OperationResult.Ok($"Disk successfully initialized as {(useGpt ? "GPT" : "MBR")} format.")
                    : OperationResult.Fail(
                        $"Initialization failed. Error code: {rv} - {ErrorMessages.GetMsftErrorMessage(rv)}", rv);
            }, diskIndex: diskIndex);
        }

        public OperationResult SetDiskOnlineOffline(uint diskIndex, bool online)
        {
            return ExecuteWmiOperation(nameof(SetDiskOnlineOffline), () =>
            {
                var scope = CreateConnectedScope();
                using var disk = GetDisk(scope, diskIndex);

                if (disk == null)
                    return OperationResult.Fail(ErrorMessages.DiskNotFound);

                var methodName = online ? "Online" : "Offline";
                var outParams = disk.InvokeMethod(methodName, null, null);
                var rv = Convert.ToUInt32(outParams["ReturnValue"]);

                return rv == DiskManagementConstants.WMI_SUCCESS
                    ? OperationResult.Ok(online ? "Disk is now online." : "Disk is now offline.")
                    : OperationResult.Fail(
                        $"Operation failed. Error code: {rv} - {ErrorMessages.GetMsftErrorMessage(rv)}", rv);
            }, diskIndex: diskIndex);
        }

        public bool IsDiskOnline(uint diskIndex)
        {
            try
            {
                var scope = CreateConnectedScope();
                var query = new ObjectQuery($"SELECT IsOffline FROM MSFT_Disk WHERE Number = {diskIndex}");
                using var searcher = new ManagementObjectSearcher(scope, query);

                foreach (ManagementObject disk in searcher.GetAndDispose())
                {
                    using (disk)
                        return !GetWmiPropertySafe<bool>(disk, "IsOffline");
                }
            }
            catch (Exception ex)
            {
                DiagnosticLogger.LogDebug($"[{nameof(IsDiskOnline)}] Query failed: {ex.Message}", diskIndex: diskIndex);
            }

            return true; // assume online on failure
        }

        public OperationResult SetDiskReadOnly(uint diskIndex, bool readOnly)
        {
            return ExecuteWmiOperation(nameof(SetDiskReadOnly), () =>
            {
                var scope = CreateConnectedScope();
                using var disk = GetDisk(scope, diskIndex);

                if (disk == null)
                    return OperationResult.Fail(ErrorMessages.DiskNotFound);

                var inParams = disk.GetMethodParameters("SetAttributes");
                inParams["IsReadOnly"] = readOnly;

                var outParams = disk.InvokeMethod("SetAttributes", inParams, null);
                var rv = Convert.ToUInt32(outParams["ReturnValue"]);

                return rv == DiskManagementConstants.WMI_SUCCESS
                    ? OperationResult.Ok(readOnly ? "Disk set to read-only." : "Disk set to read-write.")
                    : OperationResult.Fail(
                        $"Operation failed. Error code: {rv} - {ErrorMessages.GetMsftErrorMessage(rv)}", rv);
            }, diskIndex: diskIndex);
        }

        public bool IsDiskReadOnly(uint diskIndex)
        {
            try
            {
                var scope = CreateConnectedScope();
                var query = new ObjectQuery($"SELECT IsReadOnly FROM MSFT_Disk WHERE Number = {diskIndex}");
                using var searcher = new ManagementObjectSearcher(scope, query);

                foreach (ManagementObject disk in searcher.GetAndDispose())
                {
                    using (disk)
                        return GetWmiPropertySafe<bool>(disk, "IsReadOnly");
                }
            }
            catch (Exception ex)
            {
                DiagnosticLogger.LogDebug($"[{nameof(IsDiskReadOnly)}] Query failed: {ex.Message}", diskIndex: diskIndex);
            }

            return false;
        }

        public OperationResult CleanDisk(uint diskIndex)
        {
            if (_service.IsSystemDisk(diskIndex))
                return OperationResult.Fail(ErrorMessages.SystemDiskClean);

            if (_service.DiskContainsCriticalPartitions(diskIndex))
                return OperationResult.Fail(ErrorMessages.CriticalPartitionsOnDisk);

            return ExecuteWmiOperation(nameof(CleanDisk), () =>
            {
                var scope = CreateConnectedScope();
                using var disk = GetDisk(scope, diskIndex);

                if (disk == null)
                    return OperationResult.Fail(ErrorMessages.DiskNotFound);

                var inParams = disk.GetMethodParameters("Clear");
                inParams["RemoveData"] = true;
                inParams["RemoveOEM"] = true;

                var outParams = disk.InvokeMethod("Clear", inParams, null);
                var rv = Convert.ToUInt32(outParams["ReturnValue"]);

                return rv == DiskManagementConstants.WMI_SUCCESS
                    ? OperationResult.Ok("Disk cleaned, all partitions removed.")
                    : OperationResult.Fail(
                        $"Clean failed. Error code: {rv} - {ErrorMessages.GetMsftErrorMessage(rv)}", rv);
            }, diskIndex: diskIndex);
        }

        public bool DiskNeedsInitialization(uint diskIndex)
        {
            try
            {
                var scope = CreateConnectedScope();
                var query = new ObjectQuery($"SELECT PartitionStyle FROM MSFT_Disk WHERE Number = {diskIndex}");
                using var searcher = new ManagementObjectSearcher(scope, query);

                foreach (ManagementObject disk in searcher.GetAndDispose())
                {
                    using (disk)
                    {
                        var style = GetWmiPropertySafe<ushort>(disk, "PartitionStyle");
                        return style == DiskManagementConstants.PARTITION_STYLE_RAW;
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticLogger.LogDebug($"[{nameof(DiskNeedsInitialization)}] Query failed: {ex.Message}", diskIndex: diskIndex);
            }

            return false;
        }

        private static ManagementScope CreateConnectedScope()
        {
            var scope = new ManagementScope(DiskManagementConstants.StorageWmiScope);
            scope.Connect();
            return scope;
        }

        private static ManagementObject? GetDisk(ManagementScope scope, uint diskIndex)
        {
            var query = new ObjectQuery($"SELECT * FROM MSFT_Disk WHERE Number = {diskIndex}");
            using var searcher = new ManagementObjectSearcher(scope, query);
            using var collection = searcher.Get();
            foreach (ManagementObject disk in collection)
            {
                return disk;
            }

            return null;
        }

        private static T GetWmiPropertySafe<T>(ManagementBaseObject obj, string propertyName, T defaultValue = default!)
        {
            try
            {
                var value = obj[propertyName];
                if (value == null) return defaultValue;

                var targetType = typeof(T);
                var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

                if (underlyingType == typeof(string))
                    return (T)(object)(value.ToString()?.Trim() ?? string.Empty);

                return (T)Convert.ChangeType(value, underlyingType);
            }
            catch
            {
                return defaultValue;
            }
        }

        private static string SafeString(object? value, string defaultValue = "")
        {
            try
            {
                return value?.ToString()?.Trim() ?? defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        private static OperationResult ExecuteWmiOperation(
            string operationName,
            Func<OperationResult> operation,
            uint? diskIndex = null)
        {
            DiagnosticLogger.LogOperationStart(operationName, diskIndex, null, null);

            try
            {
                var result = operation();

                if (result.Success)
                {
                    DiagnosticLogger.LogOperationSuccess(operationName, result.Message, diskIndex, null, null);
                }
                else
                {
                    DiagnosticLogger.LogWarning($"{operationName} [FAIL]: {result.Message}", diskIndex, null, null);
                }

                return result;
            }
            catch (ManagementException mex)
            {
                DiagnosticLogger.LogOperationError(operationName, mex, diskIndex, null, null, $"WMI Error Code: {mex.ErrorCode}");
                return OperationResult.Fail($"{operationName} failed: {mex.Message}");
            }
            catch (COMException comEx)
            {
                DiagnosticLogger.LogOperationError(operationName, comEx, diskIndex, null, null, $"COM HRESULT: 0x{comEx.HResult:X8}");
                return OperationResult.Fail($"{operationName} failed with COM error: {comEx.Message} (0x{comEx.HResult:X8})");
            }
            catch (UnauthorizedAccessException)
            {
                return OperationResult.AccessDenied(operationName);
            }
            catch (Exception ex)
            {
                DiagnosticLogger.LogOperationError(operationName, ex, diskIndex, null, null);
                return OperationResult.Fail($"Error during {operationName}: {ex.Message}");
            }
        }

    }
}


