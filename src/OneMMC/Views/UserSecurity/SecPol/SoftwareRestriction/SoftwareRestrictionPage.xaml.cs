using System;
using System.ComponentModel;
using System.Globalization;
using OneMMC.Core.Abstractions.Services;
using OneMMC.Core.Features.UserSecurity.Models.SecPol.SoftwareRestriction;
using OneMMC.Core.Features.UserSecurity.ViewModels.SecPol.SoftwareRestriction;
using OneMMC.Core.Localization;
using OneMMC.Helpers;
using OneMMC.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace OneMMC.Views;

/// <summary>
/// Software Restriction Policies page. Thin code-behind: switches the section panels driven by
/// the <see cref="SelectorBar"/>, opens the rule editor/details dialogs, and forwards the
/// immediate-apply enforcement controls to the view model with revert-on-failure.
/// </summary>
public sealed partial class SoftwareRestrictionPage : Page
{
    private const string SecurityLevelsSectionTag = "SecurityLevels";
    private const string AdditionalRulesSectionTag = "AdditionalRules";
    private const string EnforcementSectionTag = "Enforcement";
    private const string DesignatedFileTypesSectionTag = "DesignatedFileTypes";
    private const string TrustedPublishersSectionTag = "TrustedPublishers";

    private readonly IFileDialogService _fileDialogService;
    private bool _hasLoaded;

    /// <summary>Gets the localized strings accessor used by compiled bindings.</summary>
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    /// <summary>Gets the page view model.</summary>
    public SoftwareRestrictionViewModel ViewModel { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SoftwareRestrictionPage"/> class.
    /// </summary>
    public SoftwareRestrictionPage()
    {
        _fileDialogService = App.GetRequiredService<IFileDialogService>();
        ViewModel = App.GetRequiredService<SoftwareRestrictionViewModel>();

        InitializeComponent();
        DataContext = ViewModel;
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
        ViewModel.AdminPermissionRequired += OnAdminPermissionRequired;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        if (_hasLoaded)
        {
            return;
        }

        _hasLoaded = true;
        SectionSelectorBar.SelectedItem = SecurityLevelsSelectorItem;
        await ViewModel.LoadPolicyAsync();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.AdminPermissionRequired -= OnAdminPermissionRequired;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        DataContext = null;
        Loaded -= OnPageLoaded;
        Unloaded -= OnPageUnloaded;
    }

    private async void OnAdminPermissionRequired(object? sender, EventArgs e)
    {
        await AdminDialogHelper.ShowAdminRequiredDialogAsync(XamlRoot);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SoftwareRestrictionViewModel.IsConfigured))
        {
            UpdateSectionVisibility();
        }
    }

    // ====================  Section switching  ====================

    private void SectionSelectorBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        UpdateSectionVisibility();
    }

    /// <summary>
    /// Shows the panel matching the selected section; all panels stay hidden until a policy exists
    /// so the not-configured empty state (bound in XAML) is the only visible content.
    /// </summary>
    private void UpdateSectionVisibility()
    {
        string sectionTag = SectionSelectorBar.SelectedItem?.Tag as string ?? SecurityLevelsSectionTag;
        bool isConfigured = ViewModel.IsConfigured;

        SecurityLevelsPanel.Visibility = ToVisibility(isConfigured && sectionTag == SecurityLevelsSectionTag);
        AdditionalRulesPanel.Visibility = ToVisibility(isConfigured && sectionTag == AdditionalRulesSectionTag);
        EnforcementPanel.Visibility = ToVisibility(isConfigured && sectionTag == EnforcementSectionTag);
        DesignatedFileTypesPanel.Visibility = ToVisibility(isConfigured && sectionTag == DesignatedFileTypesSectionTag);
        TrustedPublishersPanel.Visibility = ToVisibility(isConfigured && sectionTag == TrustedPublishersSectionTag);
    }

    private static Visibility ToVisibility(bool isVisible)
        => isVisible ? Visibility.Visible : Visibility.Collapsed;

    // ====================  Commands  ====================

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.RefreshAsync();
    }

    private async void CreatePolicyButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.CreateDefaultPolicyAsync();
    }

    private async void DeletePolicyButton_Click(object sender, RoutedEventArgs e)
    {
        if (await ShowConfirmDialogAsync(
            LocalizedStrings.SRP_Dialog_DeletePolicy_Title,
            LocalizedStrings.SRP_Dialog_DeletePolicy_Message,
            LocalizedStrings.Common_DeleteButton))
        {
            await ViewModel.DeletePolicyAsync();
        }
    }

    // ====================  Security levels  ====================

    private async void SetDefaultSecurityLevelButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SoftwareRestrictionSecurityLevelItem item }
            || item.Level == ViewModel.PolicyState.Enforcement.DefaultSecurityLevel)
        {
            return;
        }

        if (await ConfirmDefaultSecurityLevelAsync(item.Level))
        {
            await ViewModel.SaveDefaultSecurityLevelAsync(item.Level);
        }
    }

    /// <summary>
    /// Asks the user to confirm changing the default security level; the Disallowed level appends
    /// an extra warning because it blocks everything no allow rule covers.
    /// </summary>
    private async Task<bool> ConfirmDefaultSecurityLevelAsync(SoftwareRestrictionSecurityLevel securityLevel)
    {
        string message = string.Format(
            CultureInfo.CurrentCulture,
            GetString(SoftwareRestrictionKeys.DialogSetDefaultSecurityLevelMessageFormat),
            SoftwareRestrictionRule.FormatSecurityLevel(securityLevel));

        if (securityLevel == SoftwareRestrictionSecurityLevel.Disallowed)
        {
            message = $"{message}{Environment.NewLine}{Environment.NewLine}{LocalizedStrings.SRP_Dialog_DisallowedDefault_Warning}";
        }

        return await ShowConfirmDialogAsync(
            GetString(SoftwareRestrictionKeys.DialogSetDefaultSecurityLevelTitle),
            message,
            LocalizedStrings.SRP_Command_SetAsDefault);
    }

    // ====================  Additional rules  ====================

    private async void NewPathRule_Click(object sender, RoutedEventArgs e)
    {
        await ShowRuleDialogAsync(SoftwareRestrictionRuleKind.Path, null);
    }

    private async void NewHashRule_Click(object sender, RoutedEventArgs e)
    {
        await ShowRuleDialogAsync(SoftwareRestrictionRuleKind.Hash, null);
    }

    private async void NewCertificateRule_Click(object sender, RoutedEventArgs e)
    {
        await ShowRuleDialogAsync(SoftwareRestrictionRuleKind.Certificate, null);
    }

    private async void NewNetworkZoneRule_Click(object sender, RoutedEventArgs e)
    {
        await ShowRuleDialogAsync(SoftwareRestrictionRuleKind.NetworkZone, null);
    }

    private async void EditRuleButton_Click(object sender, RoutedEventArgs e)
    {
        await EditSelectedRuleAsync();
    }

    private async void RulesListView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        await EditSelectedRuleAsync();
    }

    private async void DeleteRuleButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedRule is not { } rule)
        {
            return;
        }

        if (await ShowConfirmDialogAsync(
            LocalizedStrings.SRP_Dialog_DeleteRule_Title,
            LocalizedStrings.SRP_Dialog_DeleteRule_Message,
            LocalizedStrings.Common_DeleteButton))
        {
            await ViewModel.DeleteRuleAsync(rule);
        }
    }

    private void RuleSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            ViewModel.FilterText = sender.Text;
        }
    }

    private async Task EditSelectedRuleAsync()
    {
        if (ViewModel.SelectedRule is not { } rule)
        {
            return;
        }

        if (rule.Kind is SoftwareRestrictionRuleKind.Path
            or SoftwareRestrictionRuleKind.Hash
            or SoftwareRestrictionRuleKind.NetworkZone)
        {
            await ShowRuleDialogAsync(rule.Kind, rule);
            return;
        }

        if (rule.CanViewDetails)
        {
            SoftwareRestrictionRuleDetailsDialog detailsDialog = new(rule);
            PrepareDialog(detailsDialog);
            _ = await detailsDialog.ShowAsync();
            return;
        }

        await ShowMessageDialogAsync(GetString(SoftwareRestrictionKeys.RuleUnsupportedForEdit));
    }

    private async Task ShowRuleDialogAsync(SoftwareRestrictionRuleKind kind, SoftwareRestrictionRule? existingRule)
    {
        SoftwareRestrictionRuleDialog dialog = new(kind, existingRule, _fileDialogService);
        PrepareDialog(dialog);

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.SaveRuleAsync(dialog.BuildRule());
        }
    }

    // ====================  Enforcement (immediate apply with revert-on-failure)  ====================

    private async void DefaultLevelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int selectedIndex = DefaultLevelComboBox.SelectedIndex;
        if (selectedIndex < 0 || selectedIndex == ViewModel.DefaultSecurityLevelIndex)
        {
            return;
        }

        SoftwareRestrictionSecurityLevel securityLevel = SoftwareRestrictionViewModel.SecurityLevelFromIndex(selectedIndex);
        bool confirmed = securityLevel != SoftwareRestrictionSecurityLevel.Disallowed
            || await ConfirmDefaultSecurityLevelAsync(securityLevel);

        if (!confirmed || !await ViewModel.SaveDefaultSecurityLevelAsync(securityLevel))
        {
            DefaultLevelComboBox.SelectedIndex = ViewModel.DefaultSecurityLevelIndex;
        }
    }

    private async void FileScopeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int selectedIndex = FileScopeComboBox.SelectedIndex;
        if (selectedIndex < 0 || selectedIndex == ViewModel.FileScopeIndex)
        {
            return;
        }

        SoftwareRestrictionFileScope fileScope = selectedIndex == 1
            ? SoftwareRestrictionFileScope.AllSoftwareFiles
            : SoftwareRestrictionFileScope.ExecutableFilesOnly;
        if (!await ViewModel.SaveFileScopeAsync(fileScope))
        {
            FileScopeComboBox.SelectedIndex = ViewModel.FileScopeIndex;
        }
    }

    private async void UserScopeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int selectedIndex = UserScopeComboBox.SelectedIndex;
        if (selectedIndex < 0 || selectedIndex == ViewModel.UserScopeIndex)
        {
            return;
        }

        SoftwareRestrictionUserScope userScope = selectedIndex == 1
            ? SoftwareRestrictionUserScope.AllUsersExceptLocalAdministrators
            : SoftwareRestrictionUserScope.AllUsers;
        if (!await ViewModel.SaveUserScopeAsync(userScope))
        {
            UserScopeComboBox.SelectedIndex = ViewModel.UserScopeIndex;
        }
    }

    private async void CertificateRulesToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (CertificateRulesToggle.IsOn == ViewModel.CertificateRulesEnabled)
        {
            return;
        }

        if (!await ViewModel.SaveCertificateRulesEnabledAsync(CertificateRulesToggle.IsOn))
        {
            CertificateRulesToggle.IsOn = ViewModel.CertificateRulesEnabled;
        }
    }

    // ====================  Designated file types  ====================

    private async void AddFileTypeButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.AddFileTypeAsync();
    }

    private async void RemoveFileTypeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SoftwareRestrictionFileTypeItem item })
        {
            await ViewModel.RemoveFileTypeAsync(item);
        }
    }

    // ====================  Trusted publishers (immediate apply with revert-on-failure)  ====================

    private async void TrustedPublishersDefinedToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (TrustedPublishersDefinedToggle.IsOn == ViewModel.TrustedPublishersDefined)
        {
            return;
        }

        if (!await ViewModel.SaveTrustedPublishersDefinedAsync(TrustedPublishersDefinedToggle.IsOn))
        {
            TrustedPublishersDefinedToggle.IsOn = ViewModel.TrustedPublishersDefined;
        }
    }

    private async void PublisherScopeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int selectedIndex = PublisherScopeComboBox.SelectedIndex;
        if (selectedIndex < 0 || selectedIndex == ViewModel.PublisherScopeIndex)
        {
            return;
        }

        if (!await ViewModel.SavePublisherScopeAsync((SoftwareRestrictionPublisherScope)selectedIndex))
        {
            PublisherScopeComboBox.SelectedIndex = ViewModel.PublisherScopeIndex;
        }
    }

    private async void PublisherRevocationToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (PublisherRevocationToggle.IsOn == ViewModel.CheckPublisherRevocation)
        {
            return;
        }

        if (!await ViewModel.SavePublisherRevocationAsync(PublisherRevocationToggle.IsOn))
        {
            PublisherRevocationToggle.IsOn = ViewModel.CheckPublisherRevocation;
        }
    }

    private async void TimestampRevocationToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (TimestampRevocationToggle.IsOn == ViewModel.CheckTimestampRevocation)
        {
            return;
        }

        if (!await ViewModel.SaveTimestampRevocationAsync(TimestampRevocationToggle.IsOn))
        {
            TimestampRevocationToggle.IsOn = ViewModel.CheckTimestampRevocation;
        }
    }

    // ====================  Dialog helpers  ====================

    private void PrepareDialog(ContentDialog dialog)
    {
        dialog.XamlRoot = XamlRoot;
        dialog.Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style;
        dialog.RequestedTheme = App.CurrentTheme;
    }

    private async Task<bool> ShowConfirmDialogAsync(string title, string message, string primaryButtonText)
    {
        ContentDialog dialog = new()
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = ContentDialogButton.Close
        };
        PrepareDialog(dialog);

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task ShowMessageDialogAsync(string message)
    {
        ContentDialog dialog = new()
        {
            Title = LocalizedStrings.Common_ErrorTitle,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = LocalizedStrings.Common_CloseButton,
            DefaultButton = ContentDialogButton.Close
        };
        PrepareDialog(dialog);

        _ = await dialog.ShowAsync();
    }

    private static string GetString(string key)
    {
        string value = LocalizationProvider.Current.GetString(ResourceFileNames.SecPol, key);
        return string.IsNullOrWhiteSpace(value) ? key : value;
    }
}
