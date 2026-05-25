using System;
using System.Collections.Generic;
using ManagementTools.Core.Features.PolicyManagement.Models.GpEdit;
using ManagementTools.Core.Features.PolicyManagement.Services.GpEdit;

namespace ManagementTools.Core.Features.PolicyManagement.Services.GpEdit.Manager
{
    /// <summary>
    /// Defines the contract for a policy service that manages reading and writing of Group Policy settings.
    /// </summary>
    public interface IPolicyService : IDisposable
    {
        /// <summary>
        /// Gets whether this service is for user policies (true) or machine policies (false).
        /// </summary>
        bool IsUserPolicy { get; }

        /// <summary>
        /// Gets whether the policy source is writable.
        /// </summary>
        bool IsWritable { get; }

        /// <summary>
        /// Gets whether the service has been successfully initialized.
        /// </summary>
        bool IsInitialized { get; }

        /// <summary>
        /// Gets the last error that occurred during an operation.
        /// </summary>
        string? LastError { get; }

        /// <summary>
        /// Initializes the policy service and opens the underlying policy source.
        /// </summary>
        /// <returns>True if initialization succeeded, false otherwise.</returns>
        bool Initialize();

        /// <summary>
        /// Gets the current state of a policy.
        /// </summary>
        /// <param name="policy">The policy to check.</param>
        /// <returns>The current state of the policy.</returns>
        PolicyState GetPolicyState(PolicyManagerPolicy policy);

        /// <summary>
        /// Gets the current option values for a policy.
        /// </summary>
        /// <param name="policy">The policy to get options for.</param>
        /// <returns>A dictionary of option IDs to their current values.</returns>
        Dictionary<string, object> GetPolicyOptions(PolicyManagerPolicy policy);

        /// <summary>
        /// Sets the state and options for a policy.
        /// </summary>
        /// <param name="policy">The policy to modify.</param>
        /// <param name="state">The new state to set.</param>
        /// <param name="options">The options to apply (used when state is Enabled).</param>
        /// <returns>True if the operation succeeded, false otherwise.</returns>
        bool SetPolicyState(PolicyManagerPolicy policy, PolicyState state, Dictionary<string, object>? options);

        /// <summary>
        /// Saves any pending changes to the policy source.
        /// </summary>
        /// <returns>A human-readable description of what was saved.</returns>
        string Save();

        /// <summary>
        /// Reloads the policy source to reflect external changes.
        /// </summary>
        void Reload();
    }
}


