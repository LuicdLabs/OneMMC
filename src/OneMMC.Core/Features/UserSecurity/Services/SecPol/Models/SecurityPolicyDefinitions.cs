using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OneMMC.Core.Features.UserSecurity.Models.SecPol;

namespace OneMMC.Core.Features.UserSecurity.Services.SecPol
{
    /// <summary>
    /// Supplies the policy definitions that are not registry values and therefore cannot
    /// come from the Security Configuration Engine's registry catalog
    /// (see <see cref="SecurityOptionsCatalog"/>).
    /// <para>
    /// Password, lockout and audit policy live in fixed structures of the SAM and LSA, so
    /// their membership is part of Windows rather than something that can be enumerated;
    /// each is identified here by the item string ID <c>secpol.msc</c> itself uses, and
    /// both the display name and the numeric unit are read from that string at runtime.
    /// User Rights Assignment <i>is</i> enumerable and is read from the engine with
    /// <c>SceLookupPrivRightName</c>, so a privilege added by a Windows update appears
    /// without any change here.
    /// </para>
    /// <para>No user-facing text is stored in this file.</para>
    /// </summary>
    public static class SecurityPolicyDefinitions
    {
        #region Item string IDs (wsecedit.dll)

        // Password policy
        private const int ItemPasswordHistoryLength = 54;
        private const int ItemMaxPasswordAge = 51;
        private const int ItemMinPasswordAge = 52;
        private const int ItemMinPasswordLength = 53;
        private const int ItemPasswordComplexity = 55;
        private const int ItemClearTextPassword = 359;
        private const int ItemRelaxMinPasswordLength = 755;
        private const int ItemMinPasswordLengthAudit = 756;

        // Account lockout policy
        private const int ItemLockoutDuration = 60;
        private const int ItemLockoutThreshold = 58;
        private const int ItemLockoutObservationWindow = 59;
        private const int ItemAllowAdminLockout = 769;

        // Audit policy
        private const int ItemAuditAccountLogon = 273;
        private const int ItemAuditAccountManagement = 88;
        private const int ItemAuditDirectoryServiceAccess = 272;
        private const int ItemAuditLogonEvents = 84;
        private const int ItemAuditObjectAccess = 85;
        private const int ItemAuditPolicyChange = 87;
        private const int ItemAuditPrivilegeUse = 86;
        private const int ItemAuditProcessTracking = 89;
        private const int ItemAuditSystemEvents = 83;

        // Account options that are not registry values
        private const int ItemAdminAccountStatus = 432;
        private const int ItemGuestAccountStatus = 433;
        private const int ItemRenameAdministratorAccount = 65;
        private const int ItemRenameGuestAccount = 67;
        private const int ItemForceLogoffWhenHourExpire = 63;
        private const int ItemLsaAnonymousNameLookup = 57458;

        #endregion

        #region Audit categories (POLICY_AUDIT_EVENT_TYPE order)

        private const int AuditSystem = 0;
        private const int AuditLogon = 1;
        private const int AuditObjectAccess = 2;
        private const int AuditPrivilegeUse = 3;
        private const int AuditProcessTracking = 4;
        private const int AuditPolicyChange = 5;
        private const int AuditAccountManagement = 6;
        private const int AuditDirectoryServiceAccess = 7;
        private const int AuditAccountLogon = 8;

        #endregion

        #region Value ranges

        // Ranges Windows documents for the account policies. Unlike the registry
        // policies, these are properties of the SAM structures rather than of any
        // metadata the system exposes.
        private const long MaxPasswordHistoryLength = 24;
        private const long MaxPasswordAgeDays = 999;
        private const long MinPasswordAgeDays = 998;
        private const long MaxLockoutMinutes = 99999;
        private const long MaxLockoutThreshold = 999;
        private const long MaxPasswordLengthAudit = 128;

        /// <summary>
        /// Minimum password length is capped at 14 characters unless
        /// "Relax minimum password length limits" is enabled, which raises the cap to
        /// <see cref="RelaxedMinPasswordLength"/>. The snap-in reads that policy before
        /// choosing the bound, and so does this catalog.
        /// </summary>
        private const long StandardMinPasswordLength = 14;

        private const long RelaxedMinPasswordLength = 128;

        private const string SamKeyPath = @"SYSTEM\CurrentControlSet\Control\SAM";

        #endregion

        private static ILogger _logger = NullLogger.Instance;

        internal static void ConfigureLogger(ILogger logger)
        {
            _logger = logger ?? NullLogger.Instance;
        }

        #region Password Policy

        public static List<SecurityPolicyDefinition> GetPasswordPolicyDefinitions()
        {
            return
            [
                Numeric("PasswordHistoryLength", SecurityPolicyCategory.PasswordPolicy, ItemPasswordHistoryLength, 0, MaxPasswordHistoryLength),
                Numeric("MaxPasswordAge", SecurityPolicyCategory.PasswordPolicy, ItemMaxPasswordAge, 0, MaxPasswordAgeDays),
                Numeric("MinPasswordAge", SecurityPolicyCategory.PasswordPolicy, ItemMinPasswordAge, 0, MinPasswordAgeDays),
                Numeric("MinPasswordLength", SecurityPolicyCategory.PasswordPolicy, ItemMinPasswordLength, 0, GetMinPasswordLengthMaximum()),
                Boolean("PasswordComplexity", SecurityPolicyCategory.PasswordPolicy, ItemPasswordComplexity),
                Boolean("ClearTextPassword", SecurityPolicyCategory.PasswordPolicy, ItemClearTextPassword),
                Registry(
                    Numeric("MinPasswordLengthAudit", SecurityPolicyCategory.PasswordPolicy, ItemMinPasswordLengthAudit, 0, MaxPasswordLengthAudit),
                    SamKeyPath,
                    "MinimumPasswordLengthAudit"),
                Registry(
                    Boolean("RelaxMinPasswordLength", SecurityPolicyCategory.PasswordPolicy, ItemRelaxMinPasswordLength),
                    SamKeyPath,
                    "RelaxMinimumPasswordLengthLimits")
            ];
        }

        /// <summary>
        /// Returns the upper bound for "Minimum password length", which depends on
        /// whether the relaxed-limits policy is enabled.
        /// </summary>
        private static long GetMinPasswordLengthMaximum()
        {
            var relaxLimits = new SecurityPolicyDefinition
            {
                Key = "RelaxMinPasswordLength",
                PolicyType = SecurityPolicyType.Boolean,
                RegistryKeyPath = SamKeyPath,
                RegistryValueName = "RelaxMinimumPasswordLengthLimits"
            };

            try
            {
                SecurityPolicyValue value = PolicyNativeHelpers.ReadRegistryValue(relaxLimits);
                return value.IsDefined && value.NumericValue != 0
                    ? RelaxedMinPasswordLength
                    : StandardMinPasswordLength;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[SecurityPolicyDefinitions] Could not read the relaxed password length policy");
                return StandardMinPasswordLength;
            }
        }

        #endregion

        #region Account Lockout Policy

        public static List<SecurityPolicyDefinition> GetAccountLockoutPolicyDefinitions()
        {
            return
            [
                Numeric("LockoutDuration", SecurityPolicyCategory.AccountLockoutPolicy, ItemLockoutDuration, 0, MaxLockoutMinutes),
                Numeric("LockoutThreshold", SecurityPolicyCategory.AccountLockoutPolicy, ItemLockoutThreshold, 0, MaxLockoutThreshold),

                // The observation window cannot be zero: it is the period over which
                // failed attempts are counted.
                Numeric("LockoutObservationWindow", SecurityPolicyCategory.AccountLockoutPolicy, ItemLockoutObservationWindow, 1, MaxLockoutMinutes),
                Boolean("AllowAdminLockout", SecurityPolicyCategory.AccountLockoutPolicy, ItemAllowAdminLockout)
            ];
        }

        #endregion

        #region Audit Policy

        public static List<SecurityPolicyDefinition> GetAuditPolicyDefinitions()
        {
            return
            [
                Audit("AuditAccountLogon", ItemAuditAccountLogon, AuditAccountLogon),
                Audit("AuditAccountManagement", ItemAuditAccountManagement, AuditAccountManagement),
                Audit("AuditDirectoryServiceAccess", ItemAuditDirectoryServiceAccess, AuditDirectoryServiceAccess),
                Audit("AuditLogonEvents", ItemAuditLogonEvents, AuditLogon),
                Audit("AuditObjectAccess", ItemAuditObjectAccess, AuditObjectAccess),
                Audit("AuditPolicyChange", ItemAuditPolicyChange, AuditPolicyChange),
                Audit("AuditPrivilegeUse", ItemAuditPrivilegeUse, AuditPrivilegeUse),
                Audit("AuditProcessTracking", ItemAuditProcessTracking, AuditProcessTracking),
                Audit("AuditSystemEvents", ItemAuditSystemEvents, AuditSystem)
            ];
        }

        #endregion

        #region User Rights Assignment

        /// <summary>
        /// Enumerates the privileges and logon rights from the Security Configuration
        /// Engine, in the engine's own order, and resolves each display name through
        /// Win32. This is the enumeration <c>secpol.msc</c> performs.
        /// </summary>
        public static List<SecurityPolicyDefinition> GetUserRightsAssignmentDefinitions()
        {
            var definitions = new List<SecurityPolicyDefinition>();

            foreach (string privilegeConstant in PolicyNativeHelpers.EnumeratePrivilegeConstants())
            {
                definitions.Add(new SecurityPolicyDefinition
                {
                    Key = privilegeConstant,
                    DisplayName = PolicyNativeHelpers.ResolveUserRightDisplayName(privilegeConstant),
                    Category = SecurityPolicyCategory.UserRightsAssignment,
                    PolicyType = SecurityPolicyType.UserRightsAssignment,
                    PrivilegeConstant = privilegeConstant,
                    DataSource = PolicyDataSource.Dynamic
                });
            }

            _logger.LogDebug("[SecurityPolicyDefinitions] Enumerated {Count} user rights", definitions.Count);
            return definitions;
        }

        #endregion

        #region Account options (non-registry Security Options)

        /// <summary>
        /// Returns the Security Options that the registry catalog does not describe.
        /// The account options live in the SAM and are served by an
        /// <see cref="ISpecialPolicyHandler"/>; anonymous SID/Name translation is an LSA
        /// setting the engine exposes through its profile rather than through the
        /// catalog, so its registry location is named here.
        /// </summary>
        internal static List<SecurityPolicyDefinition> GetAccountOptionDefinitions()
        {
            return
            [
                Boolean("AdminAccountStatus", SecurityPolicyCategory.SecurityOptions, ItemAdminAccountStatus),
                Boolean("GuestAccountStatus", SecurityPolicyCategory.SecurityOptions, ItemGuestAccountStatus),
                Text("RenameAdministratorAccount", SecurityPolicyCategory.SecurityOptions, ItemRenameAdministratorAccount),
                Text("RenameGuestAccount", SecurityPolicyCategory.SecurityOptions, ItemRenameGuestAccount),
                Boolean("ForceLogoffWhenHourExpire", SecurityPolicyCategory.SecurityOptions, ItemForceLogoffWhenHourExpire),
                Registry(
                    Boolean("LSAAnonymousNameLookup", SecurityPolicyCategory.SecurityOptions, ItemLsaAnonymousNameLookup),
                    @"SYSTEM\CurrentControlSet\Control\Lsa",
                    "TurnOffAnonymousBlock")
            ];
        }

        #endregion

        #region Get All Definitions By Category

        /// <summary>
        /// Gets the policy definitions for a category.
        /// </summary>
        /// <remarks>
        /// For <see cref="SecurityPolicyCategory.SecurityOptions"/> this returns only the
        /// account options; the full merged set comes from
        /// <see cref="SecurityPolicyService"/>, which adds the registry catalog.
        /// </remarks>
        public static List<SecurityPolicyDefinition> GetDefinitionsForCategory(SecurityPolicyCategory category)
        {
            return category switch
            {
                SecurityPolicyCategory.PasswordPolicy => GetPasswordPolicyDefinitions(),
                SecurityPolicyCategory.AccountLockoutPolicy => GetAccountLockoutPolicyDefinitions(),
                SecurityPolicyCategory.AuditPolicy => GetAuditPolicyDefinitions(),
                SecurityPolicyCategory.UserRightsAssignment => GetUserRightsAssignmentDefinitions(),
                SecurityPolicyCategory.SecurityOptions => GetAccountOptionDefinitions(),
                _ => []
            };
        }

        #endregion

        #region Definition factories

        /// <summary>
        /// Creates a definition whose display name and unit are loaded from the
        /// snap-in's item string, falling back to the key if the resource is missing.
        /// </summary>
        private static SecurityPolicyDefinition Create(string key, SecurityPolicyCategory category, SecurityPolicyType policyType, int itemId)
        {
            PolicyItemText itemText = SecurityPolicyResourceLoader.Instance.LoadItemText(itemId);

            return new SecurityPolicyDefinition
            {
                Key = key,
                DisplayName = itemText.HasName ? itemText.Name : key,
                Unit = policyType == SecurityPolicyType.Numeric ? itemText.Unit : string.Empty,
                Category = category,
                PolicyType = policyType,
                ItemId = itemId
            };
        }

        private static SecurityPolicyDefinition Numeric(string key, SecurityPolicyCategory category, int itemId, long minValue, long maxValue)
        {
            SecurityPolicyDefinition definition = Create(key, category, SecurityPolicyType.Numeric, itemId);
            definition.MinValue = minValue;
            definition.MaxValue = maxValue;
            return definition;
        }

        private static SecurityPolicyDefinition Boolean(string key, SecurityPolicyCategory category, int itemId)
            => Create(key, category, SecurityPolicyType.Boolean, itemId);

        private static SecurityPolicyDefinition Text(string key, SecurityPolicyCategory category, int itemId)
            => Create(key, category, SecurityPolicyType.String, itemId);

        private static SecurityPolicyDefinition Audit(string key, int itemId, int auditEventIndex)
        {
            SecurityPolicyDefinition definition = Create(key, SecurityPolicyCategory.AuditPolicy, SecurityPolicyType.Audit, itemId);
            definition.AuditEventIndex = auditEventIndex;
            return definition;
        }

        /// <summary>
        /// Marks a definition as stored in the registry rather than in the SAM, and
        /// therefore able to return to the Not Defined state.
        /// </summary>
        private static SecurityPolicyDefinition Registry(SecurityPolicyDefinition definition, string keyPath, string valueName)
        {
            definition.RegistryKeyPath = keyPath;
            definition.RegistryValueName = valueName;
            definition.AllowNotDefined = true;
            return definition;
        }

        #endregion
    }
}
