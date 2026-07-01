using OneMMC.Core.Features.PolicyManagement.Models.GpEdit;
using OneMMC.Core.Features.PolicyManagement.Services.GpEdit;

namespace OneMMC.Core.Features.PolicyManagement.Services.RSoP
{
    /// <summary>
    /// Represents the result of a policy evaluation in the RSoP context.
    /// Contains the policy metadata and its effective state.
    /// </summary>
    public sealed class RSoPPolicyResult
    {
        /// <summary>
        /// Gets the display name of the policy.
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        /// Gets the description/explanation of the policy.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Gets the effective state of the policy.
        /// </summary>
        public PolicyState State { get; }

        /// <summary>
        /// Gets the registry key path associated with this policy.
        /// </summary>
        public string RegistryKey { get; }

        /// <summary>
        /// Gets the registry value name associated with this policy.
        /// </summary>
        public string RegistryValue { get; }

        /// <summary>
        /// Gets the human-readable category path (e.g. "System \ Logon").
        /// </summary>
        public string CategoryPath { get; }

        /// <summary>
        /// Gets the supported-on text for this policy.
        /// </summary>
        public string SupportedOn { get; }

        /// <summary>
        /// Gets whether this is a computer (machine) policy or a user policy.
        /// </summary>
        public bool IsComputerPolicy { get; }

        /// <summary>
        /// Gets the underlying PolicyManagerPolicy for detailed inspection.
        /// </summary>
        public PolicyManagerPolicy UnderlyingPolicy { get; }

        /// <summary>
        /// Creates a new RSoPPolicyResult from a PolicyManagerPolicy and its evaluated state.
        /// </summary>
        public RSoPPolicyResult(PolicyManagerPolicy policy, PolicyState state, bool isComputer)
        {
            UnderlyingPolicy = policy;
            DisplayName = policy.DisplayName;
            Description = policy.DisplayExplanation;
            State = state;
            RegistryKey = policy.RawPolicy.RegistryKey;
            RegistryValue = policy.RawPolicy.RegistryValue;
            CategoryPath = RSoPService.BuildCategoryPath(policy.Category);
            SupportedOn = policy.SupportedOn?.DisplayName ?? string.Empty;
            IsComputerPolicy = isComputer;
        }
    }
}


