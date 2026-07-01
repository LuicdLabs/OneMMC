using OneMMC.Core.Features.UserSecurity.Models.SecPol;

namespace OneMMC.Core.Features.UserSecurity.Services.SecPol
{
    /// <summary>
    /// Strategy interface for special (non-registry) security option policies.
    /// <para>
    /// Each implementation encapsulates the read/write logic for one special policy
    /// (e.g., built-in account status, forced logoff). This replaces the fragile
    /// <c>Dictionary&lt;string, Action&gt;</c> pattern with a strongly-typed contract
    /// that the compiler can verify at build time.
    /// </para>
    /// </summary>
    internal interface ISpecialPolicyHandler
    {
        /// <summary>The unique key identifying the policy this handler manages.</summary>
        string Key { get; }

        /// <summary>Reads the current policy value from the system.</summary>
        void Read(SecurityPolicyValue value);

        /// <summary>Writes the policy value to the system.</summary>
        void Write(SecurityPolicyValue value);
    }
}


