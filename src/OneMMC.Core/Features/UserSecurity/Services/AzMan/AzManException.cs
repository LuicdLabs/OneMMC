// ============================================================================
// AzMan Exception
// ============================================================================

using System;

namespace OneMMC.Core.Features.UserSecurity.Services.AzMan;

/// <summary>
/// AzMan-related exception
/// </summary>
public class AzManException : Exception
{
    /// <summary>
    /// Create an AzMan exception
    /// </summary>
    public AzManException() { }

    /// <summary>
    /// Create an AzMan exception
    /// </summary>
    public AzManException(string message) : base(message) { }

    /// <summary>
    /// Create an AzMan exception
    /// </summary>
    public AzManException(string message, Exception innerException) : base(message, innerException) { }
}


