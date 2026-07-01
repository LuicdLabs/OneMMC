using System;
using System.Threading.Tasks;
using OneMMC.Helpers;
using OneMMC.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OneMMC.Views.Dialogs.ConnectionSecurity;

public sealed partial class CustomizeAllowIfSecureDialog : UserControl
{
    public const int AuthenticateNone = 0;
    public const int AuthenticateNoEncapsulation = 1;
    public const int AuthenticateWithIntegrity = 2;
    public const int AuthenticateAndNegotiateEncryption = 3;
    public const int AuthenticateAndEncrypt = 4;

    private readonly Action<ElementTheme> _themeChangedHandler;

    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    public bool AllowAuthenticated => AllowAuthenticatedRadio.IsChecked == true;
    public bool RequireEncryption => RequireEncryptionRadio.IsChecked == true;
    public bool AllowNullEncapsulation => AllowNullEncapRadio.IsChecked == true;
    public bool OverrideBlockRules => OverrideBlockRulesCheckBox.IsChecked == true;
    public bool AllowNegotiateEncryption => NegotiateEncryptionCheckBox.IsChecked == true;

    public int SelectedSecureFlags
    {
        get
        {
            if (RequireEncryption)
            {
                return AllowNegotiateEncryption
                    ? AuthenticateAndNegotiateEncryption
                    : AuthenticateAndEncrypt;
            }

            if (AllowNullEncapsulation)
            {
                return AuthenticateNoEncapsulation;
            }

            // Default Allow If Secure behavior in WF.msc is authentication + integrity.
            return AuthenticateWithIntegrity;
        }
    }

    public CustomizeAllowIfSecureDialog()
    {
        InitializeComponent();
        RequestedTheme = App.CurrentTheme;
        _themeChangedHandler = theme => RequestedTheme = theme;
        Loaded += CustomizeAllowIfSecureDialog_Loaded;
        Unloaded += CustomizeAllowIfSecureDialog_Unloaded;

        OverrideBlockRulesDescriptionText.Text = LocalizedStrings.WF_AllowIfSecure_OverrideBlockRules_Desc;

        RequireEncryptionRadio.Checked += (_, _) => UpdateNegotiateEncryptionState();
        RequireEncryptionRadio.Unchecked += (_, _) => UpdateNegotiateEncryptionState();
    }

    public void ConfigureEncryptionNegotiationOption(bool showNegotiationOption)
    {
        NegotiateEncryptionCheckBox.Visibility = showNegotiationOption
            ? Visibility.Visible
            : Visibility.Collapsed;
        NegotiateEncryptionDescriptionText.Visibility = showNegotiationOption
            ? Visibility.Visible
            : Visibility.Collapsed;

        OverrideBlockRulesDescriptionText.Text = showNegotiationOption
            ? LocalizedStrings.WF_AllowIfSecure_OverrideBlockRules_Desc_Inbound
            : LocalizedStrings.WF_AllowIfSecure_OverrideBlockRules_Desc;

        if (!showNegotiationOption)
        {
            NegotiateEncryptionCheckBox.IsChecked = false;
        }

        UpdateNegotiateEncryptionState();
    }

    public Task<WindowDialogResult> ShowDialogAsync(XamlRoot ownerXamlRoot)
    {
        var modalWindow = new ModalDialogWindow(new ModalDialogOptions
        {
            Title = LocalizedStrings.WF_Action_CustomizeAllowSecure,
            Content = this,
            OwnerXamlRoot = ownerXamlRoot,
            RequestedTheme = App.CurrentTheme,
            ThemeChangeSubscribe = h => App.ThemeChanged += h,
            ThemeChangeUnsubscribe = h => App.ThemeChanged -= h,
            PrimaryButtonText = LocalizedStrings.Common_OKButton,
            SecondaryButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = WindowDialogResult.Primary,
            Width = 550,
            Height = 700
        });

        return modalWindow.ShowDialogAsync();
    }

    private void CustomizeAllowIfSecureDialog_Loaded(object sender, RoutedEventArgs e)
    {
        App.ThemeChanged -= _themeChangedHandler;
        App.ThemeChanged += _themeChangedHandler;
    }

    private void CustomizeAllowIfSecureDialog_Unloaded(object sender, RoutedEventArgs e)
    {
        App.ThemeChanged -= _themeChangedHandler;
    }

    public void ApplySelection(int secureFlags, bool overrideBlockRules)
    {
        switch (secureFlags)
        {
            case AuthenticateNoEncapsulation:
                AllowNullEncapRadio.IsChecked = true;
                break;
            case AuthenticateAndNegotiateEncryption:
            case AuthenticateAndEncrypt:
                RequireEncryptionRadio.IsChecked = true;
                NegotiateEncryptionCheckBox.IsChecked = secureFlags == AuthenticateAndNegotiateEncryption;
                break;
            default:
                AllowAuthenticatedRadio.IsChecked = true;
                break;
        }

        OverrideBlockRulesCheckBox.IsChecked = overrideBlockRules;
        UpdateNegotiateEncryptionState();
    }

    private void UpdateNegotiateEncryptionState()
    {
        bool enabled = RequireEncryptionRadio.IsChecked == true;
        NegotiateEncryptionCheckBox.IsEnabled = enabled;
        NegotiateEncryptionDescriptionText.Opacity = enabled ? 1.0 : 0.65;
        if (!enabled)
        {
            NegotiateEncryptionCheckBox.IsChecked = false;
        }
    }
}
