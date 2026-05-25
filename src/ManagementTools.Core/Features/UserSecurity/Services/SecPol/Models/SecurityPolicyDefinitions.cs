using System;
using System.Collections.Generic;
using ManagementTools.Core.Features.UserSecurity.Models.SecPol;

namespace ManagementTools.Core.Features.UserSecurity.Services.SecPol
{
    /// <summary>
    /// Provides a catalog of well-known security policy definitions.
    /// <para>
    /// Display names are loaded at runtime from <c>wsecedit.dll</c> string resources
    /// via <see cref="SecurityPolicyResourceLoader"/>, ensuring they are automatically
    /// localized based on the current system language.
    /// </para>
    /// <para>
    /// For Security Options, these definitions cover only the special non-registry policies.
    /// Registry-based Security Options are parsed dynamically from <c>sceregvl.inf</c>
    /// by <see cref="SceRegVlParser"/>.
    /// </para>
    /// </summary>
    public static class SecurityPolicyDefinitions
    {
        #region Well-Known Audit Category GUIDs (Windows Vista+)

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

        #region Display Name Resolution

        /// <summary>
        /// Resolves localized display names for a list of definitions by loading
        /// from <c>wsecedit.dll</c> using each definition's ExplainResourceId.
        /// </summary>
        private static List<SecurityPolicyDefinition> ResolveDisplayNames(List<SecurityPolicyDefinition> definitions)
        {
            var loader = SecurityPolicyResourceLoader.Instance;
            foreach (var def in definitions)
            {
                loader.ResolveDefinitionDisplayName(def);
            }
            return definitions;
        }

        #endregion

        #region Password Policy Definitions

        public static List<SecurityPolicyDefinition> GetPasswordPolicyDefinitions()
        {
            return ResolveDisplayNames(new List<SecurityPolicyDefinition>
            {
                new()
                {
                    Key = "PasswordHistoryLength",
                    Category = SecurityPolicyCategory.PasswordPolicy,
                    PolicyType = SecurityPolicyType.Numeric,
                    MinValue = 0, MaxValue = 24, Unit = "passwords remembered",
                    ExplainResourceId = 1900
                },
                new()
                {
                    Key = "MaxPasswordAge",
                    Category = SecurityPolicyCategory.PasswordPolicy,
                    PolicyType = SecurityPolicyType.Numeric,
                    MinValue = 0, MaxValue = 999, Unit = "days",
                    ExplainResourceId = 1901
                },
                new()
                {
                    Key = "MinPasswordAge",
                    Category = SecurityPolicyCategory.PasswordPolicy,
                    PolicyType = SecurityPolicyType.Numeric,
                    MinValue = 0, MaxValue = 998, Unit = "days",
                    ExplainResourceId = 1902
                },
                new()
                {
                    Key = "MinPasswordLength",
                    Category = SecurityPolicyCategory.PasswordPolicy,
                    PolicyType = SecurityPolicyType.Numeric,
                    MinValue = 0, MaxValue = 128, Unit = "characters",
                    ExplainResourceId = 1903
                },
                new()
                {
                    Key = "PasswordComplexity",
                    Category = SecurityPolicyCategory.PasswordPolicy,
                    PolicyType = SecurityPolicyType.Boolean,
                    ExplainResourceId = 1904
                },
                new()
                {
                    Key = "ClearTextPassword",
                    Category = SecurityPolicyCategory.PasswordPolicy,
                    PolicyType = SecurityPolicyType.Boolean,
                    ExplainResourceId = 1905
                },
                new()
                {
                    Key = "MinPasswordLengthAudit",
                    Category = SecurityPolicyCategory.PasswordPolicy,
                    PolicyType = SecurityPolicyType.Numeric,
                    MinValue = 0, MaxValue = 128, Unit = "characters",
                    AllowNotDefined = true,
                    RegistryKeyPath = @"SYSTEM\CurrentControlSet\Control\SAM",
                    RegistryValueName = "MinimumPasswordLengthAudit",
                    ExplainResourceId = 2085
                },
                new()
                {
                    Key = "RelaxMinPasswordLength",
                    Category = SecurityPolicyCategory.PasswordPolicy,
                    PolicyType = SecurityPolicyType.Boolean,
                    AllowNotDefined = true,
                    RegistryKeyPath = @"SYSTEM\CurrentControlSet\Control\SAM",
                    RegistryValueName = "RelaxMinimumPasswordLengthLimits",
                    ExplainResourceId = 2084
                }
            });
        }

        #endregion

        #region Account Lockout Policy Definitions

        public static List<SecurityPolicyDefinition> GetAccountLockoutPolicyDefinitions()
        {
            return ResolveDisplayNames(new List<SecurityPolicyDefinition>
            {
                new()
                {
                    Key = "LockoutDuration",
                    Category = SecurityPolicyCategory.AccountLockoutPolicy,
                    PolicyType = SecurityPolicyType.Numeric,
                    MinValue = 0, MaxValue = 99999, Unit = "minutes",
                    ExplainResourceId = 1906
                },
                new()
                {
                    Key = "LockoutThreshold",
                    Category = SecurityPolicyCategory.AccountLockoutPolicy,
                    PolicyType = SecurityPolicyType.Numeric,
                    MinValue = 0, MaxValue = 999, Unit = "invalid logon attempts",
                    ExplainResourceId = 1907
                },
                new()
                {
                    Key = "LockoutObservationWindow",
                    Category = SecurityPolicyCategory.AccountLockoutPolicy,
                    PolicyType = SecurityPolicyType.Numeric,
                    MinValue = 1, MaxValue = 99999, Unit = "minutes",
                    ExplainResourceId = 1908
                },
                new()
                {
                    Key = "AllowAdminLockout",
                    Category = SecurityPolicyCategory.AccountLockoutPolicy,
                    PolicyType = SecurityPolicyType.Boolean,
                    ExplainResourceId = 2088
                }
            });
        }

        #endregion

        #region Audit Policy Definitions

        public static List<SecurityPolicyDefinition> GetAuditPolicyDefinitions()
        {
            return ResolveDisplayNames(new List<SecurityPolicyDefinition>
            {
                new() { Key = "AuditAccountLogon", Category = SecurityPolicyCategory.AuditPolicy, PolicyType = SecurityPolicyType.Audit, AuditEventIndex = AuditAccountLogon, ExplainResourceId = 1914 },
                new() { Key = "AuditAccountManagement", Category = SecurityPolicyCategory.AuditPolicy, PolicyType = SecurityPolicyType.Audit, AuditEventIndex = AuditAccountManagement, ExplainResourceId = 1915 },
                new() { Key = "AuditDirectoryServiceAccess", Category = SecurityPolicyCategory.AuditPolicy, PolicyType = SecurityPolicyType.Audit, AuditEventIndex = AuditDirectoryServiceAccess, ExplainResourceId = 1916 },
                new() { Key = "AuditLogonEvents", Category = SecurityPolicyCategory.AuditPolicy, PolicyType = SecurityPolicyType.Audit, AuditEventIndex = AuditLogon, ExplainResourceId = 1917 },
                new() { Key = "AuditObjectAccess", Category = SecurityPolicyCategory.AuditPolicy, PolicyType = SecurityPolicyType.Audit, AuditEventIndex = AuditObjectAccess, ExplainResourceId = 1918 },
                new() { Key = "AuditPolicyChange", Category = SecurityPolicyCategory.AuditPolicy, PolicyType = SecurityPolicyType.Audit, AuditEventIndex = AuditPolicyChange, ExplainResourceId = 1919 },
                new() { Key = "AuditPrivilegeUse", Category = SecurityPolicyCategory.AuditPolicy, PolicyType = SecurityPolicyType.Audit, AuditEventIndex = AuditPrivilegeUse, ExplainResourceId = 1920 },
                new() { Key = "AuditProcessTracking", Category = SecurityPolicyCategory.AuditPolicy, PolicyType = SecurityPolicyType.Audit, AuditEventIndex = AuditProcessTracking, ExplainResourceId = 1921 },
                new() { Key = "AuditSystemEvents", Category = SecurityPolicyCategory.AuditPolicy, PolicyType = SecurityPolicyType.Audit, AuditEventIndex = AuditSystem, ExplainResourceId = 1922 },
            });
        }

        #endregion

        #region User Rights Assignment Definitions

        public static List<SecurityPolicyDefinition> GetUserRightsAssignmentDefinitions()
        {
            return ResolveDisplayNames(new List<SecurityPolicyDefinition>
            {
                MakeUra("SeTrustedCredManAccessPrivilege", 2060),
                MakeUra("SeNetworkLogonRight", 1923),
                MakeUra("SeTcbPrivilege", 1924),
                MakeUra("SeMachineAccountPrivilege", 1925),
                MakeUra("SeIncreaseQuotaPrivilege", 1926),
                MakeUra("SeInteractiveLogonRight", 1927),
                MakeUra("SeRemoteInteractiveLogonRight", 1928),
                MakeUra("SeBackupPrivilege", 1929),
                MakeUra("SeChangeNotifyPrivilege", 1930),
                MakeUra("SeSystemtimePrivilege", 1931),
                MakeUra("SeTimeZonePrivilege", 2061),
                MakeUra("SeCreatePagefilePrivilege", 1932),
                MakeUra("SeCreateTokenPrivilege", 1933),
                MakeUra("SeCreateGlobalPrivilege", 1934),
                MakeUra("SeCreatePermanentPrivilege", 1935),
                MakeUra("SeCreateSymbolicLinkPrivilege", 2057),
                MakeUra("SeDebugPrivilege", 1936),
                MakeUra("SeDenyNetworkLogonRight", 1937),
                MakeUra("SeDenyBatchLogonRight", 1938),
                MakeUra("SeDenyServiceLogonRight", 1939),
                MakeUra("SeDenyInteractiveLogonRight", 1940),
                MakeUra("SeDenyRemoteInteractiveLogonRight", 1941),
                MakeUra("SeEnableDelegationPrivilege", 1942),
                MakeUra("SeRemoteShutdownPrivilege", 1943),
                MakeUra("SeAuditPrivilege", 1944),
                MakeUra("SeImpersonatePrivilege", 1945),
                MakeUra("SeIncreaseWorkingSetPrivilege", 2062),
                MakeUra("SeIncreaseBasePriorityPrivilege", 1946),
                MakeUra("SeLoadDriverPrivilege", 1947),
                MakeUra("SeLockMemoryPrivilege", 1948),
                MakeUra("SeBatchLogonRight", 1949),
                MakeUra("SeServiceLogonRight", 1950),
                MakeUra("SeSecurityPrivilege", 1951),
                MakeUra("SeRelabelPrivilege", 2058),
                MakeUra("SeSystemEnvironmentPrivilege", 1952),
                MakeUra("SeDelegateSessionUserImpersonatePrivilege", 2080),
                MakeUra("SeManageVolumePrivilege", 1953),
                MakeUra("SeProfileSingleProcessPrivilege", 1954),
                MakeUra("SeSystemProfilePrivilege", 1955),
                MakeUra("SeUndockPrivilege", 1956),
                MakeUra("SeAssignPrimaryTokenPrivilege", 1957),
                MakeUra("SeRestorePrivilege", 1958),
                MakeUra("SeShutdownPrivilege", 1959),
                MakeUra("SeSyncAgentPrivilege", 1960),
                MakeUra("SeTakeOwnershipPrivilege", 1961)
            });
        }

        private static SecurityPolicyDefinition MakeUra(string privilegeConstant, int explainResourceId)
        {
            var loader = SecurityPolicyResourceLoader.Instance;
            var privilegeDisplayName = loader.ResolvePrivilegeDisplayName(privilegeConstant);

            return new SecurityPolicyDefinition
            {
                Key = privilegeConstant,
                DisplayName = privilegeDisplayName ?? privilegeConstant,
                Category = SecurityPolicyCategory.UserRightsAssignment,
                PolicyType = SecurityPolicyType.UserRightsAssignment,
                PrivilegeConstant = privilegeConstant,
                ExplainResourceId = explainResourceId
            };
        }

        #endregion

        #region Security Options Definitions

        /// <summary>
        /// Returns only the <b>special non-registry</b> Security Options definitions.
        /// Display names are loaded from <c>wsecedit.dll</c> at runtime.
        /// </summary>
        /// <remarks>
        /// <b>Deprecated:</b> Security Options definitions are now loaded from the
        /// embedded <c>SecurityOptionsDefinitions.json</c> resource by
        /// <see cref="SecurityOptionsPolicyProvider"/>. This method is retained
        /// only for backward compatibility with <see cref="GetDefinitionsForCategory"/>.
        /// </remarks>
        [System.Obsolete("Use SecurityOptionsPolicyProvider which loads definitions from SecurityOptionsDefinitions.json.")]
        public static List<SecurityPolicyDefinition> GetSecurityOptionsDefinitions()
        {
            return ResolveDisplayNames(new List<SecurityPolicyDefinition>
            {
                new()
                {
                    Key = "AdminAccountStatus",
                    Category = SecurityPolicyCategory.SecurityOptions,
                    PolicyType = SecurityPolicyType.Boolean,
                    ExplainResourceId = 1962
                },
                new()
                {
                    Key = "GuestAccountStatus",
                    Category = SecurityPolicyCategory.SecurityOptions,
                    PolicyType = SecurityPolicyType.Boolean,
                    ExplainResourceId = 1963
                },
                new()
                {
                    Key = "RenameAdministratorAccount",
                    Category = SecurityPolicyCategory.SecurityOptions,
                    PolicyType = SecurityPolicyType.String,
                    ExplainResourceId = 1965
                },
                new()
                {
                    Key = "RenameGuestAccount",
                    Category = SecurityPolicyCategory.SecurityOptions,
                    PolicyType = SecurityPolicyType.String,
                    ExplainResourceId = 1966
                },
                new()
                {
                    Key = "ForceLogoffWhenHourExpire",
                    Category = SecurityPolicyCategory.SecurityOptions,
                    PolicyType = SecurityPolicyType.Boolean,
                    ExplainResourceId = 2013
                }
            });
        }

        #endregion

        #region Get All Definitions By Category

        /// <summary>
        /// Gets policy definitions for a given category with localized display names.
        /// <para>
        /// For most categories this returns the authoritative set. For
        /// <see cref="SecurityPolicyCategory.SecurityOptions"/>, prefer using
        /// <see cref="SecurityPolicyService"/> which loads the full merged set
        /// from <c>SecurityOptionsDefinitions.json</c> and <c>sceregvl.inf</c>.
        /// </para>
        /// </summary>
        public static List<SecurityPolicyDefinition> GetDefinitionsForCategory(SecurityPolicyCategory category)
        {
#pragma warning disable CS0618 // Suppress obsolete warning for backward compatibility
            return category switch
            {
                SecurityPolicyCategory.PasswordPolicy => GetPasswordPolicyDefinitions(),
                SecurityPolicyCategory.AccountLockoutPolicy => GetAccountLockoutPolicyDefinitions(),
                SecurityPolicyCategory.AuditPolicy => GetAuditPolicyDefinitions(),
                SecurityPolicyCategory.UserRightsAssignment => GetUserRightsAssignmentDefinitions(),
                SecurityPolicyCategory.SecurityOptions => GetSecurityOptionsDefinitions(),
                _ => new List<SecurityPolicyDefinition>()
            };
#pragma warning restore CS0618
        }

        #endregion
    }
}


