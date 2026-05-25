using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ManagementTools.Core.Features.UserSecurity.Models.SecPol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static ManagementTools.Core.Features.UserSecurity.Services.SecPol.SecurityPolicyNativeMethods;

namespace ManagementTools.Core.Features.UserSecurity.Services.SecPol
{
    /// <summary>
    /// Handles Audit Policy reading/writing via LsaQueryInformationPolicy / LsaSetInformationPolicy.
    /// </summary>
    internal sealed class AuditPolicyProvider : IPolicyProvider
    {
        private readonly ILogger<AuditPolicyProvider> _logger;

        public AuditPolicyProvider()
            : this(NullLogger<AuditPolicyProvider>.Instance)
        {
        }

        public AuditPolicyProvider(ILogger<AuditPolicyProvider> logger)
        {
            _logger = logger;
        }

        public SecurityPolicyCategory Category => SecurityPolicyCategory.AuditPolicy;

        public IReadOnlyList<SecurityPolicyDefinition> GetDefinitions()
        {
            return SecurityPolicyDefinitions.GetAuditPolicyDefinitions();
        }

        public SecurityPolicyValue ReadPolicy(SecurityPolicyDefinition definition)
        {
            var value = new SecurityPolicyValue { Definition = definition, IsDefined = true };

            using var policyHandle = PolicyNativeHelpers.TryOpenLsaPolicyScope(POLICY_VIEW_AUDIT_INFORMATION | POLICY_VIEW_LOCAL_INFORMATION);
            if (policyHandle is null)
            {
                value.IsDefined = false;
                return value;
            }

            if (!PolicyNativeHelpers.TryQueryLsaPolicyBuffer(policyHandle.Handle, PolicyAuditEventsInformation, out var queryBuffer, out uint status))
            {
                _logger.LogDebug("[AuditPolicyProvider] LsaQueryInformationPolicy failed: 0x{Status:X8}", status);
                value.IsDefined = false;
                return value;
            }

            using (queryBuffer)
            {
                IntPtr eventsPtr = Marshal.ReadIntPtr(queryBuffer.Pointer, IntPtr.Size);
                int maxCount = Marshal.ReadInt32(queryBuffer.Pointer, IntPtr.Size + IntPtr.Size);

                if (definition.AuditEventIndex >= 0 && definition.AuditEventIndex < maxCount)
                {
                    int eventValue = Marshal.ReadInt32(eventsPtr, definition.AuditEventIndex * sizeof(int));
                    value.NumericValue = ConvertAuditEventToFlags(eventValue);
                    _logger.LogDebug("[AuditPolicyProvider] '{PolicyKey}': raw={RawValue}, flags={FlagsValue}", definition.Key, eventValue, value.NumericValue);
                }
                else
                {
                    value.IsDefined = false;
                }
            }

            return value;
        }

        public void WritePolicy(SecurityPolicyValue value)
        {
            // Enable SeSecurityPrivilege - required for LsaSetInformationPolicy with audit events
            PolicyNativeHelpers.EnsurePrivilegeEnabled(SE_SECURITY_NAME);

            using var policyHandle = PolicyNativeHelpers.OpenLsaPolicyOrThrow(
                POLICY_VIEW_AUDIT_INFORMATION | POLICY_VIEW_LOCAL_INFORMATION | POLICY_SET_AUDIT_REQUIREMENTS,
                "Failed to open LSA policy for writing audit settings.");

            if (!PolicyNativeHelpers.TryQueryLsaPolicyBuffer(policyHandle.Handle, PolicyAuditEventsInformation, out var queryBuffer, out uint status))
                throw new InvalidOperationException($"LsaQueryInformationPolicy failed: 0x{status:X8}");

            using (queryBuffer)
            {
                IntPtr eventsPtr = Marshal.ReadIntPtr(queryBuffer.Pointer, IntPtr.Size);
                int maxCount = Marshal.ReadInt32(queryBuffer.Pointer, IntPtr.Size + IntPtr.Size);

                if (value.Definition.AuditEventIndex < 0 || value.Definition.AuditEventIndex >= maxCount)
                    throw new ArgumentOutOfRangeException(nameof(value), "AuditEventIndex out of range.");

                int[] events = new int[maxCount];
                for (int i = 0; i < maxCount; i++)
                    events[i] = Marshal.ReadInt32(eventsPtr, i * sizeof(int));

                events[value.Definition.AuditEventIndex] = ConvertFlagsToAuditEvent((AuditPolicyFlags)value.NumericValue);

                int structSize = IntPtr.Size + IntPtr.Size + sizeof(int) + sizeof(int);
                using var writeBuffer = new PolicyNativeHelpers.HGlobalBuffer(structSize);
                using var eventsBuffer = new PolicyNativeHelpers.HGlobalBuffer(maxCount * sizeof(int));

                Marshal.Copy(events, 0, eventsBuffer.Pointer, maxCount);

                Marshal.WriteByte(writeBuffer.Pointer, 0, 1); // AuditingMode = true
                for (int i = 1; i < IntPtr.Size; i++)
                    Marshal.WriteByte(writeBuffer.Pointer, i, 0);
                Marshal.WriteIntPtr(writeBuffer.Pointer, IntPtr.Size, eventsBuffer.Pointer);
                Marshal.WriteInt32(writeBuffer.Pointer, IntPtr.Size + IntPtr.Size, maxCount);

                status = LsaSetInformationPolicy(policyHandle.Handle, PolicyAuditEventsInformation, writeBuffer.Pointer);
                if (status != STATUS_SUCCESS)
                {
                    int win32Error = LsaNtStatusToWinError(status);
                    _logger.LogDebug("[AuditPolicyProvider] LsaSetInformationPolicy failed: NTSTATUS=0x{Status:X8}, Win32={Win32Error}", status, win32Error);

                    if (win32Error == ERROR_ACCESS_DENIED)
                    {
                        throw new UnauthorizedAccessException(
                            "Access denied while writing audit policy. Run ManagementTools elevated and ensure the current account can hold SeSecurityPrivilege.");
                    }

                    throw new InvalidOperationException($"LsaSetInformationPolicy failed: NTSTATUS=0x{status:X8}, Win32={win32Error}");
                }

                _logger.LogDebug("[AuditPolicyProvider] Wrote '{PolicyKey}' = {PolicyValue}", value.Definition.Key, value.NumericValue);
            }
        }

        private static long ConvertAuditEventToFlags(int eventValue)
        {
            long flags = 0;
            if ((eventValue & POLICY_AUDIT_EVENT_SUCCESS) != 0) flags |= (long)AuditPolicyFlags.Success;
            if ((eventValue & POLICY_AUDIT_EVENT_FAILURE) != 0) flags |= (long)AuditPolicyFlags.Failure;
            return flags;
        }

        private static int ConvertFlagsToAuditEvent(AuditPolicyFlags flags)
        {
            int result = 0;
            if (flags.HasFlag(AuditPolicyFlags.Success)) result |= (int)POLICY_AUDIT_EVENT_SUCCESS;
            if (flags.HasFlag(AuditPolicyFlags.Failure)) result |= (int)POLICY_AUDIT_EVENT_FAILURE;
            if (result == 0) result = (int)POLICY_AUDIT_EVENT_NONE;
            return result;
        }
    }
}





