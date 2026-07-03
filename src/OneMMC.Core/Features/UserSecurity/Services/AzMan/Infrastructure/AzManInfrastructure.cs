// ============================================================================
// AzMan Service - Helper Methods
// ============================================================================
// Helper methods: type conversion, error handling, path processing, etc.
// ============================================================================

using System;
using System.Collections.Generic;
using System.DirectoryServices;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using OneMMC.Core.Features.UserSecurity.Services.AzMan.Native;
using OneMMC.Core.Infrastructure.Interop;
using OneMMC.Core.Localization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OneMMC.Core.Features.UserSecurity.Models.AzMan;

namespace OneMMC.Core.Features.UserSecurity.Services.AzMan;

internal sealed class AzManInfrastructure
{
    private readonly AzManService _service;

    public AzManInfrastructure(AzManService service)
    {
        _service = service;
    }

    private object _lockObject => _service.LockObject;
    private List<AzAuthorizationStoreInfo> _openedStores => _service.OpenedStoresInternal;
    private TaskFactory _comTaskFactory => _service.ComTaskFactory;
    private ILogger<AzManService> _logger => _service.Logger;

    #region Helper Methods

    /// <summary>
    /// Ensure the store is open
    /// </summary>
    internal void EnsureStoreOpen(string storePath)
    {
        var storeInfo = _openedStores.Find(s =>
            s.StorePath.Equals(storePath, StringComparison.OrdinalIgnoreCase));

        if (storeInfo is not { } openedStore || _service.GetAuthStore(storePath) is null)
        {
            throw new InvalidOperationException($"Store '{storePath}' is not open. Please open the store first.");
        }

        // If XML store file is missing, automatically close the store
        if (openedStore.StoreType == AzStoreType.Xml)
        {
            string xmlPath = GetXmlFilePathFromStoreUrl(openedStore.StorePath);
            if (!File.Exists(xmlPath))
            {
                CloseStoreInternal(openedStore.StorePath);
                throw new InvalidOperationException($"Store file not found: {xmlPath}. The store has been closed.");
            }
        }
    }

    /// <summary>
    /// Close store without acquiring lock (assumes caller holds the lock).
    /// </summary>
    internal void CloseStoreInternal(string storePath)
    {
        var storeInfo = _openedStores.Find(s =>
            s.StorePath.Equals(storePath, StringComparison.OrdinalIgnoreCase));

        if (storeInfo != null)
        {
            _openedStores.Remove(storeInfo);

            _service.RemoveAuthStoreInstance(storePath);

            _logger.LogDebug($"[AzManService] Closed store: {storePath}");
        }
    }

    /// <summary>
    /// Normalize msxml:// store URL to local file path.
    /// </summary>
    internal static string GetXmlFilePathFromStoreUrl(string storeUrl)
    {
        if (storeUrl.StartsWith("msxml://", StringComparison.OrdinalIgnoreCase))
        {
            return storeUrl.Substring(8);
        }

        return storeUrl;
    }

    /// <summary>
    /// Ensure XML store schema version is set to 2.0.
    /// </summary>
    internal void EnsureXmlStoreSchemaV2(string storeUrl)
    {
        try
        {
            string xmlPath = GetXmlFilePathFromStoreUrl(storeUrl);
            if (!File.Exists(xmlPath))
            {
                return;
            }

            var doc = XDocument.Load(xmlPath, LoadOptions.PreserveWhitespace);
            var root = doc.Root;
            if (root == null)
            {
                return;
            }

            root.SetAttributeValue("MajorVersion", "2");
            root.SetAttributeValue("MinorVersion", "0");

            doc.Save(xmlPath);
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"[AzManService] Failed to update XML schema version: {ex.Message}");
        }
    }

    /// <summary>
    /// Ensure the Active Directory authorization store schema is version 2.0.
    /// Mirrors <see cref="EnsureXmlStoreSchemaV2"/> for Active Directory-backed stores.
    /// Two strategies are attempted in order:
    /// <list type="number">
    ///   <item>Open a dedicated <c>MANAGE_STORE_ONLY</c> handle and call
    ///         <c>UpgradeStoresFunctionalLevel(1)</c> (NT6 level).</item>
    ///   <item>Write version attributes directly via ADSI
    ///         (<see cref="System.DirectoryServices.DirectoryEntry"/>).</item>
    /// </list>
    /// </summary>
    internal void EnsureAdStoreSchemaV2(string storeUrl)
    {
        if (TryUpgradeAdStoreViaDedicatedHandle(storeUrl))
        {
            _logger.LogInformation(
                "[AzManService] Upgraded AD store schema to 2.0 via UpgradeStoresFunctionalLevel: {StoreUrl}",
                storeUrl);
            return;
        }

        _logger.LogDebug(
            "[AzManService] UpgradeStoresFunctionalLevel did not succeed; falling back to ADSI for {StoreUrl}",
            storeUrl);

        UpgradeAdStoreSchemaViaAdsi(storeUrl);
    }

    // AZ_AZSTORE_NT6_POLICY_LEVEL = 1 → Windows Server 2008+ (AzMan schema 2.0)
    private const int AZ_AZSTORE_NT6_POLICY_LEVEL = 1;

    /// <summary>
    /// Read the persisted schema version of an Active Directory authorization store via ADSI.
    /// </summary>
    /// <remarks>
    /// <para>
    /// AzRoles COM does NOT natively expose <c>MajorVersion</c>/<c>MinorVersion</c> for
    /// AD stores via its COM properties on the <c>IAzAuthorizationStore</c> object directly
    /// (it will always yield <c>1.0</c> if queried dynamically).
    /// To accurately reflect the stored schema, we read the version attributes directly from
    /// the directory using <see cref="System.DirectoryServices.DirectoryEntry"/>.
    /// </para>
    /// </remarks>
    /// <returns>
    /// A tuple with the major and minor version read from AD, or <c>(1, 0)</c> if the
    /// properties are unavailable or cannot be read.
    /// </returns>
    internal (int MajorVersion, int MinorVersion) ReadAdStoreSchemaVersion(string storeUrl)
    {
        string ldapPath = storeUrl.StartsWith("msldap://", StringComparison.OrdinalIgnoreCase)
            ? "LDAP://" + storeUrl[9..]
            : storeUrl;

        try
        {
            using var entry = new DirectoryEntry(ldapPath);
            entry.RefreshCache();

            int major = 1;
            int minor = 0;

            var candidates = new[]
            {
                ("msDS-AzMajorVersion", "msDS-AzMinorVersion"),
                ("AZMajorVersion",      "AZMinorVersion"),
                ("azMajorVersion",      "azMinorVersion"),
                ("MajorVersion",        "MinorVersion"),
            };

            foreach (var (majorName, minorName) in candidates)
            {
                if (entry.Properties.Contains(majorName) && entry.Properties[majorName].Value != null)
                {
                    major = Convert.ToInt32(entry.Properties[majorName].Value);

                    if (entry.Properties.Contains(minorName) && entry.Properties[minorName].Value != null)
                    {
                        minor = Convert.ToInt32(entry.Properties[minorName].Value);
                    }

                    _logger.LogDebug(
                        "[AzManService] AD store schema version read as {Major}.{Minor} via ADSI ({MajorName}/{MinorName}): {StoreUrl}",
                        major, minor, majorName, minorName, storeUrl);

                    return (major, minor);
                }
            }

            _logger.LogDebug(
                "[AzManService] No version attributes found via ADSI for {StoreUrl}. Returning default (1.0).",
                storeUrl);

            return (major, minor);
        }
        catch (System.DirectoryServices.ActiveDirectory.ActiveDirectoryOperationException ex)
        {
            _logger.LogWarning(ex,
                "[AzManService] Unable to connect to Active Directory for {StoreUrl}. This computer may not be joined to a domain. Returning (1, 0).",
                storeUrl);
            return (1, 0);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                "[AzManService] ReadAdStoreSchemaVersion via ADSI failed for {StoreUrl}: {Message}. Returning (1, 0).",
                storeUrl, ex.Message);
            return (1, 0);
        }
    }

    /// <summary>
    /// Open a transient MANAGE_STORE_ONLY COM handle and call UpgradeStoresFunctionalLevel(1).
    /// Returns <c>true</c> on success.
    /// </summary>
    private bool TryUpgradeAdStoreViaDedicatedHandle(string storeUrl)
    {
        IAzAuthorizationStore3? tempStore = null;
        try
        {
            tempStore = AzRolesCom.CreateStore();
            tempStore.Initialize(AzManService.AZ_AZSTORE_FLAG_MANAGE_STORE_ONLY, storeUrl, Variant.Missing);
            tempStore.UpgradeStoresFunctionalLevel(AZ_AZSTORE_NT6_POLICY_LEVEL);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                "[AzManService] UpgradeStoresFunctionalLevel via MANAGE_STORE_ONLY failed for {StoreUrl}: {Message}",
                storeUrl, ex.Message);
            return false;
        }
        finally
        {
            if (tempStore != null)
                try { AzRolesCom.Release(tempStore); } catch { }
        }
    }

    /// <summary>
    /// Write MajorVersion/MinorVersion attributes directly on the AD object via ADSI.
    /// First tries to discover existing version attributes in the already-populated
    /// property set; if absent (newly created store), tries a list of known candidate
    /// attribute names from the AzMan AD schema extension.
    /// </summary>
    private void UpgradeAdStoreSchemaViaAdsi(string storeUrl)
    {
        string ldapPath = storeUrl.StartsWith("msldap://", StringComparison.OrdinalIgnoreCase)
            ? "LDAP://" + storeUrl[9..]
            : storeUrl;

        try
        {
            using var entry = new DirectoryEntry(ldapPath);
            entry.RefreshCache();

            // Log every attribute at DEBUG level so the actual AD schema naming is visible
            // in diagnostics — useful when the candidate list needs to be extended.
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                var sb = new StringBuilder();
                foreach (PropertyValueCollection pvc in entry.Properties)
                    sb.Append(pvc.PropertyName).Append('=').Append(pvc.Value).Append("; ");
                _logger.LogDebug(
                    "[AzManService] AD store ADSI properties at {LdapPath}: [{Properties}]",
                    ldapPath, sb.ToString());
            }

            // Phase 1 – discover version attributes from already-populated properties.
            string? majorAttr = null, minorAttr = null;
            foreach (PropertyValueCollection pvc in entry.Properties)
            {
                var lower = pvc.PropertyName.ToLowerInvariant();
                if (majorAttr == null && lower.Contains("major") && lower.Contains("version"))
                    majorAttr = pvc.PropertyName;
                else if (minorAttr == null && lower.Contains("minor") && lower.Contains("version"))
                    minorAttr = pvc.PropertyName;
            }

            if (majorAttr != null)
            {
                entry.Properties[majorAttr].Value = 2;
                if (minorAttr != null)
                    entry.Properties[minorAttr].Value = 0;
                entry.CommitChanges();
                _logger.LogInformation(
                    "[AzManService] Set AD store schema to 2.0 via ADSI (discovered attrs {Major}/{Minor}): {LdapPath}",
                    majorAttr, minorAttr, ldapPath);
                return;
            }

            // Phase 2 – attribute not set yet; try candidate names from the AzMan schema extension.
            // Optional AD attributes can be added even when not currently populated.
            var candidates = new[]
            {
                ("msDS-AzMajorVersion", "msDS-AzMinorVersion"),
                ("AZMajorVersion",      "AZMinorVersion"),
                ("azMajorVersion",      "azMinorVersion"),
                ("MajorVersion",        "MinorVersion"),
            };

            Exception? lastEx = null;
            foreach (var (majorName, minorName) in candidates)
            {
                try
                {
                    // Use a fresh DirectoryEntry for each attempt so prior failed writes
                    // don't pollute the property cache.
                    using var attempt = new DirectoryEntry(ldapPath);
                    attempt.Properties[majorName].Value = 2;
                    attempt.Properties[minorName].Value = 0;
                    attempt.CommitChanges();
                    _logger.LogInformation(
                        "[AzManService] Set AD store schema to 2.0 via ADSI (candidate attrs {Major}/{Minor}): {LdapPath}",
                        majorName, minorName, ldapPath);
                    return;
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    _logger.LogDebug(
                        "[AzManService] ADSI candidate ({Major}/{Minor}) failed: {Message}",
                        majorName, minorName, ex.Message);
                }
            }

            var message =
                $"Could not set AD authorization store schema to version 2.0 at '{storeUrl}'. " +
                $"No writable version attributes were found or accepted by the Active Directory. " +
                $"Please upgrade the store schema manually using azman.msc.";

            if (lastEx is not null)
            {
                throw new AzManException(message, lastEx);
            }

            throw new AzManException(message);
        }
        catch (System.DirectoryServices.ActiveDirectory.ActiveDirectoryOperationException ex)
        {
            _logger.LogWarning(ex,
                "[AzManService] Unable to connect to Active Directory for {StoreUrl}. This computer may not be joined to a domain.",
                storeUrl);
            throw new AzManException(
                $"Unable to connect to Active Directory. This computer may not be joined to a domain.", ex);
        }
        catch (AzManException) { throw; }
        catch (Exception ex)
        {
            throw new AzManException(
                $"Failed to set AD store schema to version 2.0 via ADSI: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Get the current authorization store, ensuring it is open.
    /// </summary>
    internal IAzAuthorizationStore3 GetAuthStoreOrThrow(string storePath)
    {
        EnsureStoreOpen(storePath);
        return _service.GetAuthStore(storePath)!;
    }

    internal Task RunComAsync(Action action)
    {
        return _comTaskFactory.StartNew(action);
    }

    internal Task<T> RunComAsync<T>(Func<T> func)
    {
        return _comTaskFactory.StartNew(func);
    }

    /// <summary>
    /// Run a store write operation with lock, error handling, and Submit.
    /// </summary>
    internal Task RunStoreWriteAsync(string storePath, Action<IAzAuthorizationStore3> action, string errorMessage, string? debugMessage = null)
    {
        return RunComAsync(() =>
        {
            lock (_lockObject)
            {
                try
                {
                    IAzAuthorizationStore3 store = GetAuthStoreOrThrow(storePath);
                    action(store);
                    store.Submit(0, Variant.Missing);

                    if (!string.IsNullOrEmpty(debugMessage))
                    {
                        _logger.LogDebug(debugMessage);
                    }
                }
                catch (COMException ex)
                {
                    throw new AzManException($"{errorMessage}: {GetComErrorMessage(ex)}", ex);
                }
                catch (Exception ex) when (ex is not AzManException)
                {
                    throw new AzManException($"{errorMessage}: {ex.Message}", ex);
                }
            }
        });
    }

    /// <summary>
    /// Run a store read operation with lock and error handling.
    /// </summary>
    internal Task<T> RunStoreReadAsync<T>(string storePath, Func<IAzAuthorizationStore3, T> func, string errorMessage)
    {
        return RunComAsync(() =>
        {
            lock (_lockObject)
            {
                try
                {
                    var store = GetAuthStoreOrThrow(storePath);
                    return func(store);
                }
                catch (COMException ex)
                {
                    throw new AzManException($"{errorMessage}: {GetComErrorMessage(ex)}", ex);
                }
                catch (Exception ex) when (ex is not AzManException)
                {
                    throw new AzManException($"{errorMessage}: {ex.Message}", ex);
                }
            }
        });
    }

    /// <summary>
    /// Run an application write operation with lock, error handling, and Submit.
    /// </summary>
    internal Task RunApplicationWriteAsync(
        string storePath,
        string appName,
        Action<IAzApplication> action,
        string errorMessage,
        string? debugMessage = null,
        bool submitStore = false)
    {
        return RunComAsync(() =>
        {
            lock (_lockObject)
            {
                try
                {
                    IAzAuthorizationStore3 store = GetAuthStoreOrThrow(storePath);
                    store.OpenApplication(appName, Variant.Missing, out IAzApplication app);
                    try
                    {
                        action(app);
                        app.Submit(0, Variant.Missing);
                    }
                    finally
                    {
                        AzRolesCom.Release(app);
                    }

                    if (submitStore)
                    {
                        store.Submit(0, Variant.Missing);
                    }

                    if (!string.IsNullOrEmpty(debugMessage))
                    {
                        _logger.LogDebug(debugMessage);
                    }
                }
                catch (COMException ex)
                {
                    throw new AzManException($"{errorMessage}: {GetComErrorMessage(ex)}", ex);
                }
                catch (Exception ex) when (ex is not AzManException)
                {
                    throw new AzManException($"{errorMessage}: {ex.Message}", ex);
                }
            }
        });
    }

    /// <summary>
    /// Run an application read operation with lock and error handling.
    /// </summary>
    internal Task<T> RunApplicationReadAsync<T>(string storePath, string appName, Func<IAzApplication, T> func, string errorMessage)
    {
        return RunComAsync(() =>
        {
            lock (_lockObject)
            {
                try
                {
                    IAzAuthorizationStore3 store = GetAuthStoreOrThrow(storePath);
                    store.OpenApplication(appName, Variant.Missing, out IAzApplication app);
                    try
                    {
                        return func(app);
                    }
                    finally
                    {
                        AzRolesCom.Release(app);
                    }
                }
                catch (COMException ex)
                {
                    throw new AzManException($"{errorMessage}: {GetComErrorMessage(ex)}", ex);
                }
                catch (Exception ex) when (ex is not AzManException)
                {
                    throw new AzManException($"{errorMessage}: {ex.Message}", ex);
                }
            }
        });
    }

    /// <summary>
    /// Run a store-level group write operation with lock, error handling, and Submit.
    /// </summary>
    internal Task RunStoreGroupWriteAsync(
        string storePath,
        string groupName,
        Action<IAzApplicationGroup2> action,
        string errorMessage,
        string? debugMessage = null,
        bool submitStore = true)
    {
        return RunComAsync(() =>
        {
            lock (_lockObject)
            {
                try
                {
                    IAzAuthorizationStore3 store = GetAuthStoreOrThrow(storePath);
                    store.OpenApplicationGroup(groupName, Variant.Missing, out IAzApplicationGroup2 group);
                    try
                    {
                        action(group);
                        group.Submit(0, Variant.Missing);
                    }
                    finally
                    {
                        AzRolesCom.Release(group);
                    }

                    if (submitStore)
                    {
                        store.Submit(0, Variant.Missing);
                    }

                    if (!string.IsNullOrEmpty(debugMessage))
                    {
                        _logger.LogDebug(debugMessage);
                    }
                }
                catch (COMException ex)
                {
                    throw new AzManException($"{errorMessage}: {GetComErrorMessage(ex)}", ex);
                }
                catch (Exception ex) when (ex is not AzManException)
                {
                    throw new AzManException($"{errorMessage}: {ex.Message}", ex);
                }
            }
        });
    }

    /// <summary>
    /// Run an application-level group write operation with lock, error handling, and Submit.
    /// </summary>
    internal Task RunAppGroupWriteAsync(
        string storePath,
        string appName,
        string groupName,
        Action<IAzApplicationGroup2> action,
        string errorMessage,
        string? debugMessage = null,
        bool submitApp = true)
    {
        return RunComAsync(() =>
        {
            lock (_lockObject)
            {
                try
                {
                    IAzAuthorizationStore3 store = GetAuthStoreOrThrow(storePath);
                    store.OpenApplication(appName, Variant.Missing, out IAzApplication app);
                    IAzApplicationGroup2? group = null;
                    try
                    {
                        app.OpenApplicationGroup(groupName, Variant.Missing, out group);
                        action(group);
                        group.Submit(0, Variant.Missing);

                        if (submitApp)
                        {
                            app.Submit(0, Variant.Missing);
                        }
                    }
                    finally
                    {
                        AzRolesCom.Release(group);
                        AzRolesCom.Release(app);
                    }

                    if (!string.IsNullOrEmpty(debugMessage))
                    {
                        _logger.LogDebug(debugMessage);
                    }
                }
                catch (COMException ex)
                {
                    throw new AzManException($"{errorMessage}: {GetComErrorMessage(ex)}", ex);
                }
                catch (Exception ex) when (ex is not AzManException)
                {
                    throw new AzManException($"{errorMessage}: {ex.Message}", ex);
                }
            }
        });
    }

    /// <summary>
    /// Run a role assignment write operation with lock, error handling, and Submit.
    /// </summary>
    internal Task RunRoleWriteAsync(
        string storePath,
        string appName,
        string roleName,
        Action<IAzRole> action,
        string errorMessage,
        string? debugMessage = null)
    {
        return RunComAsync(() =>
        {
            lock (_lockObject)
            {
                try
                {
                    IAzAuthorizationStore3 store = GetAuthStoreOrThrow(storePath);
                    store.OpenApplication(appName, Variant.Missing, out IAzApplication app);
                    IAzRole? role = null;
                    try
                    {
                        app.OpenRole(roleName, Variant.Missing, out role);
                        action(role);
                        role.Submit(0, Variant.Missing);
                        app.Submit(0, Variant.Missing);
                    }
                    finally
                    {
                        AzRolesCom.Release(role);
                        AzRolesCom.Release(app);
                    }

                    if (!string.IsNullOrEmpty(debugMessage))
                    {
                        _logger.LogDebug(debugMessage);
                    }
                }
                catch (COMException ex)
                {
                    throw new AzManException($"{errorMessage}: {GetComErrorMessage(ex)}", ex);
                }
                catch (Exception ex) when (ex is not AzManException)
                {
                    throw new AzManException($"{errorMessage}: {ex.Message}", ex);
                }
            }
        });
    }

    /// <summary>
    /// Run a task (including role definition task) write operation with lock, error handling, and Submit.
    /// </summary>
    internal Task RunTaskWriteAsync(
        string storePath,
        string appName,
        string taskName,
        Action<IAzTask> action,
        string errorMessage,
        string? debugMessage = null)
    {
        return RunComAsync(() =>
        {
            lock (_lockObject)
            {
                try
                {
                    IAzAuthorizationStore3 store = GetAuthStoreOrThrow(storePath);
                    store.OpenApplication(appName, Variant.Missing, out IAzApplication app);
                    IAzTask? task = null;
                    try
                    {
                        app.OpenTask(taskName, Variant.Missing, out task);
                        action(task);
                        task.Submit(0, Variant.Missing);
                        app.Submit(0, Variant.Missing);
                    }
                    finally
                    {
                        AzRolesCom.Release(task);
                        AzRolesCom.Release(app);
                    }

                    if (!string.IsNullOrEmpty(debugMessage))
                    {
                        _logger.LogDebug(debugMessage);
                    }
                }
                catch (COMException ex)
                {
                    throw new AzManException($"{errorMessage}: {GetComErrorMessage(ex)}", ex);
                }
                catch (Exception ex) when (ex is not AzManException)
                {
                    throw new AzManException($"{errorMessage}: {ex.Message}", ex);
                }
            }
        });
    }

    /// <summary>
    /// Run an operation write operation with lock, error handling, and Submit.
    /// </summary>
    internal Task RunOperationWriteAsync(
        string storePath,
        string appName,
        string operationName,
        Action<IAzOperation> action,
        string errorMessage,
        string? debugMessage = null)
    {
        return RunComAsync(() =>
        {
            lock (_lockObject)
            {
                try
                {
                    IAzAuthorizationStore3 store = GetAuthStoreOrThrow(storePath);
                    store.OpenApplication(appName, Variant.Missing, out IAzApplication app);
                    IAzOperation? operation = null;
                    try
                    {
                        app.OpenOperation(operationName, Variant.Missing, out operation);
                        action(operation);
                        operation.Submit(0, Variant.Missing);
                        app.Submit(0, Variant.Missing);
                    }
                    finally
                    {
                        AzRolesCom.Release(operation);
                        AzRolesCom.Release(app);
                    }

                    if (!string.IsNullOrEmpty(debugMessage))
                    {
                        _logger.LogDebug(debugMessage);
                    }
                }
                catch (COMException ex)
                {
                    throw new AzManException($"{errorMessage}: {GetComErrorMessage(ex)}", ex);
                }
                catch (Exception ex) when (ex is not AzManException)
                {
                    throw new AzManException($"{errorMessage}: {ex.Message}", ex);
                }
            }
        });
    }

    /// <summary>
    /// Run a scope write operation with lock, error handling, and Submit.
    /// </summary>
    internal Task RunScopeWriteAsync(
        string storePath,
        string appName,
        string scopeName,
        Action<IAzScope> action,
        string errorMessage,
        string? debugMessage = null,
        bool submitScope = true,
        bool submitApp = false)
    {
        return RunComAsync(() =>
        {
            lock (_lockObject)
            {
                try
                {
                    IAzAuthorizationStore3 store = GetAuthStoreOrThrow(storePath);
                    store.OpenApplication(appName, Variant.Missing, out IAzApplication app);
                    IAzScope? scope = null;
                    try
                    {
                        app.OpenScope(scopeName, Variant.Missing, out scope);
                        action(scope);

                        if (submitScope)
                        {
                            scope.Submit(0, Variant.Missing);
                        }

                        if (submitApp)
                        {
                            app.Submit(0, Variant.Missing);
                        }
                    }
                    finally
                    {
                        AzRolesCom.Release(scope);
                        AzRolesCom.Release(app);
                    }

                    if (!string.IsNullOrEmpty(debugMessage))
                    {
                        _logger.LogDebug(debugMessage);
                    }
                }
                catch (COMException ex)
                {
                    throw new AzManException($"{errorMessage}: {GetComErrorMessage(ex)}", ex);
                }
                catch (Exception ex) when (ex is not AzManException)
                {
                    throw new AzManException($"{errorMessage}: {ex.Message}", ex);
                }
            }
        });
    }

    /// <summary>
    /// Run a scope read operation with lock and error handling.
    /// </summary>
    internal Task<T> RunScopeReadAsync<T>(string storePath, string appName, string scopeName, Func<IAzScope, T> func, string errorMessage)
    {
        return RunComAsync(() =>
        {
            lock (_lockObject)
            {
                try
                {
                    IAzAuthorizationStore3 store = GetAuthStoreOrThrow(storePath);
                    store.OpenApplication(appName, Variant.Missing, out IAzApplication app);
                    IAzScope? scope = null;
                    try
                    {
                        app.OpenScope(scopeName, Variant.Missing, out scope);
                        return func(scope);
                    }
                    finally
                    {
                        AzRolesCom.Release(scope);
                        AzRolesCom.Release(app);
                    }
                }
                catch (COMException ex)
                {
                    throw new AzManException($"{errorMessage}: {GetComErrorMessage(ex)}", ex);
                }
                catch (Exception ex) when (ex is not AzManException)
                {
                    throw new AzManException($"{errorMessage}: {ex.Message}", ex);
                }
            }
        });
    }

    /// <summary>
    /// Extract name from store path
    /// </summary>
    internal static string ExtractStoreName(string path)
    {
        if (string.IsNullOrEmpty(path))
            return "Unknown";

        // For XML files, extract filename
        if (path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            return System.IO.Path.GetFileName(path);
        }

        // For LDAP paths, extract CN name
        if (path.Contains("CN=", StringComparison.OrdinalIgnoreCase))
        {
            var cnMatch = Regex.Match(path, @"CN=([^,]+)", RegexOptions.IgnoreCase);
            if (cnMatch.Success)
            {
                return cnMatch.Groups[1].Value;
            }
        }

        // For SQL Server, extract database name
        if (path.Contains("/"))
        {
            var parts = path.Split('/');
            return parts.Length > 0 ? parts[^1] : path;
        }

        return path;
    }

    /// <summary>
    /// Get COM error message
    /// </summary>
    internal static string GetComErrorMessage(COMException ex)
    {
        return ex.ErrorCode switch
        {
            unchecked((int)0x80070005) => LocalizationProvider.Current.GetString(ResourceFileNames.AzMan, AzManKeys.AccessDenied),
            unchecked((int)0x80070002) => "The specified file was not found.",
            unchecked((int)0x80070003) => "The specified path was not found.",
            unchecked((int)0x80070035) => "Network path not found. Please check that the path is correct and accessible.",
            unchecked((int)0x80070050) => "The file already exists.",
            unchecked((int)0x80070490) => "Element not found.",
            unchecked((int)0x80004002) => "Interface not supported. Please ensure AzMan components are properly installed.",
            unchecked((int)0x800401F3) => "Invalid class string. AzMan COM component may not be registered.",
            unchecked((int)0x80040154) => "Class not registered. Please ensure AzMan components are installed.",
            _ => $"{ex.Message} (Error code: 0x{ex.ErrorCode:X8})"
        };
    }

    /// <summary>
    /// Try to read version information from XML file
    /// </summary>
    internal void TryReadVersionFromXml(string storeUrl, ref AzAuthorizationStoreInfo info)
    {
        try
        {
            // Remove msxml:// prefix
            string xmlPath = storeUrl;
            if (xmlPath.StartsWith("msxml://", StringComparison.OrdinalIgnoreCase))
            {
                xmlPath = xmlPath.Substring(8);
            }

            if (!System.IO.File.Exists(xmlPath))
            {
                return;
            }

            // Read first few lines of XML file to extract version
            using var reader = new System.IO.StreamReader(xmlPath);
            string? line;
            int lineCount = 0;
            while ((line = reader.ReadLine()) != null && lineCount < 10)
            {
                lineCount++;
                if (line.Contains("<AzAdminManager"))
                {
                    // Try to extract MajorVersion
                    var majorMatch = Regex.Match(line, @"MajorVersion=""(\d+)""");
                    if (majorMatch.Success && int.TryParse(majorMatch.Groups[1].Value, out int major))
                    {
                        info.MajorVersion = major;
                    }

                    // Try to extract MinorVersion
                    var minorMatch = Regex.Match(line, @"MinorVersion=""(\d+)""");
                    if (minorMatch.Success && int.TryParse(minorMatch.Groups[1].Value, out int minor))
                    {
                        info.MinorVersion = minor;
                    }

                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"[AzManService] Failed to read version information from XML: {ex.Message}");
        }
    }

    #endregion
}
