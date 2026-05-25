using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using ManagementTools.Core.Features.UserSecurity.Models.SecPol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static ManagementTools.Core.Features.UserSecurity.Services.SecPol.SecurityPolicyNativeMethods;

namespace ManagementTools.Core.Features.UserSecurity.Services.SecPol
{
    /// <summary>
    /// Handles User Rights Assignment reading/writing via LSA APIs
    /// (LsaEnumerateAccountsWithUserRight, LsaAddAccountRights, LsaRemoveAccountRights).
    /// </summary>
    internal sealed class UserRightsPolicyProvider : IPolicyProvider
    {
        private readonly ILogger<UserRightsPolicyProvider> _logger;

        public UserRightsPolicyProvider()
            : this(NullLogger<UserRightsPolicyProvider>.Instance)
        {
        }

        public UserRightsPolicyProvider(ILogger<UserRightsPolicyProvider> logger)
        {
            _logger = logger;
        }

        public SecurityPolicyCategory Category => SecurityPolicyCategory.UserRightsAssignment;

        public IReadOnlyList<SecurityPolicyDefinition> GetDefinitions()
        {
            return SecurityPolicyDefinitions.GetUserRightsAssignmentDefinitions();
        }

        public SecurityPolicyValue ReadPolicy(SecurityPolicyDefinition definition)
        {
            var value = new SecurityPolicyValue
            {
                Definition = definition,
                IsDefined = true,
                AccountList = new List<string>()
            };

            using var policyHandle = PolicyNativeHelpers.TryOpenLsaPolicyScope(POLICY_LOOKUP_NAMES | POLICY_VIEW_LOCAL_INFORMATION);
            if (policyHandle is null)
            {
                value.IsDefined = false;
                return value;
            }

            using var privilegeName = new PolicyNativeHelpers.LsaUnicodeStringScope(definition.PrivilegeConstant);
            uint status = LsaEnumerateAccountsWithUserRight(
                policyHandle.Handle,
                ref privilegeName.Value,
                out IntPtr enumBuffer,
                out uint countReturned);

            if (status == STATUS_NO_MORE_ENTRIES || status == STATUS_OBJECT_NAME_NOT_FOUND)
            {
                _logger.LogDebug("[UserRightsPolicyProvider] '{PolicyKey}': no accounts assigned", definition.Key);
                return value;
            }

            if (status != STATUS_SUCCESS)
            {
                _logger.LogDebug("[UserRightsPolicyProvider] LsaEnumerateAccountsWithUserRight failed: 0x{Status:X8}", status);
                value.IsDefined = false;
                return value;
            }

            using (var enumScope = new PolicyNativeHelpers.LsaMemoryScope(enumBuffer))
            {
                int structSize = Marshal.SizeOf<LSA_ENUMERATION_INFORMATION>();
                for (uint i = 0; i < countReturned; i++)
                {
                    IntPtr entryPtr = enumScope.Pointer + (int)(i * structSize);
                    var entry = Marshal.PtrToStructure<LSA_ENUMERATION_INFORMATION>(entryPtr);
                    string accountName = PolicyNativeHelpers.LookupSid(entry.Sid);
                    if (!string.IsNullOrEmpty(accountName))
                    {
                        value.AccountList.Add(accountName);
                    }
                }

                _logger.LogDebug("[UserRightsPolicyProvider] '{PolicyKey}': {AccountCount} accounts", definition.Key, value.AccountList.Count);
            }

            return value;
        }

        public void WritePolicy(SecurityPolicyValue value)
        {
            // We need both POLICY_CREATE_ACCOUNT to add rights to accounts that don't have any, and POLICY_LOOKUP_NAMES to resolve SIDs for accounts that do have rights.
            // POLICY_VIEW_LOCAL_INFORMATION is needed to enumerate current accounts with the right.
            uint desiredAccess = POLICY_CREATE_ACCOUNT | POLICY_LOOKUP_NAMES | POLICY_VIEW_LOCAL_INFORMATION;
            using var policyHandle = PolicyNativeHelpers.OpenLsaPolicyOrThrow(desiredAccess, "Failed to open LSA policy for writing user rights.");

            using var privilegeName = new PolicyNativeHelpers.LsaUnicodeStringScope(value.Definition.PrivilegeConstant);

            // First, get current accounts with this right
            var currentAccounts = new List<IntPtr>();
            uint status = LsaEnumerateAccountsWithUserRight(
                policyHandle.Handle,
                ref privilegeName.Value,
                out IntPtr enumBuffer,
                out uint countReturned);

            if (status == STATUS_SUCCESS)
            {
                using var enumScope = new PolicyNativeHelpers.LsaMemoryScope(enumBuffer);
                int structSize = Marshal.SizeOf<LSA_ENUMERATION_INFORMATION>();
                for (uint i = 0; i < countReturned; i++)
                {
                    IntPtr entryPtr = enumScope.Pointer + (int)(i * structSize);
                    var entry = Marshal.PtrToStructure<LSA_ENUMERATION_INFORMATION>(entryPtr);

                    int sidLen = GetLengthSid(entry.Sid);
                    IntPtr sidCopy = Marshal.AllocHGlobal(sidLen);
                    CopySid(sidLen, sidCopy, entry.Sid);
                    currentAccounts.Add(sidCopy);
                }
            }

            try
            {
                var newAccountNames = new HashSet<string>(value.AccountList, StringComparer.OrdinalIgnoreCase);
                var lsaRights = new[] { privilegeName.Value };

                foreach (var sid in currentAccounts)
                {
                    string name = PolicyNativeHelpers.LookupSid(sid);
                    if (!newAccountNames.Contains(name))
                    {
                        status = LsaRemoveAccountRights(policyHandle.Handle, sid, false, lsaRights, 1);
                        _logger.LogDebug("[UserRightsPolicyProvider] Removed right '{PolicyKey}' from '{AccountName}': 0x{Status:X8}", value.Definition.Key, name, status);
                    }
                }

                foreach (string accountName in value.AccountList)
                {
                    IntPtr sid = PolicyNativeHelpers.LookupAccountSidByName(accountName);
                    if (sid != IntPtr.Zero)
                    {
                        try
                        {
                            status = LsaAddAccountRights(policyHandle.Handle, sid, lsaRights, 1);
                            _logger.LogDebug("[UserRightsPolicyProvider] Added right '{PolicyKey}' to '{AccountName}': 0x{Status:X8}", value.Definition.Key, accountName, status);
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(sid);
                        }
                    }
                    else
                    {
                        _logger.LogDebug("[UserRightsPolicyProvider] Could not resolve account '{AccountName}' to SID", accountName);
                    }
                }
            }
            finally
            {
                foreach (var sid in currentAccounts)
                    Marshal.FreeHGlobal(sid);
            }
        }
    }
}




