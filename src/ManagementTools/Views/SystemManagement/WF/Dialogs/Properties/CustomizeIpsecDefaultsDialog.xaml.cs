using System;
using System.Linq;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Authentication;
using ManagementTools.Core.Features.SystemManagement.Models.WF.ConnectionSecurity;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Monitoring;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Profiles;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Rules;
using ManagementTools.Views.Dialogs.Authentication;
using ManagementTools.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ManagementTools.Views.Dialogs.WFProperties;

public sealed partial class CustomizeIpsecDefaultsDialog : ContentDialog
{
    private bool _isDialogInitialized;
    private bool _keyExchangeCustomized;
    private bool _dataProtectionCustomized;
    private bool _authenticationCustomized;

    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    public IpsecDefaultsModel Defaults { get; }

    public CustomizeIpsecDefaultsDialog(IpsecDefaultsModel defaults)
    {
        InitializeComponent();
        RequestedTheme = App.CurrentTheme;
        App.ThemeChanged += OnThemeChanged;
        Unloaded += CustomizeIpsecDefaultsDialog_Unloaded;
        PrimaryButtonClick += CustomizeIpsecDefaultsDialog_PrimaryButtonClick;

        Title = LocalizedStrings.WF_CustomizeIpsecDefaults_Title;
        Defaults = defaults;
        LoadDefaults(defaults);
        _isDialogInitialized = true;
        UpdateButtonStates();
    }

    private void KeyExchangeRadioButton_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isDialogInitialized)
        {
            return;
        }

        ResetValidationMessage();
        UpdateButtonStates();
    }

    private void DataProtectionRadioButton_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isDialogInitialized)
        {
            return;
        }

        ResetValidationMessage();
        UpdateButtonStates();
    }

    private void AuthenticationMethodRadioButton_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isDialogInitialized)
        {
            return;
        }

        ResetValidationMessage();
        UpdateButtonStates();
    }

    private async void KeyExchangeCustomizeButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AdvancedKeyExchangeSettingsDialog();
        dialog.ApplySecurityMethods(Defaults.AdvancedMainModeSecurityMethods);
        dialog.ApplyOptions(
            Defaults.MainModeKeyLifetimeMinutes,
            Defaults.MainModeKeyLifetimeSessions,
            Defaults.MainModeForceDiffieHellman);
        if (await dialog.ShowDialogAsync(XamlRoot) == ManagementTools.Helpers.WindowDialogResult.Primary)
        {
            Defaults.AdvancedMainModeSecurityMethods = dialog.GetSecurityMethods();
            Defaults.MainModeKeyLifetimeMinutes = dialog.GetLifetimeMinutes();
            Defaults.MainModeKeyLifetimeSessions = dialog.GetLifetimeSessions();
            Defaults.MainModeForceDiffieHellman = dialog.GetForceDiffieHellman();
            _keyExchangeCustomized = true;
            ResetValidationMessage();
        }
    }

    private async void DataProtectionCustomizeButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new DataProtectionSettingsDialog();
        dialog.ApplyAlgorithms(
            Defaults.AdvancedIntegrityAlgorithms,
            Defaults.AdvancedIntegrityEncryptionAlgorithms);

        if (await dialog.ShowDialogAsync(XamlRoot) == ManagementTools.Helpers.WindowDialogResult.Primary)
        {
            Defaults.AdvancedIntegrityAlgorithms = dialog.GetIntegrityAlgorithms();
            Defaults.AdvancedIntegrityEncryptionAlgorithms = dialog.GetIntegrityEncryptionAlgorithms();
            _dataProtectionCustomized = true;
            ResetValidationMessage();
        }
    }

    private async void AuthenticationCustomizeButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CustomizeAuthMethodsDialog();
        dialog.ApplySelections(
            Defaults.AdvancedFirstAuthMethods,
            Defaults.AdvancedSecondAuthMethods,
            Defaults.IsAdvancedFirstAuthOptional,
            Defaults.IsAdvancedSecondAuthOptional);

        if (await dialog.ShowDialogAsync(XamlRoot) == ManagementTools.Helpers.WindowDialogResult.Primary)
        {
            Defaults.AdvancedFirstAuthMethods = dialog.FirstMethods.ToList();
            Defaults.AdvancedSecondAuthMethods = dialog.SecondMethods.ToList();
            Defaults.IsAdvancedFirstAuthOptional = dialog.IsFirstAuthOptional;
            Defaults.IsAdvancedSecondAuthOptional = dialog.IsSecondAuthOptional;
            _authenticationCustomized = true;
            ResetValidationMessage();
        }
    }

    private void UpdateButtonStates()
    {
        if (KeyExchangeCustomizeButton is null ||
            KeyExchangeAdvancedRadioButton is null ||
            DataProtectionCustomizeButton is null ||
            DataProtectionAdvancedRadioButton is null ||
            AuthenticationCustomizeButton is null ||
            AuthenticationAdvancedRadioButton is null)
        {
            return;
        }

        KeyExchangeCustomizeButton.IsEnabled = KeyExchangeAdvancedRadioButton.IsChecked == true;
        DataProtectionCustomizeButton.IsEnabled = DataProtectionAdvancedRadioButton.IsChecked == true;
        AuthenticationCustomizeButton.IsEnabled = AuthenticationAdvancedRadioButton.IsChecked == true;
    }

    private void LoadDefaults(IpsecDefaultsModel defaults)
    {
        bool loadedKeyExchangeAdvanced = !string.Equals(defaults.KeyExchangeMode, "Default", StringComparison.OrdinalIgnoreCase);
        bool loadedDataProtectionAdvanced = !string.Equals(defaults.DataProtectionMode, "Default", StringComparison.OrdinalIgnoreCase);
        bool loadedAuthenticationAdvanced = string.Equals(defaults.AuthenticationMethodMode, "Advanced", StringComparison.OrdinalIgnoreCase);
        bool hasLoadedKeyExchangeMethods = defaults.AdvancedMainModeSecurityMethods.Count > 0;
        bool hasLoadedDataProtectionMethods =
            defaults.AdvancedIntegrityAlgorithms.Count > 0 ||
            defaults.AdvancedIntegrityEncryptionAlgorithms.Count > 0;
        bool hasLoadedAuthenticationMethods =
            defaults.AdvancedFirstAuthMethods.Count > 0 ||
            defaults.AdvancedSecondAuthMethods.Count > 0;

        KeyExchangeDefaultRadioButton.IsChecked = string.Equals(defaults.KeyExchangeMode, "Default", StringComparison.OrdinalIgnoreCase);
        KeyExchangeAdvancedRadioButton.IsChecked = !string.Equals(defaults.KeyExchangeMode, "Default", StringComparison.OrdinalIgnoreCase);

        DataProtectionDefaultRadioButton.IsChecked = string.Equals(defaults.DataProtectionMode, "Default", StringComparison.OrdinalIgnoreCase);
        DataProtectionAdvancedRadioButton.IsChecked = !string.Equals(defaults.DataProtectionMode, "Default", StringComparison.OrdinalIgnoreCase);

        switch (defaults.AuthenticationMethodMode)
        {
            case "Computer and user":
                AuthenticationComputerAndUserRadioButton.IsChecked = true;
                break;
            case "Computer":
                AuthenticationComputerRadioButton.IsChecked = true;
                break;
            case "User":
                AuthenticationUserRadioButton.IsChecked = true;
                break;
            case "Advanced":
                AuthenticationAdvancedRadioButton.IsChecked = true;
                break;
            default:
                AuthenticationDefaultRadioButton.IsChecked = true;
                break;
        }

        _keyExchangeCustomized = loadedKeyExchangeAdvanced && hasLoadedKeyExchangeMethods;
        _dataProtectionCustomized = loadedDataProtectionAdvanced && hasLoadedDataProtectionMethods;
        _authenticationCustomized = loadedAuthenticationAdvanced && hasLoadedAuthenticationMethods;

        if (defaults.AdvancedMainModeSecurityMethods.Count == 0)
        {
            defaults.AdvancedMainModeSecurityMethods =
            [
                new()
                {
                    IntegrityAlgorithm = "SHA-256",
                    EncryptionAlgorithm = "AES-CBC 256",
                    KeyExchangeAlgorithm = "Elliptic Curve Diffie-Hellman P-256"
                },
                new()
                {
                    IntegrityAlgorithm = "SHA-256",
                    EncryptionAlgorithm = "AES-CBC 192",
                    KeyExchangeAlgorithm = "Elliptic Curve Diffie-Hellman P-256"
                },
                new()
                {
                    IntegrityAlgorithm = "SHA-1",
                    EncryptionAlgorithm = "AES-CBC 128",
                    KeyExchangeAlgorithm = "Diffie-Hellman Group 2"
                },
                new()
                {
                    IntegrityAlgorithm = "SHA-1",
                    EncryptionAlgorithm = "3DES",
                    KeyExchangeAlgorithm = "Diffie-Hellman Group 2"
                }
            ];
        }

        if (!loadedDataProtectionAdvanced && defaults.AdvancedIntegrityAlgorithms.Count == 0)
        {
            defaults.AdvancedIntegrityAlgorithms =
            [
                new() { Protocol = "ESP", IntegrityAlgorithm = "SHA-256", MinutesLifetime = 60, KilobytesLifetime = 100000 },
                new() { Protocol = "AH", IntegrityAlgorithm = "SHA-256", MinutesLifetime = 60, KilobytesLifetime = 100000 },
                new() { Protocol = "ESP", IntegrityAlgorithm = "SHA-1", MinutesLifetime = 60, KilobytesLifetime = 100000 },
                new() { Protocol = "AH", IntegrityAlgorithm = "SHA-1", MinutesLifetime = 60, KilobytesLifetime = 100000 }
            ];
        }

        if (!loadedDataProtectionAdvanced && defaults.AdvancedIntegrityEncryptionAlgorithms.Count == 0)
        {
            defaults.AdvancedIntegrityEncryptionAlgorithms =
            [
                new()
                {
                    Protocol = "ESP",
                    IntegrityAlgorithm = "AES-GCM 256",
                    EncryptionAlgorithm = "AES-GCM 256",
                    MinutesLifetime = 60,
                    KilobytesLifetime = 100000
                },
                new()
                {
                    Protocol = "ESP",
                    IntegrityAlgorithm = "SHA-256",
                    EncryptionAlgorithm = "AES-CBC 256",
                    MinutesLifetime = 60,
                    KilobytesLifetime = 100000
                },
                new()
                {
                    Protocol = "ESP",
                    IntegrityAlgorithm = "SHA-1",
                    EncryptionAlgorithm = "AES-CBC 128",
                    MinutesLifetime = 60,
                    KilobytesLifetime = 100000
                },
                new()
                {
                    Protocol = "ESP",
                    IntegrityAlgorithm = "SHA-1",
                    EncryptionAlgorithm = "3DES",
                    MinutesLifetime = 60,
                    KilobytesLifetime = 100000
                }
            ];
        }

        if (defaults.AdvancedFirstAuthMethods.Count == 0 && defaults.AdvancedSecondAuthMethods.Count == 0)
        {
            defaults.AdvancedFirstAuthMethods =
            [
                new()
                {
                    Kind = "ComputerKerberos",
                    Method = LocalizedStrings.WF_AuthMethod_ComputerKerberos,
                    Details = LocalizedStrings.WF_AuthDetails_KerberosAuthentication
                },
                new()
                {
                    Kind = "ComputerNtlm",
                    Method = LocalizedStrings.WF_AuthMethod_ComputerNtlm,
                    Details = LocalizedStrings.WF_AuthDetails_NtlmAuthentication
                }
            ];

            defaults.AdvancedSecondAuthMethods =
            [
                new()
                {
                    Kind = "UserKerberos",
                    Method = LocalizedStrings.WF_AuthMethod_UserKerberos,
                    Details = LocalizedStrings.WF_AuthDetails_KerberosAuthentication
                },
                new()
                {
                    Kind = "UserNtlm",
                    Method = LocalizedStrings.WF_AuthMethod_UserNtlm,
                    Details = LocalizedStrings.WF_AuthDetails_NtlmAuthentication
                },
                new()
                {
                    Kind = "Anonymous",
                    Method = LocalizedStrings.WF_AuthMethod_Anonymous,
                    Details = LocalizedStrings.WF_AuthDetails_AnonymousAuthentication
                }
            ];
        }
    }

    private void CustomizeIpsecDefaultsDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        string? validationError = GetValidationErrorMessage();
        if (validationError is not null)
        {
            args.Cancel = true;
            ValidationMessageTextBlock.Text = validationError;
            ValidationMessageTextBlock.Visibility = Visibility.Visible;
            return;
        }

        Defaults.KeyExchangeMode = KeyExchangeAdvancedRadioButton.IsChecked == true ? "Advanced" : "Default";
        Defaults.DataProtectionMode = DataProtectionAdvancedRadioButton.IsChecked == true ? "Advanced" : "Default";
        Defaults.AuthenticationMethodMode =
            AuthenticationAdvancedRadioButton.IsChecked == true ? "Advanced" :
            AuthenticationComputerAndUserRadioButton.IsChecked == true ? "Computer and user" :
            AuthenticationComputerRadioButton.IsChecked == true ? "Computer" :
            AuthenticationUserRadioButton.IsChecked == true ? "User" :
            "Default";

        if (!string.Equals(Defaults.KeyExchangeMode, "Advanced", StringComparison.OrdinalIgnoreCase))
        {
            Defaults.AdvancedMainModeSecurityMethods.Clear();
        }

        if (!string.Equals(Defaults.DataProtectionMode, "Advanced", StringComparison.OrdinalIgnoreCase))
        {
            Defaults.AdvancedIntegrityAlgorithms.Clear();
            Defaults.AdvancedIntegrityEncryptionAlgorithms.Clear();
        }

        if (!string.Equals(Defaults.AuthenticationMethodMode, "Advanced", StringComparison.OrdinalIgnoreCase))
        {
            Defaults.AdvancedFirstAuthMethods.Clear();
            Defaults.AdvancedSecondAuthMethods.Clear();
            Defaults.IsAdvancedFirstAuthOptional = false;
            Defaults.IsAdvancedSecondAuthOptional = false;
        }
    }

    private string? GetValidationErrorMessage()
    {
        if (KeyExchangeAdvancedRadioButton.IsChecked == true && !_keyExchangeCustomized)
        {
            return LocalizedStrings.WF_IpsecDefaults_KeyExchangeRequired;
        }

        if (DataProtectionAdvancedRadioButton.IsChecked == true && !_dataProtectionCustomized)
        {
            return LocalizedStrings.WF_IpsecDefaults_DataProtectionRequired;
        }

        if (AuthenticationAdvancedRadioButton.IsChecked == true && !_authenticationCustomized)
        {
            return LocalizedStrings.WF_IpsecDefaults_AuthenticationRequired;
        }

        return null;
    }

    private void ResetValidationMessage()
    {
        ValidationMessageTextBlock.Visibility = Visibility.Collapsed;
        ValidationMessageTextBlock.Text = string.Empty;
    }

    private void OnThemeChanged(ElementTheme theme)
    {
        RequestedTheme = theme;
    }

    private void CustomizeIpsecDefaultsDialog_Unloaded(object sender, RoutedEventArgs e)
    {
        App.ThemeChanged -= OnThemeChanged;
    }
}
