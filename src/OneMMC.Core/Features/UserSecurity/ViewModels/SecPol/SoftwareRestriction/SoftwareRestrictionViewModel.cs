using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OneMMC.Core.Features.UserSecurity.Models.SecPol.SoftwareRestriction;
using OneMMC.Core.Features.UserSecurity.Services.SecPol.SoftwareRestriction;
using OneMMC.Core.Infrastructure.Admin;
using OneMMC.Core.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace OneMMC.Core.Features.UserSecurity.ViewModels.SecPol.SoftwareRestriction;

/// <summary>
/// View model for the Software Restriction Policies page. Exposes per-section observable state
/// (security levels, additional rules, enforcement, designated file types, trusted publishers)
/// with immediate-apply save operations.
/// </summary>
public sealed partial class SoftwareRestrictionViewModel : ObservableObject
{
    private readonly SoftwareRestrictionPolicyService _policyService;
    private readonly ILogger<SoftwareRestrictionViewModel> _logger;
    private readonly IAdminService _adminService;
    private bool _hasLoaded;

    /// <summary>
    /// Initializes a new instance of the <see cref="SoftwareRestrictionViewModel"/> class.
    /// </summary>
    /// <param name="policyService">The SRP policy service.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="adminService">The administrator service.</param>
    public SoftwareRestrictionViewModel(
        SoftwareRestrictionPolicyService policyService,
        ILogger<SoftwareRestrictionViewModel> logger,
        IAdminService adminService)
    {
        _policyService = policyService;
        _logger = logger;
        _adminService = adminService;
    }

    /// <summary>
    /// Raised when a write operation requires administrator privileges.
    /// </summary>
    public event EventHandler? AdminPermissionRequired;

    /// <summary>
    /// Gets the security level rows for the Security Levels section.
    /// </summary>
    public ObservableCollection<SoftwareRestrictionSecurityLevelItem> SecurityLevelItems { get; } = [];

    /// <summary>
    /// Gets the additional rules matching the current filter.
    /// </summary>
    public ObservableCollection<SoftwareRestrictionRule> FilteredRules { get; } = [];

    /// <summary>
    /// Gets the designated file type rows.
    /// </summary>
    public ObservableCollection<SoftwareRestrictionFileTypeItem> FileTypeItems { get; } = [];

    /// <summary>
    /// Gets or sets the currently loaded policy state.
    /// </summary>
    [ObservableProperty]
    public partial SoftwareRestrictionPolicyState PolicyState { get; set; } = new();

    /// <summary>
    /// Gets or sets the selected additional rule.
    /// </summary>
    [ObservableProperty]
    public partial SoftwareRestrictionRule? SelectedRule { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the view model is loading data.
    /// </summary>
    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether an error is visible.
    /// </summary>
    [ObservableProperty]
    public partial bool HasError { get; set; }

    /// <summary>
    /// Gets or sets the current error message.
    /// </summary>
    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the last-operation status InfoBar is visible.
    /// </summary>
    [ObservableProperty]
    public partial bool HasStatus { get; set; }

    /// <summary>
    /// Gets or sets the last-operation status title.
    /// </summary>
    [ObservableProperty]
    public partial string StatusTitle { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the rule filter text.
    /// </summary>
    [ObservableProperty]
    public partial string FilterText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the extension being typed into the designated file types add box.
    /// </summary>
    [ObservableProperty]
    public partial string NewFileTypeExtension { get; set; } = string.Empty;

    /// <summary>
    /// Gets the hint shown with every saved-status message: policy changes affect new sign-in
    /// sessions; run gpupdate or sign out and back in for running sessions.
    /// </summary>
    public string StatusDetail => GetString(SoftwareRestrictionKeys.StatusSignInHint);

    /// <summary>
    /// Gets a value indicating whether this system no longer enforces SRP at run time
    /// (deprecated feature; Windows 11 22H2+ client editions).
    /// </summary>
    public bool ShowsDeprecationWarning => SoftwareRestrictionPolicyService.IsEnforcementUnavailableOnThisSystem;

    /// <summary>
    /// Gets a value indicating whether SRP has been initialized.
    /// </summary>
    public bool IsConfigured => PolicyState.IsConfigured;

    /// <summary>
    /// Gets a value indicating whether the not-configured empty state should be shown.
    /// </summary>
    public bool ShowsEmptyState => _hasLoaded && !IsLoading && !IsConfigured;

    /// <summary>
    /// Gets a value indicating whether the filtered rule list is empty while the policy is configured.
    /// </summary>
    public bool HasNoRules => IsConfigured && FilteredRules.Count == 0;

    /// <summary>
    /// Gets a value indicating whether the selected rule can be opened for editing or inspection.
    /// </summary>
    public bool CanEditSelectedRule => SelectedRule is { } rule
        && (IsRuleKindEditable(rule.Kind) || rule.CanViewDetails);

    /// <summary>
    /// Gets a value indicating whether the selected rule can be deleted.
    /// </summary>
    public bool CanDeleteSelectedRule => SelectedRule is not null;

    /// <summary>
    /// Gets a value indicating whether the typed extension can be added as a designated file type.
    /// </summary>
    public bool CanAddFileType
    {
        get
        {
            string extension = NormalizeFileExtension(NewFileTypeExtension);
            return !string.IsNullOrWhiteSpace(extension)
                && !FileTypeItems.Any(item => string.Equals(item.Extension, extension, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Gets the default security level combo index (0=Disallowed, 1=Basic User, 2=Unrestricted).</summary>
    public int DefaultSecurityLevelIndex => SecurityLevelToIndex(PolicyState.Enforcement.DefaultSecurityLevel);

    /// <summary>Gets the enforcement file scope combo index (0=except libraries, 1=all software files).</summary>
    public int FileScopeIndex => PolicyState.Enforcement.FileScope == SoftwareRestrictionFileScope.AllSoftwareFiles ? 1 : 0;

    /// <summary>Gets the enforcement user scope combo index (0=all users, 1=all except local administrators).</summary>
    public int UserScopeIndex => PolicyState.Enforcement.UserScope == SoftwareRestrictionUserScope.AllUsersExceptLocalAdministrators ? 1 : 0;

    /// <summary>Gets a value indicating whether certificate rules are enforced.</summary>
    public bool CertificateRulesEnabled => PolicyState.Enforcement.CertificateRulesEnabled;

    /// <summary>Gets a value indicating whether the trusted publisher policy settings are defined.</summary>
    public bool TrustedPublishersDefined => PolicyState.TrustedPublishers.IsDefined;

    /// <summary>Gets the trusted publisher management combo index (matches <see cref="SoftwareRestrictionPublisherScope"/>).</summary>
    public int PublisherScopeIndex => (int)PolicyState.TrustedPublishers.PublisherScope;

    /// <summary>Gets a value indicating whether publisher certificate revocation is checked.</summary>
    public bool CheckPublisherRevocation => PolicyState.TrustedPublishers.CheckPublisherRevocation;

    /// <summary>Gets a value indicating whether timestamp certificate revocation is checked.</summary>
    public bool CheckTimestampRevocation => PolicyState.TrustedPublishers.CheckTimestampRevocation;

    /// <summary>
    /// Converts a security level combo index back to the security level.
    /// </summary>
    /// <param name="index">The combo index (0=Disallowed, 1=Basic User, 2=Unrestricted).</param>
    /// <returns>The security level.</returns>
    public static SoftwareRestrictionSecurityLevel SecurityLevelFromIndex(int index) => index switch
    {
        0 => SoftwareRestrictionSecurityLevel.Disallowed,
        1 => SoftwareRestrictionSecurityLevel.BasicUser,
        _ => SoftwareRestrictionSecurityLevel.Unrestricted
    };

    /// <summary>
    /// Converts a security level to its combo index (0=Disallowed, 1=Basic User, 2=Unrestricted).
    /// </summary>
    /// <param name="level">The security level.</param>
    /// <returns>The combo index.</returns>
    public static int SecurityLevelToIndex(SoftwareRestrictionSecurityLevel level) => level switch
    {
        SoftwareRestrictionSecurityLevel.Disallowed => 0,
        SoftwareRestrictionSecurityLevel.BasicUser => 1,
        _ => 2
    };

    /// <summary>
    /// Loads Software Restriction Policies.
    /// </summary>
    [RelayCommand]
    public async Task LoadPolicyAsync()
    {
        await RunLoadAsync();
    }

    /// <summary>
    /// Refreshes the current policy state.
    /// </summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        await RunLoadAsync();
    }

    /// <summary>
    /// Creates the default Software Restriction Policies state.
    /// </summary>
    [RelayCommand]
    public async Task CreateDefaultPolicyAsync()
    {
        await RunSaveAsync(
            () => _policyService.CreateDefaultPolicy(),
            SoftwareRestrictionKeys.StatusPolicyCreated);
    }

    /// <summary>
    /// Deletes Software Restriction Policies.
    /// </summary>
    [RelayCommand]
    public async Task DeletePolicyAsync()
    {
        await RunSaveAsync(
            () => _policyService.DeletePolicy(),
            SoftwareRestrictionKeys.StatusPolicyDeleted);
    }

    /// <summary>
    /// Saves the default security level while preserving the rest of the enforcement settings.
    /// </summary>
    /// <param name="securityLevel">The selected default security level.</param>
    /// <returns><see langword="true"/> when the operation succeeded.</returns>
    public async Task<bool> SaveDefaultSecurityLevelAsync(SoftwareRestrictionSecurityLevel securityLevel)
    {
        return await SaveEnforcementAsync(enforcement => enforcement.DefaultSecurityLevel = securityLevel);
    }

    /// <summary>
    /// Saves the enforcement file scope while preserving the rest of the enforcement settings.
    /// </summary>
    /// <param name="fileScope">The selected file scope.</param>
    /// <returns><see langword="true"/> when the operation succeeded.</returns>
    public async Task<bool> SaveFileScopeAsync(SoftwareRestrictionFileScope fileScope)
    {
        return await SaveEnforcementAsync(enforcement => enforcement.FileScope = fileScope);
    }

    /// <summary>
    /// Saves the enforcement user scope while preserving the rest of the enforcement settings.
    /// </summary>
    /// <param name="userScope">The selected user scope.</param>
    /// <returns><see langword="true"/> when the operation succeeded.</returns>
    public async Task<bool> SaveUserScopeAsync(SoftwareRestrictionUserScope userScope)
    {
        return await SaveEnforcementAsync(enforcement => enforcement.UserScope = userScope);
    }

    /// <summary>
    /// Saves the certificate rule enforcement flag while preserving the rest of the enforcement settings.
    /// </summary>
    /// <param name="enabled">Whether certificate rules are enforced.</param>
    /// <returns><see langword="true"/> when the operation succeeded.</returns>
    public async Task<bool> SaveCertificateRulesEnabledAsync(bool enabled)
    {
        return await SaveEnforcementAsync(enforcement => enforcement.CertificateRulesEnabled = enabled);
    }

    /// <summary>
    /// Saves whether the trusted publisher policy settings are defined.
    /// </summary>
    /// <param name="isDefined">Whether the settings are defined.</param>
    /// <returns><see langword="true"/> when the operation succeeded.</returns>
    public async Task<bool> SaveTrustedPublishersDefinedAsync(bool isDefined)
    {
        return await SaveTrustedPublishersAsync(settings => settings.IsDefined = isDefined);
    }

    /// <summary>
    /// Saves the trusted publisher management scope.
    /// </summary>
    /// <param name="scope">The selected publisher scope.</param>
    /// <returns><see langword="true"/> when the operation succeeded.</returns>
    public async Task<bool> SavePublisherScopeAsync(SoftwareRestrictionPublisherScope scope)
    {
        return await SaveTrustedPublishersAsync(settings => settings.PublisherScope = scope);
    }

    /// <summary>
    /// Saves the publisher certificate revocation check flag.
    /// </summary>
    /// <param name="enabled">Whether the check is enabled.</param>
    /// <returns><see langword="true"/> when the operation succeeded.</returns>
    public async Task<bool> SavePublisherRevocationAsync(bool enabled)
    {
        return await SaveTrustedPublishersAsync(settings => settings.CheckPublisherRevocation = enabled);
    }

    /// <summary>
    /// Saves the timestamp certificate revocation check flag.
    /// </summary>
    /// <param name="enabled">Whether the check is enabled.</param>
    /// <returns><see langword="true"/> when the operation succeeded.</returns>
    public async Task<bool> SaveTimestampRevocationAsync(bool enabled)
    {
        return await SaveTrustedPublishersAsync(settings => settings.CheckTimestampRevocation = enabled);
    }

    /// <summary>
    /// Adds the typed extension to the designated file types and saves the list.
    /// </summary>
    /// <returns><see langword="true"/> when the operation succeeded.</returns>
    public async Task<bool> AddFileTypeAsync()
    {
        string extension = NormalizeFileExtension(NewFileTypeExtension);
        if (string.IsNullOrWhiteSpace(extension) || !CanAddFileType)
        {
            return false;
        }

        bool saved = await RunSaveAsync(
            () => _policyService.SaveDesignatedFileTypes(
                FileTypeItems.Select(static item => item.Extension).Append(extension)),
            SoftwareRestrictionKeys.StatusPolicySaved);

        if (saved)
        {
            NewFileTypeExtension = string.Empty;
        }

        return saved;
    }

    /// <summary>
    /// Removes a designated file type and saves the list.
    /// </summary>
    /// <param name="item">The file type row to remove.</param>
    /// <returns><see langword="true"/> when the operation succeeded.</returns>
    public async Task<bool> RemoveFileTypeAsync(SoftwareRestrictionFileTypeItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return await RunSaveAsync(
            () => _policyService.SaveDesignatedFileTypes(
                FileTypeItems
                    .Where(existing => !string.Equals(existing.Extension, item.Extension, StringComparison.OrdinalIgnoreCase))
                    .Select(static existing => existing.Extension)),
            SoftwareRestrictionKeys.StatusPolicySaved);
    }

    /// <summary>
    /// Saves an additional rule.
    /// </summary>
    /// <param name="rule">The rule to save.</param>
    /// <returns><see langword="true"/> when the operation succeeded.</returns>
    public async Task<bool> SaveRuleAsync(SoftwareRestrictionRule rule)
    {
        return await RunSaveAsync(
            () => _policyService.UpsertRule(rule),
            SoftwareRestrictionKeys.StatusRuleSaved);
    }

    /// <summary>
    /// Deletes an additional rule.
    /// </summary>
    /// <param name="rule">The rule to delete.</param>
    /// <returns><see langword="true"/> when the operation succeeded.</returns>
    public async Task<bool> DeleteRuleAsync(SoftwareRestrictionRule rule)
    {
        return await RunSaveAsync(
            () => _policyService.DeleteRule(rule),
            SoftwareRestrictionKeys.StatusRuleDeleted);
    }

    private static bool IsRuleKindEditable(SoftwareRestrictionRuleKind kind)
        => kind is SoftwareRestrictionRuleKind.Path
            or SoftwareRestrictionRuleKind.Hash
            or SoftwareRestrictionRuleKind.NetworkZone;

    private async Task<bool> SaveEnforcementAsync(Action<SoftwareRestrictionEnforcementSettings> mutate)
    {
        SoftwareRestrictionEnforcementSettings current = PolicyState.Enforcement;
        SoftwareRestrictionEnforcementSettings updated = new()
        {
            DefaultSecurityLevel = current.DefaultSecurityLevel,
            UserScope = current.UserScope,
            FileScope = current.FileScope,
            CertificateRulesEnabled = current.CertificateRulesEnabled
        };
        mutate(updated);

        return await RunSaveAsync(
            () => _policyService.SaveEnforcement(updated),
            SoftwareRestrictionKeys.StatusPolicySaved);
    }

    private async Task<bool> SaveTrustedPublishersAsync(Action<SoftwareRestrictionTrustedPublisherSettings> mutate)
    {
        SoftwareRestrictionTrustedPublisherSettings current = PolicyState.TrustedPublishers;
        SoftwareRestrictionTrustedPublisherSettings updated = new()
        {
            IsDefined = current.IsDefined,
            PublisherScope = current.PublisherScope,
            CheckPublisherRevocation = current.CheckPublisherRevocation,
            CheckTimestampRevocation = current.CheckTimestampRevocation
        };
        mutate(updated);

        return await RunSaveAsync(
            () => _policyService.SaveTrustedPublishers(updated),
            SoftwareRestrictionKeys.StatusPolicySaved);
    }

    private async Task RunLoadAsync()
    {
        IsLoading = true;
        HasError = false;
        ErrorMessage = string.Empty;
        OnPropertyChanged(nameof(ShowsEmptyState));

        try
        {
            PolicyState = await Task.Run(() => _policyService.LoadPolicy());
            _hasLoaded = true;
            RefreshSections();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SoftwareRestrictionViewModel] Failed to load Software Restriction Policies.");
            SetError(ex.Message);
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(ShowsEmptyState));
        }
    }

    private async Task<bool> RunSaveAsync(Action operation, string statusResourceKey)
    {
        HasError = false;
        ErrorMessage = string.Empty;
        HasStatus = false;

        try
        {
            await Task.Run(operation);
            StatusTitle = GetString(statusResourceKey);
            await RunLoadAsync();
            HasStatus = true;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SoftwareRestrictionViewModel] Failed to save Software Restriction Policies.");
            if (_adminService.IsPermissionError(ex) || ex is UnauthorizedAccessException)
            {
                SetError(GetString(CommonKeys.AccessDenied_Generic, ResourceFileNames.Common));
                AdminPermissionRequired?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                SetError(ex.Message);
            }

            return false;
        }
    }

    private void SetError(string message)
    {
        ErrorMessage = message;
        HasError = !string.IsNullOrWhiteSpace(message);
    }

    private void RefreshSections()
    {
        SelectedRule = null;
        RebuildSecurityLevelItems();
        ApplyRuleFilter();
        RebuildFileTypeItems();

        OnPropertyChanged(nameof(IsConfigured));
        OnPropertyChanged(nameof(ShowsEmptyState));
        OnPropertyChanged(nameof(DefaultSecurityLevelIndex));
        OnPropertyChanged(nameof(FileScopeIndex));
        OnPropertyChanged(nameof(UserScopeIndex));
        OnPropertyChanged(nameof(CertificateRulesEnabled));
        OnPropertyChanged(nameof(TrustedPublishersDefined));
        OnPropertyChanged(nameof(PublisherScopeIndex));
        OnPropertyChanged(nameof(CheckPublisherRevocation));
        OnPropertyChanged(nameof(CheckTimestampRevocation));
        OnPropertyChanged(nameof(CanAddFileType));
    }

    private void RebuildSecurityLevelItems()
    {
        SoftwareRestrictionSecurityLevel defaultLevel = PolicyState.Enforcement.DefaultSecurityLevel;
        SecurityLevelItems.Clear();

        foreach (SoftwareRestrictionSecurityLevel level in (ReadOnlySpan<SoftwareRestrictionSecurityLevel>)
        [
            SoftwareRestrictionSecurityLevel.Disallowed,
            SoftwareRestrictionSecurityLevel.BasicUser,
            SoftwareRestrictionSecurityLevel.Unrestricted
        ])
        {
            SecurityLevelItems.Add(new SoftwareRestrictionSecurityLevelItem
            {
                Level = level,
                DisplayName = SoftwareRestrictionRule.FormatSecurityLevel(level),
                Description = FormatSecurityLevelDescription(level),
                IsDefault = level == defaultLevel
            });
        }
    }

    private void ApplyRuleFilter()
    {
        FilteredRules.Clear();

        IEnumerable<SoftwareRestrictionRule> rules = PolicyState.Rules;
        if (!string.IsNullOrWhiteSpace(FilterText))
        {
            rules = rules.Where(MatchesFilter);
        }

        foreach (SoftwareRestrictionRule rule in rules)
        {
            FilteredRules.Add(rule);
        }

        OnPropertyChanged(nameof(HasNoRules));
    }

    private bool MatchesFilter(SoftwareRestrictionRule rule)
    {
        return ContainsFilterText(rule.DisplayValue)
            || ContainsFilterText(rule.KindDisplayName)
            || ContainsFilterText(rule.SecurityLevelDisplayName)
            || ContainsFilterText(rule.Description)
            || ContainsFilterText(rule.LastModifiedDisplay)
            || ContainsFilterText(rule.CertificateSubject)
            || ContainsFilterText(rule.CertificateIssuer)
            || ContainsFilterText(rule.CertificateSerialNumber)
            || ContainsFilterText(rule.CertificateThumbprint);
    }

    private bool ContainsFilterText(string value)
    {
        return value.Contains(FilterText, StringComparison.CurrentCultureIgnoreCase);
    }

    private void RebuildFileTypeItems()
    {
        FileTypeItems.Clear();
        foreach (string extension in PolicyState.DesignatedFileTypes)
        {
            FileTypeItems.Add(new SoftwareRestrictionFileTypeItem
            {
                Extension = extension,
                FileTypeDisplay = ResolveFileTypeName(extension)
            });
        }
    }

    partial void OnSelectedRuleChanged(SoftwareRestrictionRule? value)
    {
        OnPropertyChanged(nameof(CanEditSelectedRule));
        OnPropertyChanged(nameof(CanDeleteSelectedRule));
    }

    partial void OnFilterTextChanged(string value)
    {
        ApplyRuleFilter();
    }

    partial void OnNewFileTypeExtensionChanged(string value)
    {
        OnPropertyChanged(nameof(CanAddFileType));
    }

    private static string FormatSecurityLevelDescription(SoftwareRestrictionSecurityLevel level)
    {
        return level switch
        {
            SoftwareRestrictionSecurityLevel.BasicUser => GetString(SoftwareRestrictionKeys.SecurityLevelBasicUserDescription),
            SoftwareRestrictionSecurityLevel.Unrestricted => GetString(SoftwareRestrictionKeys.SecurityLevelUnrestrictedDescription),
            _ => GetString(SoftwareRestrictionKeys.SecurityLevelDisallowedDescription)
        };
    }

    private static string NormalizeFileExtension(string extension)
    {
        return extension.Trim().TrimStart('.').ToUpperInvariant();
    }

    /// <summary>
    /// Resolves the friendly file type name for an extension from the file association registry,
    /// falling back to the MMC-style generic "{extension} File" name.
    /// </summary>
    private static string ResolveFileTypeName(string extension)
    {
        try
        {
            using RegistryKey? extensionKey = Registry.ClassesRoot.OpenSubKey($".{extension}");
            string? progId = extensionKey?.GetValue(null)?.ToString();
            if (!string.IsNullOrWhiteSpace(progId))
            {
                using RegistryKey? progIdKey = Registry.ClassesRoot.OpenSubKey(progId);
                string? fileType = progIdKey?.GetValue(null)?.ToString();
                if (!string.IsNullOrWhiteSpace(fileType))
                {
                    return fileType;
                }
            }
        }
        catch (Exception)
        {
            // Best effort only; fall back to the MMC-style generic file type name.
        }

        return string.Format(
            CultureInfo.CurrentCulture,
            GetString(SoftwareRestrictionKeys.FileTypeFallbackFormat),
            extension);
    }

    private static string GetString(string key, string resourceFileName = ResourceFileNames.SecPol)
    {
        string value = LocalizationProvider.Current.GetString(resourceFileName, key);
        return string.IsNullOrWhiteSpace(value) ? key : value;
    }
}
