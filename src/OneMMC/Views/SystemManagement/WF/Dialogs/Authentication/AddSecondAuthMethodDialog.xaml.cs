using System;
using System.Collections.Generic;
using System.Linq;
using OneMMC.Core.Features.SystemManagement.Infrastructure.WF;
using OneMMC.Core.Features.SystemManagement.Models.WF.Authentication;
using OneMMC.Core.Features.SystemManagement.Models.WF.ConnectionSecurity;
using OneMMC.Core.Features.SystemManagement.Models.WF.Monitoring;
using OneMMC.Core.Features.SystemManagement.Models.WF.Profiles;
using OneMMC.Core.Features.SystemManagement.Models.WF.Rules;
using OneMMC.Helpers;
using OneMMC.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OneMMC.Views.Dialogs.Authentication;

public sealed partial class AddSecondAuthMethodDialog : UserControl
{
    private enum SecondAuthCategory
    {
        None,
        User,
        ComputerHealth,
        Mixed
    }

    private readonly Action<ElementTheme> _themeChangedHandler;
    private AdvancedCertCriteriaResult? _userAdvancedCriteria;
    private AdvancedCertCriteriaResult? _computerAdvancedCriteria;
    private SecondAuthCategory _existingCategory;

    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    public AuthMethodDialogResult? Result { get; private set; }

    public AddSecondAuthMethodDialog()
    {
        InitializeComponent();
        RequestedTheme = App.CurrentTheme;
        _themeChangedHandler = theme => RequestedTheme = theme;
        Loaded += AddSecondAuthMethodDialog_Loaded;
        Unloaded += AddSecondAuthMethodDialog_Unloaded;

        SetPanelEnabled(UserCertPanel, false);
        SetPanelEnabled(ComputerCertPanel, false);
        ApplyCategoryConstraints();
    }

    private void AddSecondAuthMethodDialog_Loaded(object sender, RoutedEventArgs e)
    {
        App.ThemeChanged -= _themeChangedHandler;
        App.ThemeChanged += _themeChangedHandler;
    }

    private void AddSecondAuthMethodDialog_Unloaded(object sender, RoutedEventArgs e)
    {
        App.ThemeChanged -= _themeChangedHandler;
    }

    private async void OpenSubDialog(AdvancedCertCriteriaResult? current, Action<AdvancedCertCriteriaResult?> saveResult)
    {
        var dialog = new AdvancedCertCriteriaDialog();
        dialog.ApplyResult(current);

        var modalWindow = new ModalDialogWindow(new ModalDialogOptions
        {
            Title = LocalizedStrings.WF_AdvancedCertificateCriteria_Title,
            Content = dialog,
            OwnerXamlRoot = XamlRoot,
            RequestedTheme = App.CurrentTheme,
            ThemeChangeSubscribe = h => App.ThemeChanged += h,
            ThemeChangeUnsubscribe = h => App.ThemeChanged -= h,
            PrimaryButtonText = LocalizedStrings.Common_OKButton,
            CloseButtonText = LocalizedStrings.Common_ClearButton,
            DefaultButton = WindowDialogResult.Primary,
            Width = 760,
            Height = 680,
            OnPrimaryButtonClick = () =>
            {
                return dialog.TryCommitResult();
            },
            OnCloseButtonClick = dialog.ClearAll
        });

        var modalResult = await modalWindow.ShowDialogAsync();
        if (modalResult == WindowDialogResult.Primary)
        {
            saveResult(dialog.Result);
        }
        else if (modalResult == WindowDialogResult.None)
        {
            saveResult(null);
        }
    }

    /// <summary>Recursively sets IsEnabled on all Control children of a panel.</summary>
    private static void SetPanelEnabled(Panel panel, bool enabled)
    {
        foreach (var child in panel.Children)
        {
            if (child is Control control)
                control.IsEnabled = enabled;
            else if (child is Panel nested)
                SetPanelEnabled(nested, enabled);
        }
    }

    private void UserCertRadio_Checked(object sender, RoutedEventArgs e)
    {
        SetPanelEnabled(UserCertPanel, true);
        ResetValidationMessage();
    }

    private void UserCertRadio_Unchecked(object sender, RoutedEventArgs e)
    {
        SetPanelEnabled(UserCertPanel, false);
        ResetValidationMessage();
    }

    private void ComputerHealthCertRadio_Checked(object sender, RoutedEventArgs e)
    {
        SetPanelEnabled(ComputerCertPanel, true);
        ResetValidationMessage();
    }

    private void ComputerHealthCertRadio_Unchecked(object sender, RoutedEventArgs e)
    {
        SetPanelEnabled(ComputerCertPanel, false);
        ResetValidationMessage();
    }

    private void UserBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        var pickerService = App.GetRequiredService<CertificateAuthorityPickerService>();
        CertificateAuthorityPickResult? result = pickerService.PickCertificateAuthority(
            hwnd,
            GetSelectedCertificateAuthorityStoreKind(UserCertStoreCombo),
            new CertificateAuthorityPickerStrings(
                LocalizedStrings.WF_CertificatePicker_Title,
                LocalizedStrings.WF_CertificatePicker_Prompt));
        if (result is not null)
        {
            UserCertCATextBox.Text = result.DistinguishedName;
            ResetValidationMessage();
        }
    }

    private void CompBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        var pickerService = App.GetRequiredService<CertificateAuthorityPickerService>();
        CertificateAuthorityPickResult? result = pickerService.PickCertificateAuthority(
            hwnd,
            GetSelectedCertificateAuthorityStoreKind(CompCertStoreCombo),
            new CertificateAuthorityPickerStrings(
                LocalizedStrings.WF_CertificatePicker_Title,
                LocalizedStrings.WF_CertificatePicker_Prompt));
        if (result is not null)
        {
            CompCertCATextBox.Text = result.DistinguishedName;
            ResetValidationMessage();
        }
    }

    private void UserAdvancedButton_Click(object sender, RoutedEventArgs e) =>
        OpenSubDialog(_userAdvancedCriteria, result => _userAdvancedCriteria = result);

    private void CompAdvancedButton_Click(object sender, RoutedEventArgs e) =>
        OpenSubDialog(_computerAdvancedCriteria, result => _computerAdvancedCriteria = result);

    public void ApplyExistingMethods(IEnumerable<AuthMethodDialogResult> existingMethods)
    {
        _existingCategory = GetSecondAuthCategory(existingMethods);
        ResetValidationMessage();
        ApplyCategoryConstraints();
    }

    public bool TryCommitResult()
    {
        string? validationError = GetValidationErrorMessage();
        if (validationError is not null)
        {
            ShowValidationMessage(validationError);
            return false;
        }

        ResetValidationMessage();
        CommitResult();
        return true;
    }

    public void CommitResult()
    {
        if (UserKerberosRadio.IsChecked == true)
        {
            Result = new AuthMethodDialogResult
            {
                Kind = "UserKerberos",
                Method = LocalizedStrings.WF_AuthMethod_UserKerberos,
                Details = LocalizedStrings.WF_AuthDetails_KerberosAuthentication
            };
            return;
        }

        if (UserNtlmRadio.IsChecked == true)
        {
            Result = new AuthMethodDialogResult
            {
                Kind = "UserNtlm",
                Method = LocalizedStrings.WF_AuthMethod_UserNtlm,
                Details = LocalizedStrings.WF_AuthDetails_NtlmAuthentication
            };
            return;
        }

        if (UserCertRadio.IsChecked == true)
        {
            string signing = AuthMethodValueMapper.NormalizeSigningAlgorithmTag(GetComboTag(UserSigningAlgoCombo));
            string storeType = AuthMethodValueMapper.NormalizeCertificateStoreTypeTag(GetComboTag(UserCertStoreCombo));
            string ca = UserCertCATextBox.Text.Trim();

            Result = new AuthMethodDialogResult
            {
                Kind = "UserCertificate",
                Method = LocalizedStrings.WF_AuthMethod_UserCertificateFromCA,
                Details = AuthMethodValueMapper.BuildCertificateDetails(
                    ca,
                    signing,
                    storeType,
                    healthCertificatesOnly: false,
                    certificateMappingEnabled: UserCertMappingCheckBox.IsChecked == true,
                    advancedCriteria: _userAdvancedCriteria),
                SigningAlgorithm = signing,
                CertificateStoreType = storeType,
                CaDistinguishedName = ca,
                CertificateMappingEnabled = UserCertMappingCheckBox.IsChecked == true,
                AdvancedCriteria = _userAdvancedCriteria
            };
            return;
        }

        string compSigning = AuthMethodValueMapper.NormalizeSigningAlgorithmTag(GetComboTag(CompSigningAlgoCombo));
        string compStoreType = AuthMethodValueMapper.NormalizeCertificateStoreTypeTag(GetComboTag(CompCertStoreCombo));
        string compCa = CompCertCATextBox.Text.Trim();

        Result = new AuthMethodDialogResult
        {
            Kind = "ComputerHealthCertificate",
            Method = LocalizedStrings.WF_AuthMethod_ComputerHealthCertificateFromCA,
            Details = AuthMethodValueMapper.BuildCertificateDetails(
                compCa,
                compSigning,
                compStoreType,
                healthCertificatesOnly: false,
                certificateMappingEnabled: CompCertMappingCheckBox.IsChecked == true,
                advancedCriteria: _computerAdvancedCriteria),
            SigningAlgorithm = compSigning,
            CertificateStoreType = compStoreType,
            CaDistinguishedName = compCa,
            CertificateMappingEnabled = CompCertMappingCheckBox.IsChecked == true,
            AdvancedCriteria = _computerAdvancedCriteria
        };
    }

    public void ApplyResult(AuthMethodDialogResult result)
    {
        ResetValidationMessage();

        switch (result.Kind)
        {
            case "UserKerberos":
                UserKerberosRadio.IsChecked = true;
                break;
            case "UserNtlm":
                UserNtlmRadio.IsChecked = true;
                break;
            case "UserCertificate":
                UserCertRadio.IsChecked = true;
                SelectComboByContent(UserSigningAlgoCombo, result.SigningAlgorithm);
                SelectComboByContent(UserCertStoreCombo, result.CertificateStoreType);
                UserCertCATextBox.Text = result.CaDistinguishedName;
                UserCertMappingCheckBox.IsChecked = result.CertificateMappingEnabled;
                _userAdvancedCriteria = result.AdvancedCriteria;
                break;
            case "ComputerHealthCertificate":
                ComputerHealthCertRadio.IsChecked = true;
                SelectComboByContent(CompSigningAlgoCombo, result.SigningAlgorithm);
                SelectComboByContent(CompCertStoreCombo, result.CertificateStoreType);
                CompCertCATextBox.Text = result.CaDistinguishedName;
                CompCertMappingCheckBox.IsChecked = result.CertificateMappingEnabled;
                _computerAdvancedCriteria = result.AdvancedCriteria;
                break;
        }

        ApplyCategoryConstraints();
    }

    private string? GetValidationErrorMessage()
    {
        if (UserCertRadio.IsChecked == true)
        {
            if (string.IsNullOrWhiteSpace(UserCertCATextBox.Text))
            {
                return LocalizedStrings.WF_Validation_CertificateAuthorityPathRequired;
            }

            if (!CertificateAuthorityNameSupport.IsValidTrustedCaName(UserCertCATextBox.Text))
            {
                return LocalizedStrings.WF_Validation_CertificateAuthorityNameInvalid;
            }
        }

        if (ComputerHealthCertRadio.IsChecked == true)
        {
            if (string.IsNullOrWhiteSpace(CompCertCATextBox.Text))
            {
                return LocalizedStrings.WF_Validation_CertificateAuthorityPathRequired;
            }

            if (!CertificateAuthorityNameSupport.IsValidTrustedCaName(CompCertCATextBox.Text))
            {
                return LocalizedStrings.WF_Validation_CertificateAuthorityNameInvalid;
            }
        }

        SecondAuthCategory selectedCategory = GetSelectedCategory();
        if ((_existingCategory == SecondAuthCategory.User && selectedCategory == SecondAuthCategory.ComputerHealth) ||
            (_existingCategory == SecondAuthCategory.ComputerHealth && selectedCategory == SecondAuthCategory.User) ||
            _existingCategory == SecondAuthCategory.Mixed)
        {
            return LocalizedStrings.WF_Validation_SecondAuthenticationCategoryMismatch;
        }

        return null;
    }

    private SecondAuthCategory GetSelectedCategory()
    {
        if (ComputerHealthCertRadio.IsChecked == true)
        {
            return SecondAuthCategory.ComputerHealth;
        }

        return SecondAuthCategory.User;
    }

    private void ApplyCategoryConstraints()
    {
        bool restrictToUser = _existingCategory == SecondAuthCategory.User;
        bool restrictToComputerHealth = _existingCategory == SecondAuthCategory.ComputerHealth;

        if (restrictToUser && ComputerHealthCertRadio.IsChecked == true)
        {
            SelectDefaultUserMethod();
        }
        else if (restrictToComputerHealth && GetSelectedCategory() != SecondAuthCategory.ComputerHealth)
        {
            ComputerHealthCertRadio.IsChecked = true;
        }

        UserKerberosRadio.IsEnabled = !restrictToComputerHealth;
        UserNtlmRadio.IsEnabled = !restrictToComputerHealth;
        UserCertRadio.IsEnabled = !restrictToComputerHealth;
        ComputerHealthCertRadio.IsEnabled = !restrictToUser;

        SetPanelEnabled(UserCertPanel, UserCertRadio.IsChecked == true && UserCertRadio.IsEnabled);
        SetPanelEnabled(ComputerCertPanel, ComputerHealthCertRadio.IsChecked == true && ComputerHealthCertRadio.IsEnabled);
    }

    private void SelectDefaultUserMethod()
    {
        if (UserKerberosRadio.IsEnabled)
        {
            UserKerberosRadio.IsChecked = true;
            return;
        }

        if (UserNtlmRadio.IsEnabled)
        {
            UserNtlmRadio.IsChecked = true;
            return;
        }

        if (UserCertRadio.IsEnabled)
        {
            UserCertRadio.IsChecked = true;
        }
    }

    private void ShowValidationMessage(string message)
    {
        ValidationMessageTextBlock.Text = message;
        ValidationMessageTextBlock.Visibility = Visibility.Visible;
    }

    private void ResetValidationMessage()
    {
        ValidationMessageTextBlock.Text = string.Empty;
        ValidationMessageTextBlock.Visibility = Visibility.Collapsed;
    }

    private static string GetComboTag(ComboBox comboBox) =>
        (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString()
            ?? (comboBox.SelectedItem as ComboBoxItem)?.Content?.ToString()
            ?? string.Empty;

    private static CertificateAuthorityStoreKind GetSelectedCertificateAuthorityStoreKind(ComboBox comboBox)
    {
        return AuthMethodValueMapper.NormalizeCertificateStoreTypeTag(GetComboTag(comboBox)) switch
        {
            "IntermediateCA" => CertificateAuthorityStoreKind.IntermediateCA,
            _ => CertificateAuthorityStoreKind.RootCA
        };
    }

    private static SecondAuthCategory GetSecondAuthCategory(IEnumerable<AuthMethodDialogResult> methods)
    {
        HashSet<SecondAuthCategory> categories = methods
            .Select(method => method.Kind)
            .Select(GetSecondAuthCategory)
            .Where(category => category is not SecondAuthCategory.None and not SecondAuthCategory.Mixed)
            .ToHashSet();

        return categories.Count switch
        {
            0 => SecondAuthCategory.None,
            1 => categories.Single(),
            _ => SecondAuthCategory.Mixed
        };
    }

    private static SecondAuthCategory GetSecondAuthCategory(string? kind)
    {
        return kind switch
        {
            "UserKerberos" or "UserNtlm" or "UserCertificate" => SecondAuthCategory.User,
            "ComputerHealthCertificate" => SecondAuthCategory.ComputerHealth,
            _ => SecondAuthCategory.None
        };
    }

    private static void SelectComboByContent(ComboBox comboBox, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        for (int i = 0; i < comboBox.Items.Count; i++)
        {
            if (comboBox.Items[i] is ComboBoxItem item &&
                (string.Equals(item.Tag?.ToString(), content, StringComparison.Ordinal) ||
                 string.Equals(item.Content?.ToString(), content, StringComparison.Ordinal)))
            {
                comboBox.SelectedIndex = i;
                break;
            }
        }
    }
}
