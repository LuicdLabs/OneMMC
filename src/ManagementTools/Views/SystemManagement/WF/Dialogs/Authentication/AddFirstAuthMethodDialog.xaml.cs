using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Authentication;
using ManagementTools.Core.Features.SystemManagement.Models.WF.ConnectionSecurity;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Monitoring;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Profiles;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Rules;
using ManagementTools.Helpers;
using ManagementTools.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ManagementTools.Views.Dialogs.Authentication;

public sealed partial class AddFirstAuthMethodDialog : UserControl
{
    private readonly Action<ElementTheme> _themeChangedHandler;
    private AdvancedCertCriteriaResult? _advancedCriteria;

    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    public AuthMethodDialogResult? Result { get; private set; }

    public AddFirstAuthMethodDialog()
    {
        InitializeComponent();
        RequestedTheme = App.CurrentTheme;
        _themeChangedHandler = theme => RequestedTheme = theme;
        Loaded += AddFirstAuthMethodDialog_Loaded;
        Unloaded += AddFirstAuthMethodDialog_Unloaded;

        SetPanelEnabled(CertPanel, false);
    }

    private void AddFirstAuthMethodDialog_Loaded(object sender, RoutedEventArgs e)
    {
        App.ThemeChanged -= _themeChangedHandler;
        App.ThemeChanged += _themeChangedHandler;
    }

    private void AddFirstAuthMethodDialog_Unloaded(object sender, RoutedEventArgs e)
    {
        App.ThemeChanged -= _themeChangedHandler;
    }

    private async void OpenSubDialog(AdvancedCertCriteriaDialog dialog)
    {
        dialog.ApplyResult(_advancedCriteria);

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
        if (modalResult == WindowDialogResult.Primary && dialog.Result is not null)
        {
            _advancedCriteria = dialog.Result;
        }
        else if (modalResult == WindowDialogResult.None)
        {
            _advancedCriteria = null;
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

    private void CertRadio_Checked(object sender, RoutedEventArgs e)
    {
        SetPanelEnabled(CertPanel, true);
        ResetValidationMessage();
    }

    private void CertRadio_Unchecked(object sender, RoutedEventArgs e)
    {
        SetPanelEnabled(CertPanel, false);
        ResetValidationMessage();
    }

    private void PresharedRadio_Checked(object sender, RoutedEventArgs e)
    {
        PresharedKeyTextBox.IsEnabled = true;
        ResetValidationMessage();
    }

    private void PresharedRadio_Unchecked(object sender, RoutedEventArgs e)
    {
        PresharedKeyTextBox.IsEnabled = false;
        ResetValidationMessage();
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        var pickerService = App.GetRequiredService<CertificateAuthorityPickerService>();
        string? distinguishedName = pickerService.PickCaDistinguishedName(
            hwnd,
            StoreLocation.LocalMachine,
            GetSelectedCertificateAuthorityStoreName(CertStoreTypeCombo));
        if (!string.IsNullOrWhiteSpace(distinguishedName))
        {
            CertCATextBox.Text = distinguishedName;
            ResetValidationMessage();
        }
    }

    private void AdvancedButton_Click(object sender, RoutedEventArgs e)
        => OpenSubDialog(new AdvancedCertCriteriaDialog());

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
        if (KerberosRadio.IsChecked == true)
        {
            Result = new AuthMethodDialogResult
            {
                Kind = "ComputerKerberos",
                Method = LocalizedStrings.WF_AuthMethod_ComputerKerberos,
                Details = LocalizedStrings.WF_AuthDetails_KerberosAuthentication
            };
            return;
        }

        if (NtlmRadio.IsChecked == true)
        {
            Result = new AuthMethodDialogResult
            {
                Kind = "ComputerNtlm",
                Method = LocalizedStrings.WF_AuthMethod_ComputerNtlm,
                Details = LocalizedStrings.WF_AuthDetails_NtlmAuthentication
            };
            return;
        }

        if (CertRadio.IsChecked == true)
        {
            string signing = AuthMethodValueMapper.NormalizeSigningAlgorithmTag(GetComboTag(SigningAlgorithmCombo));
            string storeType = AuthMethodValueMapper.NormalizeCertificateStoreTypeTag(GetComboTag(CertStoreTypeCombo));
            string ca = CertCATextBox.Text.Trim();

            Result = new AuthMethodDialogResult
            {
                Kind = "ComputerCertificate",
                Method = LocalizedStrings.WF_AuthMethod_ComputerCertificateFromCA,
                Details = AuthMethodValueMapper.BuildCertificateDetails(
                    ca,
                    signing,
                    storeType,
                    HealthCertCheckBox.IsChecked == true,
                    CertMappingCheckBox.IsChecked == true,
                    _advancedCriteria),
                SigningAlgorithm = signing,
                CertificateStoreType = storeType,
                CaDistinguishedName = ca,
                HealthCertificateOnly = HealthCertCheckBox.IsChecked == true,
                CertificateMappingEnabled = CertMappingCheckBox.IsChecked == true,
                AdvancedCriteria = _advancedCriteria
            };
            return;
        }

        string key = PresharedKeyTextBox.Text.Trim();
        Result = new AuthMethodDialogResult
        {
            Kind = "PresharedKey",
            Method = LocalizedStrings.WF_AuthMethod_PresharedKey,
            Details = string.IsNullOrWhiteSpace(key) ? LocalizedStrings.WF_AuthDetails_KeyNotSet : LocalizedStrings.WF_AuthDetails_KeyConfigured,
            PresharedKey = key
        };
    }

    public void ApplyResult(AuthMethodDialogResult result)
    {
        _advancedCriteria = result.AdvancedCriteria;
        ResetValidationMessage();

        switch (result.Kind)
        {
            case "ComputerKerberos":
                KerberosRadio.IsChecked = true;
                break;
            case "ComputerNtlm":
                NtlmRadio.IsChecked = true;
                break;
            case "ComputerCertificate":
            case "ComputerHealthCertificate":
                CertRadio.IsChecked = true;
                SelectComboByContent(SigningAlgorithmCombo, result.SigningAlgorithm);
                SelectComboByContent(CertStoreTypeCombo, result.CertificateStoreType);
                CertCATextBox.Text = result.CaDistinguishedName;
                HealthCertCheckBox.IsChecked = result.HealthCertificateOnly ||
                    string.Equals(result.Kind, "ComputerHealthCertificate", StringComparison.Ordinal);
                CertMappingCheckBox.IsChecked = result.CertificateMappingEnabled;
                break;
            case "PresharedKey":
                PresharedRadio.IsChecked = true;
                PresharedKeyTextBox.Text = result.PresharedKey;
                break;
        }
    }

    private string? GetValidationErrorMessage()
    {
        if (CertRadio.IsChecked == true && string.IsNullOrWhiteSpace(CertCATextBox.Text))
        {
            return LocalizedStrings.WF_Validation_CertificateAuthorityPathRequired;
        }

        if (PresharedRadio.IsChecked == true && string.IsNullOrWhiteSpace(PresharedKeyTextBox.Text))
        {
            return LocalizedStrings.WF_Validation_PresharedKeyRequired;
        }

        return null;
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

    private static StoreName GetSelectedCertificateAuthorityStoreName(ComboBox comboBox)
    {
        return AuthMethodValueMapper.NormalizeCertificateStoreTypeTag(GetComboTag(comboBox)) switch
        {
            "IntermediateCA" => StoreName.CertificateAuthority,
            _ => StoreName.Root
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
