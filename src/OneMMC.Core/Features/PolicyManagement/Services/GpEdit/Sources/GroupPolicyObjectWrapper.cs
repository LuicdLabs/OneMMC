using System;
using System.Runtime.InteropServices;
using OneMMC.Core.Features.PolicyManagement.Services.GpEdit.Native;
using OneMMC.Core.Infrastructure.Interop;
using OneMMC.Core.Infrastructure.PolicyStorage;
using Microsoft.Win32;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace OneMMC.Core.Features.PolicyManagement.Services.GpEdit.Sources
{
    /// <summary>
    /// Wrapper class for IGroupPolicyObject COM interface.
    /// This wrapper provides a managed interface for manipulating Group Policy Objects
    /// through the official Windows API, ensuring changes are visible in gpedit.msc and rsop.msc.
    /// </summary>
    public partial class GroupPolicyObjectWrapper : IDisposable
    {
        private static ILogger _logger = NullLogger.Instance;
        private IGroupPolicyObject? _gpo;
        private bool _disposed;
        private int _owningThreadId;
        private ApartmentState _owningApartmentState;

        /// <summary>
        /// Gets whether the GPO was successfully opened.
        /// </summary>
        public bool IsOpen => _gpo != null;

        public static void ConfigureLogger(ILogger logger)
        {
            _logger = logger ?? NullLogger.Instance;
        }

        /// <summary>
        /// Creates a new GroupPolicyObjectWrapper and opens the local machine GPO.
        /// </summary>
        /// <param name="forEditing">True to open for editing (read-write), false for read-only.</param>
        /// <returns>A new wrapper instance, or null if the GPO cannot be opened.</returns>
        public static GroupPolicyObjectWrapper? OpenLocalMachine(bool forEditing = true)
        {
            var wrapper = new GroupPolicyObjectWrapper();
            try
            {
                wrapper._gpo = ComActivator.CreateInstance<IGroupPolicyObject>(GroupPolicyObjectClsid.GroupPolicyObject);
                wrapper.CaptureOwningApartment();
                uint flags = forEditing ? (uint)GpoOpenFlags.Editing : (uint)GpoOpenFlags.LoadRegistry;
                int hr = wrapper._gpo.OpenLocalMachineGPO(flags);
                
                if (hr != 0)
                {
                    _logger.LogDebug($"[GPO] OpenLocalMachineGPO failed with HRESULT: 0x{hr:X8}");
                    ComActivator.Release(wrapper._gpo);
                    wrapper._gpo = null;
                    return null;
                }
                
                _logger.LogDebug("[GPO] Successfully opened local machine GPO");
                return wrapper;
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[GPO] Exception opening GPO: {ex.Message}");
                if (wrapper._gpo != null)
                {
                    ComActivator.Release(wrapper._gpo);
                    wrapper._gpo = null;
                }
                return null;
            }
        }

        /// <summary>
        /// Creates a new GroupPolicyObjectWrapper and opens a remote machine's GPO.
        /// </summary>
        /// <param name="computerName">The name of the remote computer.</param>
        /// <param name="forEditing">True to open for editing, false for read-only.</param>
        /// <returns>A new wrapper instance, or null if the GPO cannot be opened.</returns>
        public static GroupPolicyObjectWrapper? OpenRemoteMachine(string computerName, bool forEditing = true)
        {
            var wrapper = new GroupPolicyObjectWrapper();
            try
            {
                wrapper._gpo = ComActivator.CreateInstance<IGroupPolicyObject>(GroupPolicyObjectClsid.GroupPolicyObject);
                wrapper.CaptureOwningApartment();
                uint flags = forEditing ? (uint)GpoOpenFlags.Editing : (uint)GpoOpenFlags.LoadRegistry;
                int hr = wrapper._gpo.OpenRemoteMachineGPO(computerName, flags);
                
                if (hr != 0)
                {
                    _logger.LogDebug($"[GPO] OpenRemoteMachineGPO failed with HRESULT: 0x{hr:X8}");
                    ComActivator.Release(wrapper._gpo);
                    wrapper._gpo = null;
                    return null;
                }
                
                return wrapper;
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[GPO] Exception opening remote GPO: {ex.Message}");
                if (wrapper._gpo != null)
                {
                    ComActivator.Release(wrapper._gpo);
                    wrapper._gpo = null;
                }
                return null;
            }
        }

        /// <summary>
        /// Gets the registry key handle for the specified GPO section.
        /// </summary>
        /// <param name="isUser">True for user policy, false for machine policy.</param>
        /// <returns>A RegistryKey that can be used to read/write policy values, or null on failure.</returns>
        public RegistryKey? GetRegistryKey(bool isUser)
        {
            if (_gpo == null) return null;

            try
            {
                uint section = isUser ? (uint)GpoSection.User : (uint)GpoSection.Machine;
                IntPtr hKey;
                int hr = _gpo.GetRegistryKey(section, out hKey);
                
                if (hr != 0 || hKey == IntPtr.Zero)
                {
                    _logger.LogDebug($"[GPO] GetRegistryKey failed with HRESULT: 0x{hr:X8}");
                    return null;
                }

                // IGroupPolicyObject::GetRegistryKey transfers a handle that the caller must close.
                // Give RegistryKey an owning SafeHandle so disposing the RegistryKey calls RegCloseKey.
                var safeHandle = new Microsoft.Win32.SafeHandles.SafeRegistryHandle(hKey, ownsHandle: true);
                try
                {
                    return RegistryKey.FromHandle(safeHandle);
                }
                catch
                {
                    safeHandle.Dispose();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[GPO] Exception getting registry key: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Applies a PolFile's content to the GPO and saves it.
        /// This is the key method that ensures policy changes are visible in gpedit.msc and rsop.msc.
        /// </summary>
        /// <param name="polFile">The PolFile containing policy settings to apply.</param>
        /// <param name="isUser">True for user policy, false for machine policy.</param>
        /// <returns>True if successful, false otherwise.</returns>
        public bool ApplyAndSave(PolFile polFile, bool isUser)
        {
            if (_gpo == null)
            {
                _logger.LogDebug("[GPO] Cannot apply: GPO not open");
                return false;
            }

            try
            {
                // Get the GPO's registry key for writing
                using RegistryKey? gpoRegKey = GetRegistryKey(isUser);
                if (gpoRegKey == null)
                {
                    _logger.LogDebug("[GPO] Failed to get GPO registry key");
                    return false;
                }

                // Create a RegistryPolicyProxy to wrap the GPO's registry
                var gpoProxy = RegistryPolicyProxy.EncapsulateKey(gpoRegKey);

                // Apply the PolFile content to the GPO's registry buffer
                _logger.LogDebug($"[GPO] Applying PolFile content to GPO registry buffer");
                polFile.Apply(gpoProxy);

                // Now save through the COM interface - this properly updates GPO metadata
                return Save(isUser, addExtension: true);
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[GPO] Exception applying PolFile: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Saves changes to the GPO.
        /// This method properly notifies the Group Policy infrastructure of changes,
        /// ensuring they appear in gpedit.msc and rsop.msc.
        /// </summary>
        /// <param name="isUser">True if saving user policy, false for machine policy.</param>
        /// <param name="addExtension">True if adding new policy settings (typical case).</param>
        /// <returns>True if save succeeded, false otherwise.</returns>
        public bool Save(bool isUser, bool addExtension = true)
        {
            if (_gpo == null)
            {
                _logger.LogDebug("[GPO] Cannot save: GPO not open");
                return false;
            }

            try
            {
                // Use the Registry Extension GUID which handles Administrative Templates
                Guid extensionGuid = GroupPolicyGuids.RegistryExtensionGuid;
                Guid snapInGuid = GroupPolicyGuids.MmcSnapInGuid;

                // Save the GPO
                // bMachine: True for machine section, false for user section
                // bAdd: True if adding/enabling the extension, false if removing
                int hr = _gpo.Save(!isUser, addExtension, extensionGuid, snapInGuid);
                
                if (hr != 0)
                {
                    _logger.LogDebug($"[GPO] Save failed with HRESULT: 0x{hr:X8}");
                    return false;
                }

                _logger.LogDebug($"[GPO] Successfully saved {(isUser ? "user" : "machine")} policy");
                
                // Force a policy refresh to apply changes immediately
                PInvoke.RefreshPolicyEx(!isUser, PInvoke.RP_FORCE);
                _logger.LogDebug($"[GPO] Invoked RefreshPolicyEx for {(isUser ? "user" : "machine")} policy");
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[GPO] Exception saving GPO: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets the file system path for the GPO's policy files.
        /// </summary>
        /// <param name="isUser">True for user section, false for machine section.</param>
        /// <returns>The file system path, or null on failure.</returns>
        public string? GetFilePath(bool isUser)
        {
            if (_gpo == null) return null;

            try
            {
                uint section = isUser ? (uint)GpoSection.User : (uint)GpoSection.Machine;
                int hr = ReadIntoBuffer(260, (ptr, cch) => _gpo.GetFileSysPath(section, ptr, cch), out string? path);

                if (hr != 0)
                {
                    _logger.LogDebug($"[GPO] GetFileSysPath failed with HRESULT: 0x{hr:X8}");
                    return null;
                }

                return path;
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[GPO] Exception getting file path: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets the display name of the GPO.
        /// </summary>
        /// <returns>The display name, or null on failure.</returns>
        public string? GetDisplayName()
        {
            if (_gpo == null) return null;

            try
            {
                int hr = ReadIntoBuffer(260, (ptr, cch) => _gpo.GetDisplayName(ptr, cch), out string? name);
                return hr != 0 ? null : name;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Invokes a Windows "fill a caller-allocated wide-char buffer" COM method against a pinned
        /// buffer and reads back the resulting null-terminated string. Replaces the
        /// <see cref="System.Text.StringBuilder"/> marshalling the interop source generator does not
        /// support; see <c>doc/NativeAot.md</c>, "COM interop".
        /// </summary>
        private static unsafe int ReadIntoBuffer(int capacity, Func<nint, int, int> fill, out string? value)
        {
            Span<char> buffer = capacity <= 512 ? stackalloc char[capacity] : new char[capacity];
            buffer.Clear();
            fixed (char* p = buffer)
            {
                int hr = fill((nint)p, capacity);
                value = hr == 0 ? new string(p) : null;
                return hr;
            }
        }

        /// <summary>
        /// Checks if IGroupPolicyObject COM interface is available on this system.
        /// </summary>
        /// <returns>True if the interface is available, false otherwise.</returns>
        public static bool IsAvailable()
        {
            try
            {
                // Try to create the COM object
                var gpo = ComActivator.CreateInstance<IGroupPolicyObject>(GroupPolicyObjectClsid.GroupPolicyObject);
                ComActivator.Release(gpo);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Disposes of the GPO wrapper and releases COM resources.
        /// </summary>
        /// <remarks>
        /// This type intentionally has no finalizer. The generated COM wrapper provides its own
        /// fallback finalizer; deterministic release must happen here, on the creating COM apartment.
        /// </remarks>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed || !disposing) return;

            if (_gpo is not null)
            {
                ApartmentState currentApartment = Thread.CurrentThread.GetApartmentState();
                bool isOwningApartment = _owningApartmentState == ApartmentState.MTA
                    ? currentApartment == ApartmentState.MTA
                    : Environment.CurrentManagedThreadId == _owningThreadId;
                if (!isOwningApartment)
                {
                    throw new InvalidOperationException(
                        "GroupPolicyObjectWrapper must be disposed from the COM apartment that created it.");
                }
            }

            IGroupPolicyObject? gpo = _gpo;
            _gpo = null;
            _disposed = true;

            if (gpo is not null)
            {
                ComActivator.Release(gpo);
            }
        }

        private void CaptureOwningApartment()
        {
            _owningThreadId = Environment.CurrentManagedThreadId;
            _owningApartmentState = Thread.CurrentThread.GetApartmentState();
        }
    }
}

