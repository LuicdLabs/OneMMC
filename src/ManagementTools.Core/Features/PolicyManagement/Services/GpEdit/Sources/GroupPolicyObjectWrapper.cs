using System;
using System.Runtime.InteropServices;
using System.Text;
using ManagementTools.Core.Features.PolicyManagement.Services.GpEdit.Native;
using ManagementTools.Core.Infrastructure.PolicyStorage;
using Microsoft.Win32;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ManagementTools.Core.Features.PolicyManagement.Services.GpEdit.Sources
{
    /// <summary>
    /// Wrapper class for IGroupPolicyObject COM interface.
    /// This wrapper provides a managed interface for manipulating Group Policy Objects
    /// through the official Windows API, ensuring changes are visible in gpedit.msc and rsop.msc.
    /// </summary>
    public class GroupPolicyObjectWrapper : IDisposable
    {
        private static ILogger _logger = NullLogger.Instance;
        private IGroupPolicyObject? _gpo;
        private bool _disposed;
        private IntPtr _machineRegistryHandle;
        private IntPtr _userRegistryHandle;

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
                wrapper._gpo = (IGroupPolicyObject)new GroupPolicyObjectClass();
                uint flags = forEditing ? (uint)GpoOpenFlags.Editing : (uint)GpoOpenFlags.LoadRegistry;
                int hr = wrapper._gpo.OpenLocalMachineGPO(flags);
                
                if (hr != 0)
                {
                    _logger.LogDebug($"[GPO] OpenLocalMachineGPO failed with HRESULT: 0x{hr:X8}");
                    Marshal.ReleaseComObject(wrapper._gpo);
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
                    Marshal.ReleaseComObject(wrapper._gpo);
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
                wrapper._gpo = (IGroupPolicyObject)new GroupPolicyObjectClass();
                uint flags = forEditing ? (uint)GpoOpenFlags.Editing : (uint)GpoOpenFlags.LoadRegistry;
                int hr = wrapper._gpo.OpenRemoteMachineGPO(computerName, flags);
                
                if (hr != 0)
                {
                    _logger.LogDebug($"[GPO] OpenRemoteMachineGPO failed with HRESULT: 0x{hr:X8}");
                    Marshal.ReleaseComObject(wrapper._gpo);
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
                    Marshal.ReleaseComObject(wrapper._gpo);
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

                // Store the handle for later cleanup
                if (isUser)
                    _userRegistryHandle = hKey;
                else
                    _machineRegistryHandle = hKey;

                // Create a RegistryKey from the handle
                // Use SafeRegistryHandle to wrap the native handle
                var safeHandle = new Microsoft.Win32.SafeHandles.SafeRegistryHandle(hKey, false);
                return RegistryKey.FromHandle(safeHandle);
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
                var gpoRegKey = GetRegistryKey(isUser);
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
                var pathBuilder = new StringBuilder(260);
                int hr = _gpo.GetFileSysPath(section, pathBuilder, pathBuilder.Capacity);
                
                if (hr != 0)
                {
                    _logger.LogDebug($"[GPO] GetFileSysPath failed with HRESULT: 0x{hr:X8}");
                    return null;
                }

                return pathBuilder.ToString();
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
                var nameBuilder = new StringBuilder(260);
                int hr = _gpo.GetDisplayName(nameBuilder, nameBuilder.Capacity);
                
                if (hr != 0) return null;
                return nameBuilder.ToString();
            }
            catch
            {
                return null;
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
                var gpo = (IGroupPolicyObject)new GroupPolicyObjectClass();
                Marshal.ReleaseComObject(gpo);
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
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Finalizer.
        /// </summary>
        ~GroupPolicyObjectWrapper()
        {
            Dispose(false);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (_gpo != null)
            {
                try
                {
                    Marshal.ReleaseComObject(_gpo);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug($"[GPO] Exception releasing COM object: {ex.Message}");
                }
                _gpo = null;
            }

            _disposed = true;
        }
    }
}


