// ============================================================================
// AzMan Service - Store Management
// ============================================================================
// Store management functions: create, open, close, delete, refresh stores
// ============================================================================

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using OneMMC.Core.Features.UserSecurity.Models.AzMan;
using OneMMC.Core.Features.UserSecurity.Services.AzMan.Native;
using OneMMC.Core.Infrastructure.Interop;
using Microsoft.Extensions.Logging;

namespace OneMMC.Core.Features.UserSecurity.Services.AzMan;

internal sealed class StoreManagement
{
    private readonly AzManService _service;

    public StoreManagement(AzManService service)
    {
        _service = service;
    }

    private object _lockObject => _service.LockObject;
    private List<AzAuthorizationStoreInfo> _openedStores => _service.OpenedStoresInternal;
    private ILogger<AzManService> _logger => _service.Logger;

    private int AZ_AZSTORE_FLAG_CREATE => AzManService.AZ_AZSTORE_FLAG_CREATE;
    private int AZ_AZSTORE_FLAG_MANAGE_STORE_ONLY => AzManService.AZ_AZSTORE_FLAG_MANAGE_STORE_ONLY;
    private int AZ_AZSTORE_FLAG_BATCH_UPDATE => AzManService.AZ_AZSTORE_FLAG_BATCH_UPDATE;

    private Task RunComAsync(Action action) => _service.RunComAsync(action);
    private Task<T> RunComAsync<T>(Func<T> func) => _service.RunComAsync(func);
    private Task<T> RunStoreReadAsync<T>(string storePath, Func<IAzAuthorizationStore3, T> func, string errorMessage)
        => _service.RunStoreReadAsync(storePath, func, errorMessage);
    private Task RunStoreWriteAsync(string storePath, Action<IAzAuthorizationStore3> action, string errorMessage, string? debugMessage = null)
        => _service.RunStoreWriteAsync(storePath, action, errorMessage, debugMessage);
    private void CloseStoreInternal(string storePath) => _service.CloseStoreInternal(storePath);
    private static string ExtractStoreName(string path) => AzManService.ExtractStoreName(path);
    private static string GetComErrorMessage(COMException ex) => AzManService.GetComErrorMessage(ex);
    private static string GetXmlFilePathFromStoreUrl(string storeUrl) => AzManService.GetXmlFilePathFromStoreUrl(storeUrl);
    private AzAuthorizationStoreInfo ReadStoreInfo(IAzAuthorizationStore3 store, string storeUrl, AzStoreType storeType)
        => _service.ReadStoreInfo(store, storeUrl, storeType);
    private void EnsureXmlStoreSchemaV2(string storeUrl) => _service.EnsureXmlStoreSchemaV2(storeUrl);
    private void EnsureAdStoreSchemaV2(string storeUrl) => _service.EnsureAdStoreSchemaV2(storeUrl);

    private static bool IsXmlStore(string storePath)
        => storePath.StartsWith("msxml://", StringComparison.OrdinalIgnoreCase);

    private static bool IsActiveDirectoryStore(string storePath)
        => storePath.StartsWith("msldap://", StringComparison.OrdinalIgnoreCase);

    #region Store Management

    /// <summary>
    /// Create a new authorization store
    /// </summary>
    /// <param name="parameters">Creation parameters</param>
    /// <returns>Created store information</returns>
    public async Task<AzAuthorizationStoreInfo> CreateStoreAsync(CreateStoreParameters parameters)
    {
        return await RunComAsync(() =>
        {
            lock (_lockObject)
            {
                try
                {
                    // Create AzAuthorizationStore COM object
                    IAzAuthorizationStore3 store = AzRolesCom.CreateStore();

                    // Initialize store (create mode)
                    string storeUrl = parameters.GetStoreUrl();
                    store.Initialize(AZ_AZSTORE_FLAG_CREATE, storeUrl, Variant.Missing);

                    // Set description
                    if (!string.IsNullOrEmpty(parameters.Description))
                    {
                        store.put_Description(parameters.Description);
                    }

                    // Set auditing
                    store.put_GenerateAudits(AzRolesCom.FromBool(parameters.GenerateAudits));

                    // Submit the initial store so it is persisted.
                    store.Submit(0, Variant.Missing);

                    // Upgrade schema to 2.0.
                    // For XML stores: patch the file directly (COM API doesn't expose version attrs).
                    // For AD stores: EnsureAdStoreSchemaV2 tries two strategies:
                    //   1. UpgradeStoresFunctionalLevel(1) via a fresh MANAGE_STORE_ONLY handle
                    //      (the CREATE/BATCH_UPDATE open modes cause E_INVALIDARG).
                    //   2. ADSI DirectoryEntry attribute write as fallback.
                    // Failure is non-fatal: the store exists at schema 1.0 and can be upgraded
                    // later via Store Properties dialog.
                    if (parameters.StoreType == AzStoreType.ActiveDirectory)
                    {
                        try
                        {
                            EnsureAdStoreSchemaV2(storeUrl);
                            // Refresh the COM object so the returned storeInfo shows new elements.
                            try { store.UpdateCache(Variant.Missing); } catch { }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(
                                "[AzManService] Could not upgrade new AD store schema to 2.0 at '{StoreUrl}': {Message}. " +
                                "Store created at default schema version.",
                                storeUrl, ex.Message);
                        }
                    }
                    else if (parameters.StoreType == AzStoreType.Xml)
                    {
                        EnsureXmlStoreSchemaV2(storeUrl);
                    }

                    // Read back the live COM state.
                    var storeInfo = ReadStoreInfo(store, storeUrl, parameters.StoreType);

                    _service.SetAuthStore(storeUrl, store);
                    _openedStores.Add(storeInfo);

                    _logger.LogInformation("Successfully created store: {StoreUrl}", storeUrl);
                    return storeInfo;
                }
                catch (COMException ex)
                {
                    _logger.LogError(ex, "COM error creating store. ErrorCode=0x{ErrorCode:X8}", ex.ErrorCode);
                    throw new AzManException($"Failed to create authorization store: {GetComErrorMessage(ex)}", ex);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error creating store");
                    throw new AzManException($"Failed to create authorization store: {ex.Message}", ex);
                }
            }
        });
    }

    /// <summary>
    /// Open an existing authorization store
    /// </summary>
    /// <param name="parameters">Open parameters</param>
    /// <returns>Store information</returns>
    public async Task<AzAuthorizationStoreInfo> OpenStoreAsync(OpenStoreParameters parameters)
    {
        return await RunComAsync(() =>
        {
            lock (_lockObject)
            {
                try
                {
                    // Check if the same store is already open
                    string storeUrl = parameters.GetStoreUrl();
                    var existing = _openedStores.Find(s =>
                        s.StorePath.Equals(storeUrl, StringComparison.OrdinalIgnoreCase));
                    if (existing != null)
                    {
                        return existing;
                    }

                    // Pre-check if XML file exists (if XML type)
                    if (parameters.StoreType == AzStoreType.Xml)
                    {
                        string path = parameters.Path;
                        // Handle msxml:// prefix
                        if (path.StartsWith("msxml://", StringComparison.OrdinalIgnoreCase))
                        {
                            path = path.Substring(8);
                        }

                        // Check local file
                        if (!path.StartsWith(@"\\") && !System.IO.File.Exists(path))
                        {
                            throw new System.IO.FileNotFoundException($"File not found: {path}", path);
                        }
                    }

                    // Create AzAuthorizationStore COM object
                    IAzAuthorizationStore3 store = AzRolesCom.CreateStore();

                    // Initialize store (open mode)
                    int flags = parameters.ReadOnly ? AZ_AZSTORE_FLAG_MANAGE_STORE_ONLY : AZ_AZSTORE_FLAG_BATCH_UPDATE;
                    store.Initialize(flags, storeUrl, Variant.Missing);

                    // Read store information
                    var storeInfo = ReadStoreInfo(store, storeUrl, parameters.StoreType);

                    _service.SetAuthStore(storeUrl, store);
                    _openedStores.Add(storeInfo);

                    _logger.LogInformation("Successfully opened store: {StoreUrl}", storeUrl);
                    return storeInfo;
                }
                catch (COMException ex)
                {
                    _logger.LogError(ex, "COM error opening store. ErrorCode=0x{ErrorCode:X8}", ex.ErrorCode);
                    throw new AzManException($"Failed to open authorization store: {GetComErrorMessage(ex)}", ex);
                }
                catch (System.IO.FileNotFoundException ex)
                {
                    _logger.LogWarning(ex, "Store file not found while opening store");
                    throw new AzManException($"Failed to open authorization store: File or path not accessible ({ex.Message}).", ex);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error opening store");
                    throw new AzManException($"Failed to open authorization store: {ex.Message}", ex);
                }
            }
        });
    }

    /// <summary>
    /// Close the specified store
    /// </summary>
    /// <param name="storePath">Store path</param>
    public void CloseStore(string storePath)
    {
        RunComAsync(() =>
        {
            lock (_lockObject)
            {
                CloseStoreInternal(storePath);
            }
        }).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Delete an authorization store
    /// </summary>
    /// <param name="storePath">Store path</param>
    public async Task DeleteStoreAsync(string storePath)
    {
        await RunComAsync(() =>
        {
            lock (_lockObject)
            {
                try
                {
                    // Close the store first
                    CloseStoreInternal(storePath);

                    // Create new COM object to delete
                    IAzAuthorizationStore3 store = AzRolesCom.CreateStore();
                    try
                    {
                        store.Initialize(AZ_AZSTORE_FLAG_MANAGE_STORE_ONLY, storePath, Variant.Missing);
                        store.Delete(Variant.Missing);
                    }
                    finally
                    {
                        AzRolesCom.Release(store);
                    }

                    _logger.LogInformation("Successfully deleted store: {StorePath}", storePath);
                }
                catch (COMException ex)
                {
                    // 0x80070002 (ERROR_FILE_NOT_FOUND): the AD object (or XML file) no longer exists
                    // at the given path, e.g. it was already deleted by another tool.
                    if (ex.ErrorCode == unchecked((int)0x80070002))
                    {
                        _logger.LogWarning(ex, "Store not found while attempting deletion: {StorePath}", storePath);
                        throw new AzManException(
                            $"Failed to delete authorization store: the store at '{storePath}' could not be found. It may have already been deleted.", ex);
                    }

                    throw new AzManException($"Failed to delete authorization store: {GetComErrorMessage(ex)}", ex);
                }
                catch (System.IO.FileNotFoundException ex)
                {
                    // Kept for parity with the CLR interop layer, which mapped HRESULT 0x80070002 to
                    // FileNotFoundException. Source-generated interop surfaces it as COMException
                    // (handled above), but downstream code may still raise the BCL exception.
                    _logger.LogWarning(ex, "Store not found while attempting deletion: {StorePath}", storePath);
                    throw new AzManException(
                        $"Failed to delete authorization store: the store at '{storePath}' could not be found. It may have already been deleted.", ex);
                }
            }
        });
    }

    /// <summary>
    /// Refresh store information
    /// </summary>
    /// <param name="storePath">Store path</param>
    /// <returns>Updated store information</returns>
    public async Task<AzAuthorizationStoreInfo?> RefreshStoreAsync(string storePath)
    {
        return await RunComAsync(() =>
        {
            lock (_lockObject)
            {
                try
                {
                    var storeInfo = _openedStores.Find(s =>
                        string.Equals(s.StorePath, storePath, StringComparison.OrdinalIgnoreCase));

                    if (storeInfo == null)
                    {
                        return null;
                    }

                    IAzAuthorizationStore3? authStore = _service.GetAuthStore(storePath);
                    if (authStore == null)
                    {
                        return null;
                    }

                    var storeInfoNonNull = storeInfo!;
                    var storeType = storeInfoNonNull.StoreType;
                    var storePathValue = storeInfoNonNull.StorePath;

                    // If XML store file is missing, close it and return null
                    if (storeType == AzStoreType.Xml)
                    {
                        string xmlPath = GetXmlFilePathFromStoreUrl(storePathValue);
                        if (!System.IO.File.Exists(xmlPath))
                        {
                            CloseStoreInternal(storePathValue);
                            return null;
                        }
                    }

                    // Update cache
                    authStore.UpdateCache(Variant.Missing);

                    // Re-read information
                    var updatedInfo = ReadStoreInfo(authStore, storePath, storeType);

                    // Update item in list
                    int index = _openedStores.IndexOf(storeInfoNonNull);
                    if (index >= 0)
                    {
                        _openedStores[index] = updatedInfo;
                    }

                    return updatedInfo;
                }
                catch (COMException ex)
                {
                    throw new AzManException($"Failed to refresh store: {GetComErrorMessage(ex)}", ex);
                }
            }
        });
    }

    /// <summary>
    /// Update store properties
    /// </summary>
    public async Task UpdateStorePropertiesAsync(string storePath, string description, string applicationData, bool generateAudits)
    {
        await RunStoreWriteAsync(
            storePath,
            store =>
            {
                store.put_Description(description);
                store.put_ApplicationData(applicationData);
                store.put_GenerateAudits(AzRolesCom.FromBool(generateAudits));
            },
            "Failed to update store properties");
    }

    #endregion

    #region Policy Administrators / Readers / Delegated Users Management

    /// <summary>
    /// Add a policy administrator to the store
    /// </summary>
    /// <param name="storePath">Store path</param>
    /// <param name="adminName">Administrator name (domain\user or user@domain format)</param>
    public async Task AddPolicyAdministratorAsync(string storePath, string adminName)
    {
        await RunStoreWriteAsync(
            storePath,
            store => store.AddPolicyAdministratorName(adminName, Variant.Missing),
            "Failed to add policy administrator",
            $"[AzManService] Added policy administrator: {adminName}");
    }

    /// <summary>
    /// Remove a policy administrator from the store
    /// </summary>
    /// <param name="storePath">Store path</param>
    /// <param name="adminName">Administrator name</param>
    public async Task RemovePolicyAdministratorAsync(string storePath, string adminName)
    {
        await RunStoreWriteAsync(
            storePath,
            store => store.DeletePolicyAdministratorName(adminName, Variant.Missing),
            "Failed to remove policy administrator",
            $"[AzManService] Removed policy administrator: {adminName}");
    }

    /// <summary>
    /// Add a policy reader to the store
    /// </summary>
    /// <param name="storePath">Store path</param>
    /// <param name="readerName">Reader name (domain\user or user@domain format)</param>
    public async Task AddPolicyReaderAsync(string storePath, string readerName)
    {
        await RunStoreWriteAsync(
            storePath,
            store => store.AddPolicyReaderName(readerName, Variant.Missing),
            "Failed to add policy reader",
            $"[AzManService] Added policy reader: {readerName}");
    }

    /// <summary>
    /// Remove a policy reader from the store
    /// </summary>
    /// <param name="storePath">Store path</param>
    /// <param name="readerName">Reader name</param>
    public async Task RemovePolicyReaderAsync(string storePath, string readerName)
    {
        await RunStoreWriteAsync(
            storePath,
            store => store.DeletePolicyReaderName(readerName, Variant.Missing),
            "Failed to remove policy reader",
            $"[AzManService] Removed policy reader: {readerName}");
    }

    /// <summary>
    /// Add a delegated policy user to the store
    /// </summary>
    /// <param name="storePath">Store path</param>
    /// <param name="userName">User name (domain\user or user@domain format)</param>
    public async Task AddDelegatedPolicyUserAsync(string storePath, string userName)
    {
        await RunStoreWriteAsync(
            storePath,
            store => store.AddDelegatedPolicyUserName(userName, Variant.Missing),
            "Failed to add delegated policy user",
            $"[AzManService] Added delegated policy user: {userName}");
    }

    /// <summary>
    /// Remove a delegated policy user from the store
    /// </summary>
    /// <param name="storePath">Store path</param>
    /// <param name="userName">User name</param>
    public async Task RemoveDelegatedPolicyUserAsync(string storePath, string userName)
    {
        await RunStoreWriteAsync(
            storePath,
            store => store.DeleteDelegatedPolicyUserName(userName, Variant.Missing),
            "Failed to remove delegated policy user",
            $"[AzManService] Removed delegated policy user: {userName}");
    }

    #endregion

    #region Store Advanced Properties

    /// <summary>
    /// Get store advanced properties
    /// </summary>
    public async Task<StoreAdvancedProperties> GetStoreAdvancedPropertiesAsync(string storePath)
    {
        return await RunStoreReadAsync(
            storePath,
            store =>
            {
                try { store.UpdateCache(Variant.Missing); } catch { }

                // Auditing properties:
                // GenerateAudits → "Runtime application initialization auditing"
                // ApplyStoreSacl → "Authorization store change auditing"
                bool generateAudits = AzRolesCom.ToBool(store.get_GenerateAudits());
                bool applyStoreSacl = AzRolesCom.ToBool(store.get_ApplyStoreSacl());

                return new StoreAdvancedProperties
                {
                    DomainTimeout = TryReadInt(store.get_DomainTimeout),
                    ScriptEngineTimeout = TryReadInt(store.get_ScriptEngineTimeout),
                    MaxScriptEngines = TryReadInt(store.get_MaxScriptEngines),
                    TargetMachine = store.get_TargetMachine() ?? string.Empty,
                    GenerateAudits = generateAudits,
                    RuntimeApplicationInitializationAuditing = generateAudits,
                    AuthorizationStoreChangeAuditing = applyStoreSacl
                };
            },
            "Failed to get store advanced properties");
    }

    /// <summary>
    /// Update store advanced properties
    /// </summary>
    public async Task UpdateStoreAdvancedPropertiesAsync(string storePath, StoreAdvancedProperties properties)
    {
        await RunStoreWriteAsync(
            storePath,
            store =>
            {
                if (properties.DomainTimeout.HasValue)
                {
                    store.put_DomainTimeout(properties.DomainTimeout.Value);
                }
                if (properties.ScriptEngineTimeout.HasValue)
                {
                    store.put_ScriptEngineTimeout(properties.ScriptEngineTimeout.Value);
                }
                if (properties.MaxScriptEngines.HasValue)
                {
                    store.put_MaxScriptEngines(properties.MaxScriptEngines.Value);
                }
                if (properties.GenerateAudits.HasValue)
                {
                    store.put_GenerateAudits(AzRolesCom.FromBool(properties.GenerateAudits.Value));
                }
                if (properties.AuthorizationStoreChangeAuditing.HasValue)
                {
                    store.put_ApplyStoreSacl(AzRolesCom.FromBool(properties.AuthorizationStoreChangeAuditing.Value));
                }
            },
            "Failed to update store advanced properties",
            "[AzManService] Updated store advanced properties");
    }

    /// <summary>
    /// Upgrade store schema from 1.0 to 2.0.
    /// </summary>
    public async Task UpgradeStoreSchemaToV2Async(string storePath)
    {
        if (IsXmlStore(storePath))
        {
            EnsureXmlStoreSchemaV2(storePath);

            await RunStoreWriteAsync(
                storePath,
                _ => { },
                "Failed to upgrade store schema",
                "[AzManService] Upgraded XML store schema to 2.0");

            return;
        }

        if (!IsActiveDirectoryStore(storePath))
        {
            throw new AzManException("Schema upgrade to version 2.0 is only supported for XML and Active Directory authorization stores.");
        }

        // EnsureAdStoreSchemaV2 tries (1) MANAGE_STORE_ONLY handle with UpgradeStoresFunctionalLevel,
        // then (2) ADSI attribute write. If both fail it throws AzManException.
        await RunComAsync(() => EnsureAdStoreSchemaV2(storePath));

        // Refresh the cached store COM object so the in-memory state reflects the upgrade.
        await RunStoreReadAsync(
            storePath,
            store =>
            {
                try { store.UpdateCache(Variant.Missing); } catch { }
                return true;
            },
            "Failed to refresh store after schema upgrade");
    }

    /// <summary>Reads an int property, returning null when the COM read fails.</summary>
    private static int? TryReadInt(Func<int> getter)
    {
        try
        {
            return getter();
        }
        catch (COMException)
        {
            return null;
        }
    }

    #endregion
}

/// <summary>
/// Store advanced properties
/// </summary>
public class StoreAdvancedProperties
{
    /// <summary>Domain timeout in milliseconds (default: 15000)</summary>
    public int? DomainTimeout { get; set; }

    /// <summary>Script engine timeout in milliseconds (default: 45000)</summary>
    public int? ScriptEngineTimeout { get; set; }

    /// <summary>Maximum number of script engines (default: 120)</summary>
    public int? MaxScriptEngines { get; set; }

    /// <summary>Target machine name</summary>
    public string? TargetMachine { get; set; }

    /// <summary>Whether to generate audits</summary>
    public bool? GenerateAudits { get; set; }

    /// <summary>Runtime application initialization auditing</summary>
    public bool? RuntimeApplicationInitializationAuditing { get; set; }

    /// <summary>Authorization store change auditing</summary>
    public bool? AuthorizationStoreChangeAuditing { get; set; }
}
