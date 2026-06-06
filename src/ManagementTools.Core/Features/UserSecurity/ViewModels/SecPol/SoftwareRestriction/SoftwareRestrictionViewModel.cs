using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ManagementTools.Core.Features.UserSecurity.Models.SecPol.SoftwareRestriction;
using ManagementTools.Core.Features.UserSecurity.Services.SecPol.SoftwareRestriction;
using ManagementTools.Core.Infrastructure.Admin;
using ManagementTools.Core.Localization;
using Microsoft.Extensions.Logging;

namespace ManagementTools.Core.Features.UserSecurity.ViewModels.SecPol.SoftwareRestriction;

/// <summary>
/// View model for the Software Restriction Policies page.
/// </summary>
public sealed partial class SoftwareRestrictionViewModel : ObservableObject
{
    private readonly SoftwareRestrictionPolicyService _policyService;
    private readonly ILogger<SoftwareRestrictionViewModel> _logger;
    private readonly IAdminService _adminService;
    private List<SoftwareRestrictionListItem> _allItems = [];

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

        Sections.Add(new SoftwareRestrictionSectionItem(SoftwareRestrictionSectionKind.SecurityLevels, GetString(SoftwareRestrictionKeys.SectionSecurityLevels)));
        Sections.Add(new SoftwareRestrictionSectionItem(SoftwareRestrictionSectionKind.AdditionalRules, GetString(SoftwareRestrictionKeys.SectionAdditionalRules)));
        Sections.Add(new SoftwareRestrictionSectionItem(SoftwareRestrictionSectionKind.Enforcement, GetString(SoftwareRestrictionKeys.SectionEnforcement)));
        Sections.Add(new SoftwareRestrictionSectionItem(SoftwareRestrictionSectionKind.DesignatedFileTypes, GetString(SoftwareRestrictionKeys.SectionDesignatedFileTypes)));
        Sections.Add(new SoftwareRestrictionSectionItem(SoftwareRestrictionSectionKind.TrustedPublishers, GetString(SoftwareRestrictionKeys.SectionTrustedPublishers)));

        SelectedSection = Sections.FirstOrDefault();
    }

    /// <summary>
    /// Raised when a write operation requires administrator privileges.
    /// </summary>
    public event EventHandler? AdminPermissionRequired;

    /// <summary>
    /// Gets the left navigation sections.
    /// </summary>
    public ObservableCollection<SoftwareRestrictionSectionItem> Sections { get; } = [];

    /// <summary>
    /// Gets rows for the currently selected section.
    /// </summary>
    public ObservableCollection<SoftwareRestrictionListItem> CurrentItems { get; } = [];

    /// <summary>
    /// Gets or sets the currently loaded policy state.
    /// </summary>
    [ObservableProperty]
    public partial SoftwareRestrictionPolicyState PolicyState { get; set; } = new();

    /// <summary>
    /// Gets or sets the selected navigation section.
    /// </summary>
    [ObservableProperty]
    public partial SoftwareRestrictionSectionItem? SelectedSection { get; set; }

    /// <summary>
    /// Gets or sets the selected details row.
    /// </summary>
    [ObservableProperty]
    public partial SoftwareRestrictionListItem? SelectedItem { get; set; }

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
    /// Gets or sets the current filter text.
    /// </summary>
    [ObservableProperty]
    public partial string FilterText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the most recent status message.
    /// </summary>
    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether SRP has been initialized.
    /// </summary>
    public bool IsConfigured => PolicyState.IsConfigured;

    /// <summary>
    /// Gets a value indicating whether default SRP values can be created.
    /// </summary>
    public bool CanCreatePolicy => !IsConfigured;

    /// <summary>
    /// Gets a value indicating whether the selected row can be deleted as an additional rule.
    /// </summary>
    public bool CanDeleteSelectedRule => SelectedItem?.Rule is not null;

    /// <summary>
    /// Gets a value indicating whether the selected security level can become the default level.
    /// </summary>
    public bool CanSetSelectedSecurityLevelDefault =>
        SelectedSection?.Kind == SoftwareRestrictionSectionKind.SecurityLevels
        && SelectedItem?.SecurityLevel is SoftwareRestrictionSecurityLevel securityLevel
        && securityLevel != PolicyState.Enforcement.DefaultSecurityLevel;

    /// <summary>
    /// Gets the first list column header for the selected section.
    /// </summary>
    public string CurrentListHeaderName => GetString(SoftwareRestrictionKeys.ListHeaderName);

    /// <summary>
    /// Gets the second list column header for the selected section.
    /// </summary>
    public string CurrentListHeaderSetting => IsAdditionalRulesSectionSelected
        ? GetString(SoftwareRestrictionKeys.ListHeaderType)
        : GetString(SoftwareRestrictionKeys.ListHeaderSetting);

    /// <summary>
    /// Gets the additional rules security level column header.
    /// </summary>
    public string CurrentListHeaderSecurityLevel => IsAdditionalRulesSectionSelected
        ? GetString(SoftwareRestrictionKeys.ListHeaderSecurityLevel)
        : string.Empty;

    /// <summary>
    /// Gets the additional rules description column header.
    /// </summary>
    public string CurrentListHeaderDescription => IsAdditionalRulesSectionSelected
        ? GetString(SoftwareRestrictionKeys.ListHeaderDescription)
        : string.Empty;

    /// <summary>
    /// Gets the additional rules last modified column header.
    /// </summary>
    public string CurrentListHeaderLastModified => IsAdditionalRulesSectionSelected
        ? GetString(SoftwareRestrictionKeys.ListHeaderLastModified)
        : string.Empty;

    private bool IsAdditionalRulesSectionSelected =>
        SelectedSection?.Kind == SoftwareRestrictionSectionKind.AdditionalRules;

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
    /// Saves enforcement settings.
    /// </summary>
    /// <param name="settings">The settings to save.</param>
    /// <returns><see langword="true"/> when the operation succeeded.</returns>
    public async Task<bool> SaveEnforcementAsync(SoftwareRestrictionEnforcementSettings settings)
    {
        return await RunSaveAsync(
            () => _policyService.SaveEnforcement(settings),
            SoftwareRestrictionKeys.StatusPolicySaved);
    }

    /// <summary>
    /// Saves the default security level while preserving the rest of the enforcement settings.
    /// </summary>
    /// <param name="securityLevel">The selected default security level.</param>
    /// <returns><see langword="true"/> when the operation succeeded.</returns>
    public async Task<bool> SaveDefaultSecurityLevelAsync(SoftwareRestrictionSecurityLevel securityLevel)
    {
        SoftwareRestrictionEnforcementSettings current = PolicyState.Enforcement;
        return await SaveEnforcementAsync(new SoftwareRestrictionEnforcementSettings
        {
            DefaultSecurityLevel = securityLevel,
            UserScope = current.UserScope,
            FileScope = current.FileScope,
            CertificateRulesEnabled = current.CertificateRulesEnabled
        });
    }

    /// <summary>
    /// Saves designated file types.
    /// </summary>
    /// <param name="fileTypes">The file type extensions.</param>
    /// <returns><see langword="true"/> when the operation succeeded.</returns>
    public async Task<bool> SaveDesignatedFileTypesAsync(IEnumerable<string> fileTypes)
    {
        return await RunSaveAsync(
            () => _policyService.SaveDesignatedFileTypes(fileTypes),
            SoftwareRestrictionKeys.StatusPolicySaved);
    }

    /// <summary>
    /// Saves trusted publisher settings.
    /// </summary>
    /// <param name="settings">The settings to save.</param>
    /// <returns><see langword="true"/> when the operation succeeded.</returns>
    public async Task<bool> SaveTrustedPublishersAsync(SoftwareRestrictionTrustedPublisherSettings settings)
    {
        return await RunSaveAsync(
            () => _policyService.SaveTrustedPublishers(settings),
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

    private async Task RunLoadAsync()
    {
        IsLoading = true;
        HasError = false;
        ErrorMessage = string.Empty;

        try
        {
            PolicyState = await Task.Run(() => _policyService.LoadPolicy());
            OnPropertyChanged(nameof(IsConfigured));
            OnPropertyChanged(nameof(CanCreatePolicy));
            RefreshCurrentItems();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SoftwareRestrictionViewModel] Failed to load Software Restriction Policies.");
            SetError(ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task<bool> RunSaveAsync(Action operation, string statusResourceKey)
    {
        HasError = false;
        ErrorMessage = string.Empty;

        try
        {
            await Task.Run(operation);
            StatusMessage = GetString(statusResourceKey);
            await RunLoadAsync();
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

    private void RefreshCurrentItems()
    {
        SelectedItem = null;
        _allItems = BuildItemsForCurrentSection();
        ApplyFilter();
        OnPropertyChanged(nameof(CanDeleteSelectedRule));
        OnPropertyChanged(nameof(CanSetSelectedSecurityLevelDefault));
    }

    private List<SoftwareRestrictionListItem> BuildItemsForCurrentSection()
    {
        if (SelectedSection is null)
        {
            return [];
        }

        if (!PolicyState.IsConfigured)
        {
            return
            [
                new SoftwareRestrictionListItem
                {
                    Name = GetString(SoftwareRestrictionKeys.NoPolicyDefined),
                    Setting = GetString(SoftwareRestrictionKeys.NoPolicyDefinedDescription)
                }
            ];
        }

        return SelectedSection.Kind switch
        {
            SoftwareRestrictionSectionKind.SecurityLevels => BuildSecurityLevelItems(),
            SoftwareRestrictionSectionKind.AdditionalRules => BuildRuleItems(),
            SoftwareRestrictionSectionKind.Enforcement => BuildEnforcementItems(),
            SoftwareRestrictionSectionKind.DesignatedFileTypes => BuildDesignatedFileTypeItems(),
            SoftwareRestrictionSectionKind.TrustedPublishers => BuildTrustedPublisherItems(),
            _ => []
        };
    }

    private List<SoftwareRestrictionListItem> BuildSecurityLevelItems()
    {
        SoftwareRestrictionSecurityLevel defaultLevel = PolicyState.Enforcement.DefaultSecurityLevel;

        return
        [
            new SoftwareRestrictionListItem
            {
                Name = SoftwareRestrictionRule.FormatSecurityLevel(SoftwareRestrictionSecurityLevel.Disallowed),
                Setting = FormatSecurityLevelDescription(SoftwareRestrictionSecurityLevel.Disallowed, defaultLevel),
                SecurityLevel = SoftwareRestrictionSecurityLevel.Disallowed
            },
            new SoftwareRestrictionListItem
            {
                Name = SoftwareRestrictionRule.FormatSecurityLevel(SoftwareRestrictionSecurityLevel.BasicUser),
                Setting = FormatSecurityLevelDescription(SoftwareRestrictionSecurityLevel.BasicUser, defaultLevel),
                SecurityLevel = SoftwareRestrictionSecurityLevel.BasicUser
            },
            new SoftwareRestrictionListItem
            {
                Name = SoftwareRestrictionRule.FormatSecurityLevel(SoftwareRestrictionSecurityLevel.Unrestricted),
                Setting = FormatSecurityLevelDescription(SoftwareRestrictionSecurityLevel.Unrestricted, defaultLevel),
                SecurityLevel = SoftwareRestrictionSecurityLevel.Unrestricted
            }
        ];
    }

    private List<SoftwareRestrictionListItem> BuildRuleItems()
    {
        if (PolicyState.Rules.Count == 0)
        {
            return
            [
                new SoftwareRestrictionListItem
                {
                    Name = GetString(SoftwareRestrictionKeys.NoAdditionalRules),
                    Setting = string.Empty
                }
            ];
        }

        return PolicyState.Rules
            .Select(rule => new SoftwareRestrictionListItem
            {
                Name = rule.DisplayValue,
                Setting = rule.KindDisplayName,
                RuleSecurityLevel = rule.SecurityLevelDisplayName,
                Description = rule.Description,
                LastModified = FormatLastModified(rule.LastModified),
                Rule = rule
            })
            .ToList();
    }

    private List<SoftwareRestrictionListItem> BuildEnforcementItems()
    {
        SoftwareRestrictionEnforcementSettings settings = PolicyState.Enforcement;

        return
        [
            new SoftwareRestrictionListItem
            {
                Name = GetString(SoftwareRestrictionKeys.EnforcementDefaultSecurityLevel),
                Setting = SoftwareRestrictionRule.FormatSecurityLevel(settings.DefaultSecurityLevel)
            },
            new SoftwareRestrictionListItem
            {
                Name = GetString(SoftwareRestrictionKeys.EnforcementUserScope),
                Setting = FormatUserScope(settings.UserScope)
            },
            new SoftwareRestrictionListItem
            {
                Name = GetString(SoftwareRestrictionKeys.EnforcementFileScope),
                Setting = FormatFileScope(settings.FileScope)
            },
            new SoftwareRestrictionListItem
            {
                Name = GetString(SoftwareRestrictionKeys.EnforcementCertificateRules),
                Setting = FormatBoolean(settings.CertificateRulesEnabled)
            }
        ];
    }

    private List<SoftwareRestrictionListItem> BuildDesignatedFileTypeItems()
    {
        return PolicyState.DesignatedFileTypes.Count == 0
            ?
            [
                new SoftwareRestrictionListItem
                {
                    Name = GetString(SoftwareRestrictionKeys.NoDesignatedFileTypes),
                    Setting = string.Empty
                }
            ]
            : PolicyState.DesignatedFileTypes
                .Select(fileType => new SoftwareRestrictionListItem
                {
                    Name = fileType,
                    Setting = GetString(SoftwareRestrictionKeys.DesignatedFileTypeSetting)
                })
                .ToList();
    }

    private List<SoftwareRestrictionListItem> BuildTrustedPublisherItems()
    {
        SoftwareRestrictionTrustedPublisherSettings settings = PolicyState.TrustedPublishers;

        return
        [
            new SoftwareRestrictionListItem
            {
                Name = GetString(SoftwareRestrictionKeys.TrustedPublishersDefineSettings),
                Setting = FormatBoolean(settings.IsDefined)
            },
            new SoftwareRestrictionListItem
            {
                Name = GetString(SoftwareRestrictionKeys.TrustedPublishersManagement),
                Setting = settings.IsDefined
                    ? FormatPublisherScope(settings.PublisherScope)
                    : GetString(SecPolKeys.ValueNotDefined)
            },
            new SoftwareRestrictionListItem
            {
                Name = GetString(SoftwareRestrictionKeys.TrustedPublishersPublisherRevocation),
                Setting = settings.IsDefined
                    ? FormatBoolean(settings.CheckPublisherRevocation)
                    : GetString(SecPolKeys.ValueNotDefined)
            },
            new SoftwareRestrictionListItem
            {
                Name = GetString(SoftwareRestrictionKeys.TrustedPublishersTimestampRevocation),
                Setting = settings.IsDefined
                    ? FormatBoolean(settings.CheckTimestampRevocation)
                    : GetString(SecPolKeys.ValueNotDefined)
            }
        ];
    }

    private void ApplyFilter()
    {
        CurrentItems.Clear();

        IEnumerable<SoftwareRestrictionListItem> items = string.IsNullOrWhiteSpace(FilterText)
            ? _allItems
            : _allItems.Where(item =>
                item.Name.Contains(FilterText, StringComparison.CurrentCultureIgnoreCase)
                || item.Setting.Contains(FilterText, StringComparison.CurrentCultureIgnoreCase)
                || item.RuleSecurityLevel.Contains(FilterText, StringComparison.CurrentCultureIgnoreCase)
                || item.Description.Contains(FilterText, StringComparison.CurrentCultureIgnoreCase)
                || item.LastModified.Contains(FilterText, StringComparison.CurrentCultureIgnoreCase)
                || (item.Rule?.CertificateSubject.Contains(FilterText, StringComparison.CurrentCultureIgnoreCase) == true)
                || (item.Rule?.CertificateIssuer.Contains(FilterText, StringComparison.CurrentCultureIgnoreCase) == true)
                || (item.Rule?.CertificateSerialNumber.Contains(FilterText, StringComparison.CurrentCultureIgnoreCase) == true)
                || (item.Rule?.CertificateThumbprint.Contains(FilterText, StringComparison.CurrentCultureIgnoreCase) == true));

        foreach (SoftwareRestrictionListItem item in items)
        {
            CurrentItems.Add(item);
        }
    }

    partial void OnSelectedSectionChanged(SoftwareRestrictionSectionItem? value)
    {
        OnPropertyChanged(nameof(CurrentListHeaderName));
        OnPropertyChanged(nameof(CurrentListHeaderSetting));
        OnPropertyChanged(nameof(CurrentListHeaderSecurityLevel));
        OnPropertyChanged(nameof(CurrentListHeaderDescription));
        OnPropertyChanged(nameof(CurrentListHeaderLastModified));
        RefreshCurrentItems();
    }

    partial void OnSelectedItemChanged(SoftwareRestrictionListItem? value)
    {
        OnPropertyChanged(nameof(CanDeleteSelectedRule));
        OnPropertyChanged(nameof(CanSetSelectedSecurityLevelDefault));
    }

    partial void OnPolicyStateChanged(SoftwareRestrictionPolicyState value)
    {
        OnPropertyChanged(nameof(IsConfigured));
        OnPropertyChanged(nameof(CanCreatePolicy));
        OnPropertyChanged(nameof(CanSetSelectedSecurityLevelDefault));
    }

    partial void OnFilterTextChanged(string value)
    {
        ApplyFilter();
    }

    private static string FormatSecurityLevelDescription(
        SoftwareRestrictionSecurityLevel level,
        SoftwareRestrictionSecurityLevel defaultLevel)
    {
        string description = level switch
        {
            SoftwareRestrictionSecurityLevel.BasicUser => GetString(SoftwareRestrictionKeys.SecurityLevelBasicUserDescription),
            SoftwareRestrictionSecurityLevel.Unrestricted => GetString(SoftwareRestrictionKeys.SecurityLevelUnrestrictedDescription),
            _ => GetString(SoftwareRestrictionKeys.SecurityLevelDisallowedDescription)
        };

        if (level != defaultLevel)
        {
            return description;
        }

        return string.Format(
            CultureInfo.CurrentCulture,
            GetString(SoftwareRestrictionKeys.DefaultMarkerFormat),
            description,
            GetString(SoftwareRestrictionKeys.DefaultMarker));
    }

    private static string FormatLastModified(DateTimeOffset? lastModified)
    {
        return lastModified?.ToString("G", CultureInfo.CurrentCulture) ?? string.Empty;
    }

    private static string FormatUserScope(SoftwareRestrictionUserScope scope)
    {
        return scope == SoftwareRestrictionUserScope.AllUsersExceptLocalAdministrators
            ? GetString(SoftwareRestrictionKeys.UserScopeAllExceptAdministrators)
            : GetString(SoftwareRestrictionKeys.UserScopeAllUsers);
    }

    private static string FormatFileScope(SoftwareRestrictionFileScope scope)
    {
        return scope == SoftwareRestrictionFileScope.AllSoftwareFiles
            ? GetString(SoftwareRestrictionKeys.FileScopeAllSoftwareFiles)
            : GetString(SoftwareRestrictionKeys.FileScopeExecutableFilesOnly);
    }

    private static string FormatPublisherScope(SoftwareRestrictionPublisherScope scope)
    {
        return scope switch
        {
            SoftwareRestrictionPublisherScope.LocalAdministrators => GetString(SoftwareRestrictionKeys.PublisherScopeLocalAdministrators),
            SoftwareRestrictionPublisherScope.EnterpriseAdministrators => GetString(SoftwareRestrictionKeys.PublisherScopeEnterpriseAdministrators),
            _ => GetString(SoftwareRestrictionKeys.PublisherScopeEndUsers)
        };
    }

    private static string FormatBoolean(bool value)
    {
        return value
            ? GetString(SecPolKeys.ValueEnabled)
            : GetString(SecPolKeys.ValueDisabled);
    }

    private static string GetString(string key, string resourceFileName = ResourceFileNames.SecPol)
    {
        string value = LocalizationProvider.Current.GetString(resourceFileName, key);
        return string.IsNullOrWhiteSpace(value) ? key : value;
    }
}
