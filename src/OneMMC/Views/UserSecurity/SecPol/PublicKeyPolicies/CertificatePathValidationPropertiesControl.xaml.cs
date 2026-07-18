using System.Collections.ObjectModel;
using OneMMC.Core.Features.UserSecurity.Models.SecPol.PublicKeyPolicies;
using OneMMC.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OneMMC.Views;

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

    // The peer-trust purpose editor is created in code-behind rather than XAML. Authoring this small
    // TextBox/Button/ListView cluster in XAML alongside the rest of the compiled bindings on this page
    // triggers an internal crash in the Windows App SDK 2.2.1 XAML markup compiler; building it here
    // keeps the markup compiler stable while preserving identical behavior.
    private readonly TextBox _customPurposeTextBox = new();
    private readonly Button _addPurposeButton = new();
    private readonly Button _deletePurposeButton = new();
    private readonly ListView _purposeListView = new();

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
        BuildPurposesEditor();
        ApplySettings(settings);
        PathValidationSelectorBar.SelectedItem = StoresTabItem;
        UpdateSelectedPanel();
        UpdateEnabledStates();
    }

    private void BuildPurposesEditor()
    {
        _customPurposeTextBox.PlaceholderText = LocalizedStrings.PKP_PathValidation_CustomOidPlaceholder;

        _addPurposeButton.Content = LocalizedStrings.PKP_PathValidation_AddPurpose;
        _addPurposeButton.Click += AddPurposeButton_Click;
        _deletePurposeButton.Content = LocalizedStrings.PKP_PathValidation_DeletePurpose;
        _deletePurposeButton.Click += DeletePurposeButton_Click;

        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttonRow.Children.Add(_addPurposeButton);
        buttonRow.Children.Add(_deletePurposeButton);

        _purposeListView.MaxHeight = 150;
        _purposeListView.SelectionMode = ListViewSelectionMode.Single;
        _purposeListView.ItemsSource = PeerTrustPurposeOids;

        PurposesEditorPanel.Children.Add(_customPurposeTextBox);
        PurposesEditorPanel.Children.Add(buttonRow);
        PurposesEditorPanel.Children.Add(_purposeListView);
    }

    /// <summary>
    /// Gets the currently selected path validation settings.
    /// </summary>
    /// <returns>The edited path validation settings.</returns>
    public CertificatePathValidationSettings GetSettings()
    {
        var settings = new CertificatePathValidationSettings
        {
            StoresDefined = StoresDefinedToggle.IsOn,
            AllowUserTrustedRoots = AllowUserTrustedRootsToggle.IsOn,
            AllowPeerTrustCertificates = AllowPeerTrustToggle.IsOn,
            OnlyTrustEnterpriseRoots = EnterpriseRootRadioButton.IsChecked == true,
            RequireUpnNameConstraints = UpnConstraintsToggle.IsOn,
            TrustedPublishersDefined = TrustedPublishersDefinedToggle.IsOn,
            TrustedPublisherScope = GetTrustedPublisherScope(),
            CheckPublisherRevocation = CheckPublisherRevocationToggle.IsOn,
            CheckTimestampRevocation = CheckTimestampRevocationToggle.IsOn,
            NetworkRetrievalDefined = NetworkRetrievalDefinedToggle.IsOn,
            AutomaticallyUpdateRootCertificates = RootAutoUpdateToggle.IsOn,
            UrlRetrievalTimeoutSeconds = ReadNumber(UrlRetrievalTimeoutNumberBox, 15),
            PathValidationRetrievalTimeoutSeconds = ReadNumber(PathRetrievalTimeoutNumberBox, 20),
            AllowAiaRetrieval = AllowAiaRetrievalToggle.IsOn,
            CrossCertificateDownloadIntervalHours = ReadNumber(CrossCertIntervalNumberBox, 168),
            RevocationDefined = RevocationDefinedToggle.IsOn,
            PreferCrlBeforeOcsp = PreferCrlBeforeOcspToggle.IsOn,
            CachedOcspResponseThreshold = ReadNumber(CachedOcspThresholdNumberBox, 50),
            ExtendRevocationFreshnessLifetime = ExtendRevocationFreshnessToggle.IsOn,
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
        StoresDefinedToggle.IsOn = settings.StoresDefined;
        AllowUserTrustedRootsToggle.IsOn = settings.AllowUserTrustedRoots;
        AllowPeerTrustToggle.IsOn = settings.AllowPeerTrustCertificates;
        ThirdPartyAndEnterpriseRootRadioButton.IsChecked = !settings.OnlyTrustEnterpriseRoots;
        EnterpriseRootRadioButton.IsChecked = settings.OnlyTrustEnterpriseRoots;
        UpnConstraintsToggle.IsOn = settings.RequireUpnNameConstraints;

        TrustedPublishersDefinedToggle.IsOn = settings.TrustedPublishersDefined;
        PublisherScopeEndUsersRadioButton.IsChecked = settings.TrustedPublisherScope == PublicKeyTrustedPublisherScope.EndUsers;
        PublisherScopeLocalAdministratorsRadioButton.IsChecked = settings.TrustedPublisherScope == PublicKeyTrustedPublisherScope.LocalAdministrators;
        PublisherScopeEnterpriseAdministratorsRadioButton.IsChecked = settings.TrustedPublisherScope == PublicKeyTrustedPublisherScope.EnterpriseAdministrators;
        CheckPublisherRevocationToggle.IsOn = settings.CheckPublisherRevocation;
        CheckTimestampRevocationToggle.IsOn = settings.CheckTimestampRevocation;

        NetworkRetrievalDefinedToggle.IsOn = settings.NetworkRetrievalDefined;
        RootAutoUpdateToggle.IsOn = settings.AutomaticallyUpdateRootCertificates;
        UrlRetrievalTimeoutNumberBox.Value = settings.UrlRetrievalTimeoutSeconds;
        PathRetrievalTimeoutNumberBox.Value = settings.PathValidationRetrievalTimeoutSeconds;
        AllowAiaRetrievalToggle.IsOn = settings.AllowAiaRetrieval;
        CrossCertIntervalNumberBox.Value = settings.CrossCertificateDownloadIntervalHours;

        RevocationDefinedToggle.IsOn = settings.RevocationDefined;
        PreferCrlBeforeOcspToggle.IsOn = settings.PreferCrlBeforeOcsp;
        CachedOcspThresholdNumberBox.Value = settings.CachedOcspResponseThreshold;
        ExtendRevocationFreshnessToggle.IsOn = settings.ExtendRevocationFreshnessLifetime;
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

    private void StoresDefinedToggle_Toggled(object sender, RoutedEventArgs e)
    {
        UpdateEnabledStates();
    }

    private void TrustedPublishersDefinedToggle_Toggled(object sender, RoutedEventArgs e)
    {
        UpdateEnabledStates();
    }

    private void NetworkRetrievalDefinedToggle_Toggled(object sender, RoutedEventArgs e)
    {
        UpdateEnabledStates();
    }

    private void RevocationDefinedToggle_Toggled(object sender, RoutedEventArgs e)
    {
        UpdateEnabledStates();
    }

    private void AllowPeerTrustToggle_Toggled(object sender, RoutedEventArgs e)
    {
        UpdateEnabledStates();
    }

    private void PreferCrlBeforeOcspToggle_Toggled(object sender, RoutedEventArgs e)
    {
        UpdateEnabledStates();
    }

    private void ExtendRevocationFreshnessToggle_Toggled(object sender, RoutedEventArgs e)
    {
        UpdateEnabledStates();
    }

    private void AddPurposeButton_Click(object sender, RoutedEventArgs e)
    {
        string value = _customPurposeTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(value)
            || PeerTrustPurposeOids.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        PeerTrustPurposeOids.Add(value);
        _customPurposeTextBox.Text = string.Empty;
    }

    private void DeletePurposeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_purposeListView.SelectedItem is string selected)
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
        bool storesDefined = StoresDefinedToggle.IsOn;
        bool peerTrustEnabled = storesDefined && AllowPeerTrustToggle.IsOn;
        SetEnabled(
            storesDefined,
            AllowUserTrustedRootsToggle,
            AllowPeerTrustToggle,
            RootStoresModeRadioButtons,
            UpnConstraintsToggle);
        SetEnabled(peerTrustEnabled, _customPurposeTextBox, _addPurposeButton, _deletePurposeButton, _purposeListView);

        bool trustedPublishersDefined = TrustedPublishersDefinedToggle.IsOn;
        SetEnabled(
            trustedPublishersDefined,
            PublisherScopeRadioButtons,
            CheckPublisherRevocationToggle,
            CheckTimestampRevocationToggle);

        bool networkDefined = NetworkRetrievalDefinedToggle.IsOn;
        SetEnabled(
            networkDefined,
            RootAutoUpdateToggle,
            UrlRetrievalTimeoutNumberBox,
            PathRetrievalTimeoutNumberBox,
            AllowAiaRetrievalToggle,
            CrossCertIntervalNumberBox);

        bool revocationDefined = RevocationDefinedToggle.IsOn;
        bool preferCrl = revocationDefined && PreferCrlBeforeOcspToggle.IsOn;
        bool extendLifetime = revocationDefined && ExtendRevocationFreshnessToggle.IsOn;
        SetEnabled(revocationDefined, PreferCrlBeforeOcspToggle, ExtendRevocationFreshnessToggle);
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
