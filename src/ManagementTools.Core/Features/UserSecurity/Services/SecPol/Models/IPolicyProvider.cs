using System.Collections.Generic;
using ManagementTools.Core.Features.UserSecurity.Models.SecPol;

namespace ManagementTools.Core.Features.UserSecurity.Services.SecPol
{
    /// <summary>
    /// Strategy interface for reading and writing security policies.
    /// Each implementation handles a specific <see cref="SecurityPolicyCategory"/> and
    /// encapsulates the native API mechanics required by that category.
    /// </summary>
    public interface IPolicyProvider
    {
        /// <summary>The category this provider handles.</summary>
        SecurityPolicyCategory Category { get; }

        /// <summary>
        /// Returns the full list of policy definitions for this category.
        /// For registry-based categories (SecurityOptions) the list is discovered dynamically
        /// from sceregvl.inf; for other categories a well-known set is returned.
        /// </summary>
        IReadOnlyList<SecurityPolicyDefinition> GetDefinitions();

        /// <summary>Reads the current value of a single policy from the system.</summary>
        SecurityPolicyValue ReadPolicy(SecurityPolicyDefinition definition);

        /// <summary>Writes a policy value to the system.</summary>
        void WritePolicy(SecurityPolicyValue value);
    }
}


