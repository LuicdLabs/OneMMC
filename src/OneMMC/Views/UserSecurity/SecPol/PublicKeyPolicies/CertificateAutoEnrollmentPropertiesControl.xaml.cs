using OneMMC.Core.Features.UserSecurity.Models.SecPol.PublicKeyPolicies;
using OneMMC.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OneMMC.Views;

/// <summary>
/// Edits local machine certificate auto-enrollment policy settings.
/// </summary>
public sealed partial class CertificateAutoEnrollmentPropertiesControl : UserControl
{
    /// <summary>
    /// Gets localized strings for XAML binding.
    /// </summary>
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    /// <summary>
    /// Initializes the auto-enrollment properties editor.
    /// </summary>
    /// <param name="settings">The settings to edit.</param>
    public CertificateAutoEnrollmentPropertiesControl(CertificateAutoEnrollmentSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        InitializeComponent();
        ConfigurationModelComboBox.SelectedIndex = settings.State switch
        {
            CertificateAutoEnrollmentPolicyState.Enabled => 1,
            CertificateAutoEnrollmentPolicyState.Disabled => 2,
            _ => 0
        };
        StoreManagementCheckBox.IsChecked = settings.EnableMyStoreManagement;
        TemplateCheckCheckBox.IsChecked = settings.EnableTemplateCheck;
        UpdateEnabledOptionsVisibility();
    }

    /// <summary>
    /// Gets the currently selected auto-enrollment settings.
    /// </summary>
    public CertificateAutoEnrollmentSettings GetSettings()
    {
        return new CertificateAutoEnrollmentSettings
        {
            State = ConfigurationModelComboBox.SelectedIndex switch
            {
                1 => CertificateAutoEnrollmentPolicyState.Enabled,
                2 => CertificateAutoEnrollmentPolicyState.Disabled,
                _ => CertificateAutoEnrollmentPolicyState.NotConfigured
            },
            EnableMyStoreManagement = StoreManagementCheckBox.IsChecked == true,
            EnableTemplateCheck = TemplateCheckCheckBox.IsChecked == true
        };
    }

    private void ConfigurationModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateEnabledOptionsVisibility();
    }

    private void UpdateEnabledOptionsVisibility()
    {
        EnabledOptionsPanel.Visibility = ConfigurationModelComboBox.SelectedIndex == 1
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
}
