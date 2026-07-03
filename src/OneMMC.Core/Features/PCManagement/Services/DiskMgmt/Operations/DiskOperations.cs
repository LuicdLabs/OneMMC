using System;
using System.Runtime.InteropServices;
using OneMMC.Core.Features.PCManagement.Services.DiskMgmt.Common;
using OneMMC.Core.Infrastructure.Wmi;
using WmiLight;

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
                using var connection = CreateConnection();
                using var disk = GetDisk(connection, diskIndex);

                if (disk == null)
                    return OperationResult.Fail(ErrorMessages.DiskNotFound);

                using WmiMethod initializeMethod = disk.GetMethod("Initialize");
                using WmiMethodParameters inParams = initializeMethod.CreateInParameters();
                inParams.SetUInt16Parameter("PartitionStyle", useGpt
                    ? DiskManagementConstants.PARTITION_STYLE_GPT
                    : DiskManagementConstants.PARTITION_STYLE_MBR);

                var rv = disk.ExecuteMethod<uint>(initializeMethod, inParams, out WmiMethodParameters outParams);
                outParams?.Dispose();

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
                using var connection = CreateConnection();
                using var disk = GetDisk(connection, diskIndex);

                if (disk == null)
                    return OperationResult.Fail(ErrorMessages.DiskNotFound);

                var methodName = online ? "Online" : "Offline";
                using WmiMethod method = disk.GetMethod(methodName);
                var rv = disk.ExecuteMethod<uint>(method, out WmiMethodParameters outParams);
                outParams?.Dispose();

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
                using var connection = CreateConnection();

                foreach (WmiObject disk in connection.CreateQuery($"SELECT IsOffline FROM MSFT_Disk WHERE Number = {diskIndex}"))
                {
                    using (disk)
                        return !disk.GetPropertySafe<bool>("IsOffline");
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
                using var connection = CreateConnection();
                using var disk = GetDisk(connection, diskIndex);

                if (disk == null)
                    return OperationResult.Fail(ErrorMessages.DiskNotFound);

                using WmiMethod setAttributesMethod = disk.GetMethod("SetAttributes");
                using WmiMethodParameters inParams = setAttributesMethod.CreateInParameters();
                inParams.SetPropertyValue("IsReadOnly", readOnly);

                var rv = disk.ExecuteMethod<uint>(setAttributesMethod, inParams, out WmiMethodParameters outParams);
                outParams?.Dispose();

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
                using var connection = CreateConnection();

                foreach (WmiObject disk in connection.CreateQuery($"SELECT IsReadOnly FROM MSFT_Disk WHERE Number = {diskIndex}"))
                {
                    using (disk)
                        return disk.GetPropertySafe<bool>("IsReadOnly");
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
                using var connection = CreateConnection();
                using var disk = GetDisk(connection, diskIndex);

                if (disk == null)
                    return OperationResult.Fail(ErrorMessages.DiskNotFound);

                using WmiMethod clearMethod = disk.GetMethod("Clear");
                using WmiMethodParameters inParams = clearMethod.CreateInParameters();
                inParams.SetPropertyValue("RemoveData", true);
                inParams.SetPropertyValue("RemoveOEM", true);

                var rv = disk.ExecuteMethod<uint>(clearMethod, inParams, out WmiMethodParameters outParams);
                outParams?.Dispose();

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
                using var connection = CreateConnection();

                foreach (WmiObject disk in connection.CreateQuery($"SELECT PartitionStyle FROM MSFT_Disk WHERE Number = {diskIndex}"))
                {
                    using (disk)
                    {
                        var style = disk.GetPropertySafe<ushort>("PartitionStyle");
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

        private static WmiConnection CreateConnection()
        {
            var connection = new WmiConnection(DiskManagementConstants.StorageWmiScope);
            connection.Open();
            return connection;
        }

        private static WmiObject? GetDisk(WmiConnection connection, uint diskIndex)
        {
            foreach (WmiObject disk in connection.CreateQuery($"SELECT * FROM MSFT_Disk WHERE Number = {diskIndex}"))
            {
                return disk;
            }

            return null;
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
            catch (WmiException wex)
            {
                DiagnosticLogger.LogOperationError(operationName, wex, diskIndex, null, null, $"WMI Error Code: 0x{wex.HResult:X8}");
                return OperationResult.Fail($"{operationName} failed: {wex.Message}");
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


