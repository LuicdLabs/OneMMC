using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using OneMMC.Core.DependencyInjection;
using OneMMC.Core.Features.UserSecurity.Models.SecPol;
using OneMMC.Core.Features.UserSecurity.Models.SecPol.SystemAudit;
using OneMMC.Core.Features.UserSecurity.Services.SecPol;
using OneMMC.Core.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Windows.Win32;
using static OneMMC.Core.Features.UserSecurity.Services.SecPol.SecurityPolicyNativeMethods;

namespace OneMMC.Core.Features.UserSecurity.Services.SecPol.SystemAudit;

/// <summary>
/// Reads and writes the Local Group Policy Object System Audit policy set.
/// </summary>
public sealed class SystemAuditPolicyService
{
    private const string InclusionSuccess = "Success";
    private const string InclusionFailure = "Failure";
    private const string InclusionSuccessAndFailure = "Success and Failure";
    private const string InclusionNoAuditing = "No Auditing";
    private const string InclusionNotSpecified = "Not Specified";

    private static readonly Guid GlobalObjectAccessAuditingCategoryGuid = new("4b8b36d5-c6f4-41a9-bbdc-bfe92f5d8b9f");
    private static readonly Guid GlobalObjectAccessFileSystemGuid = new("84c179b5-0380-4b3d-81b0-16ccfcfa2626");
    private static readonly Guid GlobalObjectAccessRegistryGuid = new("fc18b720-58ab-4965-85f1-9c80b631cd39");

    private readonly ILogger<SystemAuditPolicyService> _logger;
    private readonly SystemAuditExplainTextProvider _explainTextProvider;
    private readonly SystemAuditPolicyPersistence _policyPersistence;

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemAuditPolicyService"/> class.
    /// </summary>
    /// <param name="logger">The logger used for diagnostics.</param>
    public SystemAuditPolicyService(ILogger<SystemAuditPolicyService> logger)
    {
        _logger = logger;
        _explainTextProvider = new SystemAuditExplainTextProvider(logger);

        string auditCsvPath = Environment.ExpandEnvironmentVariables(
            @"%SYSTEMROOT%\System32\GroupPolicy\Machine\Microsoft\Windows NT\Audit\audit.csv");
        string gptIniPath = Environment.ExpandEnvironmentVariables(@"%SYSTEMROOT%\System32\GroupPolicy\gpt.ini");
        _policyPersistence = new SystemAuditPolicyPersistence(auditCsvPath, gptIniPath, logger);
    }

    /// <summary>
    /// Gets the audit categories shown by the System Audit page.
    /// </summary>
    /// <returns>The available audit categories.</returns>
    public IReadOnlyList<AuditCategoryItem> GetCategories()
    {
        var categories = new List<AuditCategoryItem>();

        if (!AuditEnumerateCategories(out IntPtr categoryGuids, out uint categoryCount))
        {
            int error = Marshal.GetLastWin32Error();
            _logger.LogWarning("[SystemAuditPolicyService] AuditEnumerateCategories failed: {Error}", error);
            AddGlobalObjectAccessAuditingCategory(categories);
            return categories;
        }

        try
        {
            int guidSize = Marshal.SizeOf<Guid>();
            for (uint index = 0; index < categoryCount; index++)
            {
                Guid categoryGuid = Marshal.PtrToStructure<Guid>(IntPtr.Add(categoryGuids, (int)(index * guidSize)));
                categories.Add(new AuditCategoryItem(GetCategoryDisplayName(categoryGuid), categoryGuid));
            }
        }
        finally
        {
            AuditFree(categoryGuids);
        }

        AddGlobalObjectAccessAuditingCategory(categories);
        return categories;
    }

    /// <summary>
    /// Gets the audit items for the specified category.
    /// </summary>
    /// <param name="category">The selected category.</param>
    /// <returns>The audit items for that category.</returns>
    public IReadOnlyList<AuditSubcategoryValue> GetSubcategories(AuditCategoryItem category)
    {
        ArgumentNullException.ThrowIfNull(category);

        return category.Kind == SystemAuditCategoryKind.GlobalObjectAccessAuditing
            ? ReadGlobalObjectAccessItems()
            : ReadStandardSubcategories(category.CategoryGuid);
    }

    /// <summary>
    /// Persists a Local Group Policy Object audit policy change.
    /// </summary>
    /// <param name="subcategory">The updated audit item to save.</param>
    public void SaveSubcategory(AuditSubcategoryValue subcategory)
    {
        ArgumentNullException.ThrowIfNull(subcategory);

        PolicyNativeHelpers.EnsurePrivilegeEnabled(SE_SECURITY_NAME);

        if (subcategory.IsGlobalObjectAccessPolicy)
        {
            SaveGlobalObjectAccessItem(subcategory);
            RefreshMachinePolicy();
            return;
        }

        SystemAuditPolicyPersistence.SystemAuditCsvDocument document = _policyPersistence.LoadForSave();
        SaveStandardSubcategory(document, subcategory);
        _policyPersistence.Save(document);
        _policyPersistence.UpdateGptIni();
        RefreshMachinePolicy();
    }

    private IReadOnlyList<AuditSubcategoryValue> ReadStandardSubcategories(Guid categoryGuid)
    {
        var result = new List<AuditSubcategoryValue>();
        SystemAuditPolicyPersistence.SystemAuditCsvDocument document = _policyPersistence.LoadForRead();

        if (!AuditEnumerateSubCategories(ref categoryGuid, false, out IntPtr subcategoryGuids, out uint subcategoryCount))
        {
            int error = Marshal.GetLastWin32Error();
            _logger.LogWarning(
                "[SystemAuditPolicyService] AuditEnumerateSubCategories failed for {CategoryGuid}: {Error}",
                categoryGuid,
                error);
            return result;
        }

        try
        {
            int guidSize = Marshal.SizeOf<Guid>();
            for (uint index = 0; index < subcategoryCount; index++)
            {
                Guid subcategoryGuid = Marshal.PtrToStructure<Guid>(IntPtr.Add(subcategoryGuids, (int)(index * guidSize)));
                SystemAuditPolicyPersistence.SystemAuditCsvRow? row = document.TryGetSystemRow(subcategoryGuid);
                result.Add(CreateStandardSubcategoryValue(categoryGuid, subcategoryGuid, row));
            }
        }
        finally
        {
            AuditFree(subcategoryGuids);
        }

        return result.OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private IReadOnlyList<AuditSubcategoryValue> ReadGlobalObjectAccessItems()
    {
        SystemAuditPolicyPersistence.SystemAuditCsvDocument document = _policyPersistence.LoadForRead();

        return
        [
            CreateGlobalObjectAccessValue(
                document,
                GlobalObjectAccessFileSystemGuid,
                SystemAuditResourceType.FileSystem,
                LocalizedString(SecPolKeys.SystemAuditGlobalObjectFileSystem, "File system")),
            CreateGlobalObjectAccessValue(
                document,
                GlobalObjectAccessRegistryGuid,
                SystemAuditResourceType.Registry,
                LocalizedString(SecPolKeys.SystemAuditGlobalObjectRegistry, "Registry"))
        ];
    }

    private AuditSubcategoryValue CreateStandardSubcategoryValue(
        Guid categoryGuid,
        Guid subcategoryGuid,
        SystemAuditPolicyPersistence.SystemAuditCsvRow? row)
    {
        string displayName = GetSubcategoryDisplayName(subcategoryGuid);
        var value = new AuditSubcategoryValue
        {
            AuditCategoryGuid = categoryGuid,
            SubcategoryGuid = subcategoryGuid,
            DisplayName = displayName,
            ExplainText = _explainTextProvider.GetExplainText(subcategoryGuid, displayName)
        };

        if (row is null)
        {
            value.IsDefined = false;
            value.Flags = AuditPolicyFlags.None;
            return value;
        }

        ApplySystemSettingValue(value, row.SettingValue);
        return value;
    }

    private AuditSubcategoryValue CreateGlobalObjectAccessValue(
        SystemAuditPolicyPersistence.SystemAuditCsvDocument document,
        Guid subcategoryGuid,
        SystemAuditResourceType resourceType,
        string displayName)
    {
        string subcategoryName = GetGlobalObjectAccessSubcategoryName(resourceType);
        SystemAuditPolicyPersistence.SystemAuditCsvRow? row = document.TryGetGlobalObjectAccessRow(subcategoryName);
        string sddl = row?.SettingValue ?? string.Empty;

        return new AuditSubcategoryValue
        {
            AuditCategoryGuid = GlobalObjectAccessAuditingCategoryGuid,
            SubcategoryGuid = subcategoryGuid,
            DisplayName = displayName,
            ItemKind = SystemAuditItemKind.GlobalObjectAccessPolicy,
            ResourceType = resourceType,
            IsDefined = !string.IsNullOrWhiteSpace(sddl),
            GlobalSaclSddl = sddl,
            GlobalSaclBinary = TryConvertSddlToSaclBinary(sddl),
            ExplainText = _explainTextProvider.GetExplainText(displayName)
        };
    }

    private static void ApplySystemSettingValue(AuditSubcategoryValue value, string settingValue)
    {
        value.IsDefined = false;
        value.Flags = AuditPolicyFlags.None;

        if (!uint.TryParse(settingValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint parsedValue))
            return;

        switch (parsedValue)
        {
            case 1:
                value.IsDefined = true;
                value.Flags = AuditPolicyFlags.Success;
                break;
            case 2:
                value.IsDefined = true;
                value.Flags = AuditPolicyFlags.Failure;
                break;
            case 3:
                value.IsDefined = true;
                value.Flags = AuditPolicyFlags.SuccessAndFailure;
                break;
            case 4:
                value.IsDefined = true;
                value.Flags = AuditPolicyFlags.None;
                break;
            default:
                value.IsDefined = false;
                value.Flags = AuditPolicyFlags.None;
                break;
        }
    }

    private static byte[]? TryConvertSddlToSaclBinary(string sddl)
    {
        if (string.IsNullOrWhiteSpace(sddl))
            return null;

        try
        {
            var descriptor = new RawSecurityDescriptor(sddl);
            if (descriptor.SystemAcl is null)
                return null;

            var bytes = new byte[descriptor.SystemAcl.BinaryLength];
            descriptor.SystemAcl.GetBinaryForm(bytes, 0);
            return bytes;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void SaveStandardSubcategory(
        SystemAuditPolicyPersistence.SystemAuditCsvDocument document,
        AuditSubcategoryValue subcategory)
    {
        if (!subcategory.IsDefined)
        {
            document.RemoveSystemRow(subcategory.SubcategoryGuid);
            return;
        }

        int settingValue = MapStandardSettingValue(subcategory.Flags);
        SystemAuditPolicyPersistence.SystemAuditCsvRow row = document.TryGetSystemRow(subcategory.SubcategoryGuid)
            ?? new SystemAuditPolicyPersistence.SystemAuditCsvRow();

        row.MachineName = string.IsNullOrWhiteSpace(row.MachineName) ? Environment.MachineName : row.MachineName;
        row.PolicyTarget = SystemAuditPersistenceConstants.SystemPolicyTarget;
        row.Subcategory = string.IsNullOrWhiteSpace(row.Subcategory) ? subcategory.DisplayName : row.Subcategory;
        row.SubcategoryGuid = subcategory.SubcategoryGuid.ToString("D");
        row.InclusionSetting = GetInclusionSettingText(settingValue);
        row.ExclusionSetting = string.Empty;
        row.SettingValue = settingValue.ToString(CultureInfo.InvariantCulture);

        document.UpsertSystemRow(row);
    }

    private void SaveGlobalObjectAccessItem(AuditSubcategoryValue subcategory)
    {
        string objectTypeName = GetGlobalObjectAccessObjectTypeName(subcategory.ResourceType);
        string subcategoryName = GetGlobalObjectAccessSubcategoryName(subcategory.ResourceType);
        string sddl = subcategory.IsDefined
            ? GetGlobalSaclSddl(subcategory)
            : string.Empty;

        SaveGlobalObjectAccessPolicyRow(subcategoryName, sddl);
        SetEffectiveGlobalSacl(objectTypeName, subcategory, sddl);
    }

    private void SaveGlobalObjectAccessPolicyRow(string subcategoryName, string sddl)
    {
        SystemAuditPolicyPersistence.SystemAuditCsvDocument document = _policyPersistence.LoadForSave();

        if (string.IsNullOrWhiteSpace(sddl))
        {
            document.RemoveGlobalObjectAccessRow(subcategoryName);
        }
        else
        {
            SystemAuditPolicyPersistence.SystemAuditCsvRow row = document.TryGetGlobalObjectAccessRow(subcategoryName)
                ?? new SystemAuditPolicyPersistence.SystemAuditCsvRow();

            row.MachineName = string.IsNullOrWhiteSpace(row.MachineName) ? Environment.MachineName : row.MachineName;
            row.PolicyTarget = string.Empty;
            row.Subcategory = subcategoryName;
            row.SubcategoryGuid = string.Empty;
            row.InclusionSetting = string.Empty;
            row.ExclusionSetting = string.Empty;
            row.SettingValue = sddl;

            document.UpsertGlobalObjectAccessRow(row);
        }

        _policyPersistence.Save(document);
        _policyPersistence.UpdateGptIni();
    }

    private static string GetGlobalSaclSddl(AuditSubcategoryValue subcategory)
    {
        if (!string.IsNullOrWhiteSpace(subcategory.GlobalSaclSddl))
            return subcategory.GlobalSaclSddl;

        if (subcategory.GlobalSaclBinary is not { Length: > 0 } aclBytes)
            return string.Empty;

        var descriptor = new RawSecurityDescriptor(ControlFlags.SystemAclPresent, null, null, new RawAcl(aclBytes, 0), null);
        return descriptor.GetSddlForm(AccessControlSections.Audit);
    }

    private void SetEffectiveGlobalSacl(string objectTypeName, AuditSubcategoryValue subcategory, string sddl)
    {
        byte[]? aclBytes = subcategory.IsDefined
            ? GetGlobalSaclBytes(subcategory)
            : null;

        IntPtr aclPointer = IntPtr.Zero;
        try
        {
            if (aclBytes is not null)
            {
                aclPointer = Marshal.AllocHGlobal(aclBytes.Length);
                Marshal.Copy(aclBytes, 0, aclPointer, aclBytes.Length);
            }

            if (!AuditSetGlobalSacl(objectTypeName, aclPointer))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            _logger.LogDebug(
                "[SystemAuditPolicyService] Saved Global Object Access SACL for {ObjectTypeName}; Configured={Configured}; SddlLength={SddlLength}",
                objectTypeName,
                !string.IsNullOrWhiteSpace(sddl),
                sddl.Length);
        }
        finally
        {
            if (aclPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(aclPointer);
            }
        }
    }

    private static byte[] GetGlobalSaclBytes(AuditSubcategoryValue subcategory)
    {
        if (subcategory.GlobalSaclBinary is { Length: > 0 })
            return subcategory.GlobalSaclBinary;

        string sddl = string.IsNullOrWhiteSpace(subcategory.GlobalSaclSddl)
            ? CreateEmptySaclSddl()
            : subcategory.GlobalSaclSddl;

        var descriptor = new RawSecurityDescriptor(sddl);
        RawAcl sacl = descriptor.SystemAcl ?? new RawAcl(2, 0);
        byte[] bytes = new byte[sacl.BinaryLength];
        sacl.GetBinaryForm(bytes, 0);
        return bytes;
    }

    private void RefreshMachinePolicy()
    {
        try
        {
            bool refreshed = PInvoke.RefreshPolicyEx(true, 1);
            if (!refreshed)
            {
                int error = Marshal.GetLastWin32Error();
                _logger.LogWarning("[SystemAuditPolicyService] RefreshPolicyEx(machine) failed: {Error}", error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SystemAuditPolicyService] RefreshPolicyEx(machine) threw an exception");
        }
    }

    private static int MapStandardSettingValue(AuditPolicyFlags flags) => flags switch
    {
        AuditPolicyFlags.Success => 1,
        AuditPolicyFlags.Failure => 2,
        AuditPolicyFlags.SuccessAndFailure => 3,
        _ => 4
    };

    private static string GetInclusionSettingText(int settingValue) => settingValue switch
    {
        1 => InclusionSuccess,
        2 => InclusionFailure,
        3 => InclusionSuccessAndFailure,
        4 => InclusionNoAuditing,
        _ => InclusionNotSpecified
    };

    private static string CreateEmptySaclSddl()
    {
        var descriptor = new RawSecurityDescriptor(ControlFlags.SystemAclPresent, null, null, new RawAcl(2, 0), null);
        return descriptor.GetSddlForm(AccessControlSections.Audit);
    }

    private static string GetGlobalObjectAccessSubcategoryName(SystemAuditResourceType resourceType) => resourceType switch
    {
        SystemAuditResourceType.FileSystem => SystemAuditPersistenceConstants.FileGlobalSaclName,
        SystemAuditResourceType.Registry => SystemAuditPersistenceConstants.RegistryGlobalSaclName,
        _ => throw new InvalidEnumArgumentException(nameof(resourceType), (int)resourceType, typeof(SystemAuditResourceType))
    };

    private static string GetGlobalObjectAccessObjectTypeName(SystemAuditResourceType resourceType) => resourceType switch
    {
        SystemAuditResourceType.FileSystem => "File",
        SystemAuditResourceType.Registry => "Key",
        _ => throw new InvalidEnumArgumentException(nameof(resourceType), (int)resourceType, typeof(SystemAuditResourceType))
    };

    private static void AddGlobalObjectAccessAuditingCategory(ICollection<AuditCategoryItem> categories)
    {
        bool exists = categories.Any(category => category.Kind == SystemAuditCategoryKind.GlobalObjectAccessAuditing);
        if (exists)
            return;

        categories.Add(new AuditCategoryItem(
            LocalizedString(SecPolKeys.SystemAuditGlobalObjectAccessAuditing, "Global Object Access Auditing"),
            GlobalObjectAccessAuditingCategoryGuid,
            SystemAuditCategoryKind.GlobalObjectAccessAuditing));
    }

    private static string GetCategoryDisplayName(Guid categoryGuid)
    {
        if (!AuditLookupCategoryName(ref categoryGuid, out IntPtr categoryName))
            return categoryGuid.ToString("D");

        try
        {
            return Marshal.PtrToStringUni(categoryName) ?? categoryGuid.ToString("D");
        }
        finally
        {
            AuditFree(categoryName);
        }
    }

    private static string GetSubcategoryDisplayName(Guid subcategoryGuid)
    {
        if (!AuditLookupSubCategoryName(ref subcategoryGuid, out IntPtr subcategoryName))
            return subcategoryGuid.ToString("D");

        try
        {
            return Marshal.PtrToStringUni(subcategoryName) ?? subcategoryGuid.ToString("D");
        }
        finally
        {
            AuditFree(subcategoryName);
        }
    }

    private static string LocalizedString(string key, string fallback)
    {
        string value = LocalizationProvider.Current.GetString(ResourceFileNames.SecPol, key);
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        if (value.StartsWith("[", StringComparison.Ordinal) && value.EndsWith("]", StringComparison.Ordinal))
            return fallback;

        return value;
    }
}

