using ManagementTools.Core.Localization;

namespace ManagementTools.Core.Features.PCManagement.Services.DiskMgmt.Common
{
    /// <summary>
    /// Unified result type for disk management operations.
    /// All service methods should return this instead of raw tuples.
    /// </summary>
    public record OperationResult
    {
        public bool Success { get; init; }
        public string Message { get; init; }
        public uint? ErrorCode { get; init; }
        public bool PartialSuccess { get; init; }

        /// <summary>
        /// Indicates the operation failed due to insufficient privileges (access denied).
        /// </summary>
        public bool IsAccessDenied { get; init; }

        public OperationResult(bool success, string message, uint? errorCode = null, bool partialSuccess = false, bool isAccessDenied = false)
        {
            Success = success;
            Message = message ?? string.Empty;
            ErrorCode = errorCode;
            PartialSuccess = partialSuccess;
            IsAccessDenied = isAccessDenied;
        }

        public static OperationResult Ok(string message) => new(true, message);
        public static OperationResult Fail(string message, uint? errorCode = null)
            => new(false, message, errorCode);
        public static OperationResult AccessDenied(string operationName)
            => new(false, string.Format(LocalizationProvider.Current.GetString(ResourceFileNames.DiskManagement, DiskMgmtKeys.AccessDenied_Operation), operationName), isAccessDenied: true);
        public static OperationResult Partial(string message, uint? errorCode = null)
            => new(false, message, errorCode, partialSuccess: true);
    }

    /// <summary>
    /// Result type for operations that return a value.
    /// </summary>
    public record QueryResult<T>
    {
        public bool Success { get; init; }
        public T Value { get; init; }
        public string Message { get; init; }
        public uint? ErrorCode { get; init; }

        /// <summary>
        /// Indicates the query failed due to insufficient privileges (access denied).
        /// </summary>
        public bool IsAccessDenied { get; init; }

        public QueryResult(bool success, T value, string message, uint? errorCode = null, bool isAccessDenied = false)
        {
            Success = success;
            Value = value;
            Message = message ?? string.Empty;
            ErrorCode = errorCode;
            IsAccessDenied = isAccessDenied;
        }

        public static QueryResult<T> Ok(T value, string message) => new(true, value, message);
        public static QueryResult<T> Fail(T defaultValue, string message, uint? errorCode = null)
            => new(false, defaultValue, message, errorCode);
        public static QueryResult<T> AccessDenied(T defaultValue, string operationName)
            => new(false, defaultValue, string.Format(LocalizationProvider.Current.GetString(ResourceFileNames.DiskManagement, DiskMgmtKeys.AccessDenied_Operation), operationName), isAccessDenied: true);
    }
}


