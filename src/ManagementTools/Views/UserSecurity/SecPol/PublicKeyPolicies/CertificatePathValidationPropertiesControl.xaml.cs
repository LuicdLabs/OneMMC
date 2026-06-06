using System.Collections.ObjectModel;
using ManagementTools.Core.Features.UserSecurity.Models.SecPol.PublicKeyPolicies;
using ManagementTools.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ManagementTools.Views;

/// <summary>
/// Edits local machine certificate path validation policy settings.
/// </summary>
public sealed partial class CertificatePathValidationPropertiesControl : UserControl
{
    /// <summary>
    /// Gets localized strings for XAML binding.
    /// </summary>
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    /// <summary>
    /// Gets peer trust purpose OIDs shown in the Stores tab.
    /// </summary>
    public ObservableCollection<string> PeerTrustPurposeOids { get; } = [];

    /// <summary>
    /// Initializes the certificate path validation properties editor.
    /// </summary>
    /// <param name="settings">The settings to edit.</param>
    public CertificatePathValidationPropertiesControl(CertificatePathValidationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        foreach (string oid in settings.PeerTrustPurposeOids)
        {
            PeerTrustPurposeOids.Add(oid);
        }

        InitializeComponent();
        ApplySettings(settings);
        PathValidationSelectorBar.SelectedItem = StoresTabItem;
        UpdateSelectedPanel();
        UpdateEnabledStates();
    }

    /// <summary>
    /// Gets the currently selected path validation settings.
    /// </summary>
    /// <returns>The edited path validation settings.</returns>
    public CertificatePathValidationSettings GetSettings()
    {
        var settings = new CertificatePathValidationSettings
        {
            StoresDefined = StoresDefinedCheckBox.IsChecked == true,
            AllowUserTrustedRoots = AllowUserTrustedRootsCheckBox.IsChecked == true,
            AllowPeerTrustCertificates = AllowPeerTrustCheckBox.IsChecked == true,
            OnlyTrustEnterpriseRoots = EnterpriseRootRadioButton.IsChecked == true,
            RequireUpnNameConstraints = UpnConstraintsCheckBox.IsChecked == true,
            TrustedPublishersDefined = TrustedPublishersDefinedCheckBox.IsChecked == true,
            TrustedPublisherScope = GetTrustedPublisherScope(),
            CheckPublisherRevocation = CheckPublisherRevocationCheckBox.IsChecked == true,
            CheckTimestampRevocation = CheckTimestampRevocationCheckBox.IsChecked == true,
            NetworkRetrievalDefined = NetworkRetrievalDefinedCheckBox.IsChecked == true,
            AutomaticallyUpdateRootCertificates = RootAutoUpdateCheckBox.IsChecked == true,
            UrlRetrievalTimeoutSeconds = ReadNumber(UrlRetrievalTimeoutNumberBox, 15),
            PathValidationRetrievalTimeoutSeconds = ReadNumber(PathRetrievalTimeoutNumberBox, 20),
            AllowAiaRetrieval = AllowAiaRetrievalCheckBox.IsChecked == true,
            CrossCertificateDownloadIntervalHours = ReadNumber(CrossCertIntervalNumberBox, 168),
            RevocationDefined = RevocationDefinedCheckBox.IsChecked == true,
            PreferCrlBeforeOcsp = PreferCrlBeforeOcspCheckBox.IsChecked == true,
            CachedOcspResponseThreshold = ReadNumber(CachedOcspThresholdNumberBox, 50),
            ExtendRevocationFreshnessLifetime = ExtendRevocationFreshnessCheckBox.IsChecked == true,
            RevocationFreshnessExtensionHours = ReadNumber(RevocationExtensionHoursNumberBox, 12)
        };

        foreach (string oid in PeerTrustPurposeOids.Where(static oid => !string.IsNullOrWhiteSpace(oid)))
        {
            settings.PeerTrustPurposeOids.Add(oid.Trim());
        }

        return settings;
    }

    private void ApplySettings(CertificatePathValidationSettings settings)
    {
        StoresDefinedCheckBox.IsChecked = settings.StoresDefined;
        AllowUserTrustedRootsCheckBox.IsChecked = settings.AllowUserTrustedRoots;
        AllowPeerTrustCheckBox.IsChecked = settings.AllowPeerTrustCertificates;
        ThirdPartyAndEnterpriseRootRadioButton.IsChecked = !settings.OnlyTrustEnterpriseRoots;
        EnterpriseRootRadioButton.IsChecked = settings.OnlyTrustEnterpriseRoots;
        UpnConstraintsCheckBox.IsChecked = settings.RequireUpnNameConstraints;

        TrustedPublishersDefinedCheckBox.IsChecked = settings.TrustedPublishersDefined;
        PublisherScopeEndUsersRadioButton.IsChecked = settings.TrustedPublisherScope == PublicKeyTrustedPublisherScope.EndUsers;
        PublisherScopeLocalAdministratorsRadioButton.IsChecked = settings.TrustedPublisherScope == PublicKeyTrustedPublisherScope.LocalAdministrators;
        PublisherScopeEnterpriseAdministratorsRadioButton.IsChecked = settings.TrustedPublisherScope == PublicKeyTrustedPublisherScope.EnterpriseAdministrators;
        CheckPublisherRevocationCheckBox.IsChecked = settings.CheckPublisherRevocation;
        CheckTimestampRevocationCheckBox.IsChecked = settings.CheckTimestampRevocation;

        NetworkRetrievalDefinedCheckBox.IsChecked = settings.NetworkRetrievalDefined;
        RootAutoUpdateCheckBox.IsChecked = settings.AutomaticallyUpdateRootCertificates;
        UrlRetrievalTimeoutNumberBox.Value = settings.UrlRetrievalTimeoutSeconds;
        PathRetrievalTimeoutNumberBox.Value = settings.PathValidationRetrievalTimeoutSeconds;
        AllowAiaRetrievalCheckBox.IsChecked = settings.AllowAiaRetrieval;
        CrossCertIntervalNumberBox.Value = settings.CrossCertificateDownloadIntervalHours;

        RevocationDefinedCheckBox.IsChecked = settings.RevocationDefined;
        PreferCrlBeforeOcspCheckBox.IsChecked = settings.PreferCrlBeforeOcsp;
        CachedOcspThresholdNumberBox.Value = settings.CachedOcspResponseThreshold;
        ExtendRevocationFreshnessCheckBox.IsChecked = settings.ExtendRevocationFreshnessLifetime;
        RevocationExtensionHoursNumberBox.Value = settings.RevocationFreshnessExtensionHours;
    }

    private PublicKeyTrustedPublisherScope GetTrustedPublisherScope()
    {
        if (PublisherScopeEnterpriseAdministratorsRadioButton.IsChecked == true)
        {
            return PublicKeyTrustedPublisherScope.EnterpriseAdministrators;
        }

        if (PublisherScopeLocalAdministratorsRadioButton.IsChecked == true)
        {
            return PublicKeyTrustedPublisherScope.LocalAdministrators;
        }

        return PublicKeyTrustedPublisherScope.EndUsers;
    }

    private void PathValidationSelectorBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        UpdateSelectedPanel();
    }

    private void StoresDefinedCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateEnabledStates();
    }

    private void TrustedPublishersDefinedCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateEnabledStates();
    }

    private void NetworkRetrievalDefinedCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateEnabledStates();
    }

    private void RevocationDefinedCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateEnabledStates();
    }

    private void AllowPeerTrustCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateEnabledStates();
    }

    private void PreferCrlBeforeOcspCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateEnabledStates();
    }

    private void ExtendRevocationFreshnessCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateEnabledStates();
    }

    private void SelectPurposesButton_Click(object sender, RoutedEventArgs e)
    {
        PurposesEditorPanel.Visibility = PurposesEditorPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void AddPurposeButton_Click(object sender, RoutedEventArgs e)
    {
        string value = CustomPurposeTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(value)
            || PeerTrustPurposeOids.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        PeerTrustPurposeOids.Add(value);
        CustomPurposeTextBox.Text = string.Empty;
    }

    private void DeletePurposeButton_Click(object sender, RoutedEventArgs e)
    {
        if (PurposeListView.SelectedItem is string selected)
        {
            PeerTrustPurposeOids.Remove(selected);
        }
    }

    private void UpdateSelectedPanel()
    {
        string tag = (PathValidationSelectorBar.SelectedItem as SelectorBarItem)?.Tag as string ?? "Stores";
        StoresPanel.Visibility = tag == "Stores" ? Visibility.Visible : Visibility.Collapsed;
        TrustedPublishersPanel.Visibility = tag == "TrustedPublishers" ? Visibility.Visible : Visibility.Collapsed;
        NetworkRetrievalPanel.Visibility = tag == "NetworkRetrieval" ? Visibility.Visible : Visibility.Collapsed;
        RevocationPanel.Visibility = tag == "Revocation" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateEnabledStates()
    {
        bool storesDefined = StoresDefinedCheckBox.IsChecked == true;
        bool peerTrustEnabled = storesDefined && AllowPeerTrustCheckBox.IsChecked == true;
        SetEnabled(
            storesDefined,
            AllowUserTrustedRootsCheckBox,
            AllowPeerTrustCheckBox,
            ThirdPartyAndEnterpriseRootRadioButton,
            EnterpriseRootRadioButton,
            UpnConstraintsCheckBox);
        SetEnabled(peerTrustEnabled, SelectPurposesButton, CustomPurposeTextBox, AddPurposeButton, DeletePurposeButton, PurposeListView);
        if (!peerTrustEnabled)
        {
            PurposesEditorPanel.Visibility = Visibility.Collapsed;
        }

        bool trustedPublishersDefined = TrustedPublishersDefinedCheckBox.IsChecked == true;
        SetEnabled(
            trustedPublishersDefined,
            PublisherScopeEndUsersRadioButton,
            PublisherScopeLocalAdministratorsRadioButton,
            PublisherScopeEnterpriseAdministratorsRadioButton,
            CheckPublisherRevocationCheckBox,
            CheckTimestampRevocationCheckBox);

        bool networkDefined = NetworkRetrievalDefinedCheckBox.IsChecked == true;
        SetEnabled(
            networkDefined,
            RootAutoUpdateCheckBox,
            UrlRetrievalTimeoutNumberBox,
            PathRetrievalTimeoutNumberBox,
            AllowAiaRetrievalCheckBox,
            CrossCertIntervalNumberBox);

        bool revocationDefined = RevocationDefinedCheckBox.IsChecked == true;
        bool preferCrl = revocationDefined && PreferCrlBeforeOcspCheckBox.IsChecked == true;
        bool extendLifetime = revocationDefined && ExtendRevocationFreshnessCheckBox.IsChecked == true;
        SetEnabled(revocationDefined, PreferCrlBeforeOcspCheckBox, ExtendRevocationFreshnessCheckBox);
        SetEnabled(preferCrl, CachedOcspThresholdNumberBox);
        SetEnabled(extendLifetime, RevocationExtensionHoursNumberBox);
    }

    private static void SetEnabled(bool isEnabled, params Control[] controls)
    {
        foreach (Control control in controls)
        {
            control.IsEnabled = isEnabled;
        }
    }

    private static int ReadNumber(NumberBox numberBox, int fallback)
    {
        return double.IsNaN(numberBox.Value)
            ? fallback
            : Math.Clamp((int)numberBox.Value, 0, 9999);
    }
}
