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
        StoreManagementToggle.IsOn = settings.EnableMyStoreManagement;
        TemplateCheckToggle.IsOn = settings.EnableTemplateCheck;
        ExpirationNotificationsToggle.IsOn = settings.EnableExpirationNotifications;
        ExpirationPercentNumberBox.Value = settings.ExpirationNotificationPercent;
        AdditionalStoresTextBox.Text = settings.AdditionalExpirationStores;
        UpdateEnabledOptionsVisibility();
        UpdateExpirationControlsState();
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
            EnableMyStoreManagement = StoreManagementToggle.IsOn,
            EnableTemplateCheck = TemplateCheckToggle.IsOn,
            EnableExpirationNotifications = ExpirationNotificationsToggle.IsOn,
            ExpirationNotificationPercent = double.IsNaN(ExpirationPercentNumberBox.Value)
                ? 10
                : (int)Math.Clamp(ExpirationPercentNumberBox.Value, 1, 99),
            AdditionalExpirationStores = AdditionalStoresTextBox.Text.Trim()
        };
    }

    private void ConfigurationModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateEnabledOptionsVisibility();
    }

    private void ExpirationNotificationsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        UpdateExpirationControlsState();
    }

    private void UpdateEnabledOptionsVisibility()
    {
        EnabledOptionsPanel.Visibility = ConfigurationModelComboBox.SelectedIndex == 1
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void UpdateExpirationControlsState()
    {
        if (ExpirationPercentNumberBox is null || AdditionalStoresTextBox is null)
        {
            return;
        }

        bool notificationsEnabled = ExpirationNotificationsToggle.IsOn;
        ExpirationPercentNumberBox.IsEnabled = notificationsEnabled;
        AdditionalStoresTextBox.IsEnabled = notificationsEnabled;
        ExpirationExpander.IsExpanded = notificationsEnabled;
    }
}
