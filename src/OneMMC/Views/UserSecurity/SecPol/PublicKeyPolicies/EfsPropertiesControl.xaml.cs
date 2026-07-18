using OneMMC.Core.Features.UserSecurity.Models.SecPol.PublicKeyPolicies;
using OneMMC.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OneMMC.Views;

/// <summary>
/// Edits local machine Encrypting File System policy settings.
/// </summary>
public sealed partial class EfsPropertiesControl : UserControl
{
    private const int DefaultRsaKeyLength = 2048;
    private const int DefaultEccKeyLength = 256;
    private const int DefaultCacheTimeoutMinutes = 480;

    /// <summary>
    /// Gets localized strings for XAML binding.
    /// </summary>
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    /// <summary>
    /// Initializes the EFS properties editor.
    /// </summary>
    /// <param name="settings">The settings to edit.</param>
    public EfsPropertiesControl(EfsPolicySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        InitializeComponent();
        InitializeKeySizeComboBoxes();
        ApplySettings(settings);
        EfsSelectorBar.SelectedItem = GeneralTabItem;
        UpdateSelectedPanel();
        UpdateEnabledStates();
    }

    /// <summary>
    /// Gets the currently selected EFS settings.
    /// </summary>
    /// <returns>The edited EFS settings.</returns>
    public EfsPolicySettings GetSettings()
    {
        return new EfsPolicySettings
        {
            FileEncryptionState = GetFileEncryptionState(),
            EllipticCurveMode = GetEllipticCurveMode(),
            EncryptDocumentsFolder = EncryptDocumentsFolderToggle.IsOn,
            RequireSmartCard = RequireSmartCardToggle.IsOn,
            CreateCachingCapableUserKey = CreateSmartCardUserKeyToggle.IsOn,
            DisplayKeyBackupNotifications = KeyBackupNotificationsToggle.IsOn,
            TemplateName = TemplateNameTextBox.Text.Trim(),
            AllowSelfSignedCertificates = AllowSelfSignedToggle.IsOn,
            RsaKeyLength = ReadComboBoxValue(RsaKeySizeComboBox, DefaultRsaKeyLength),
            EccKeyLength = ReadComboBoxValue(EccKeySizeComboBox, DefaultEccKeyLength),
            ClearCacheOnTimeout = ClearCacheOnTimeoutToggle.IsOn,
            CacheTimeoutMinutes = ReadNumber(CacheTimeoutNumberBox, DefaultCacheTimeoutMinutes),
            ClearCacheOnLock = ClearCacheOnLockToggle.IsOn
        };
    }

    private void ApplySettings(EfsPolicySettings settings)
    {
        FileEncryptionNotDefinedRadioButton.IsChecked = settings.FileEncryptionState == EfsFileEncryptionState.NotDefined;
        FileEncryptionAllowRadioButton.IsChecked = settings.FileEncryptionState == EfsFileEncryptionState.Allow;
        FileEncryptionDontAllowRadioButton.IsChecked = settings.FileEncryptionState == EfsFileEncryptionState.DontAllow;

        EccAllowRadioButton.IsChecked = settings.EllipticCurveMode == EfsEllipticCurveMode.Allow;
        EccRequireRadioButton.IsChecked = settings.EllipticCurveMode == EfsEllipticCurveMode.Require;
        EccDontAllowRadioButton.IsChecked = settings.EllipticCurveMode == EfsEllipticCurveMode.DontAllow;

        EncryptDocumentsFolderToggle.IsOn = settings.EncryptDocumentsFolder;
        RequireSmartCardToggle.IsOn = settings.RequireSmartCard;
        CreateSmartCardUserKeyToggle.IsOn = settings.CreateCachingCapableUserKey;
        KeyBackupNotificationsToggle.IsOn = settings.DisplayKeyBackupNotifications;
        TemplateNameTextBox.Text = settings.TemplateName;
        AllowSelfSignedToggle.IsOn = settings.AllowSelfSignedCertificates;
        SelectComboBoxValue(RsaKeySizeComboBox, settings.RsaKeyLength);
        SelectComboBoxValue(EccKeySizeComboBox, settings.EccKeyLength);
        ClearCacheOnTimeoutToggle.IsOn = settings.ClearCacheOnTimeout;
        CacheTimeoutNumberBox.Value = settings.CacheTimeoutMinutes;
        ClearCacheOnLockToggle.IsOn = settings.ClearCacheOnLock;
    }

    private void InitializeKeySizeComboBoxes()
    {
        foreach (int keyLength in new[] { 1024, 2048, 4096, 8192, 16384 })
        {
            RsaKeySizeComboBox.Items.Add(CreateNumberItem(keyLength));
        }

        foreach (int keyLength in new[] { 256, 384, 521 })
        {
            EccKeySizeComboBox.Items.Add(CreateNumberItem(keyLength));
        }
    }

    private static ComboBoxItem CreateNumberItem(int value)
    {
        return new ComboBoxItem
        {
            Content = value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Tag = value
        };
    }

    private void EfsSelectorBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        UpdateSelectedPanel();
    }

    private void FileEncryptionRadioButton_Checked(object sender, RoutedEventArgs e)
    {
        UpdateEnabledStates();
    }

    private void ClearCacheOnTimeoutToggle_Toggled(object sender, RoutedEventArgs e)
    {
        UpdateEnabledStates();
    }

    private void UpdateSelectedPanel()
    {
        string tag = (EfsSelectorBar.SelectedItem as SelectorBarItem)?.Tag as string ?? "General";
        GeneralPanel.Visibility = tag == "General" ? Visibility.Visible : Visibility.Collapsed;
        CertificatesPanel.Visibility = tag == "Certificates" ? Visibility.Visible : Visibility.Collapsed;
        CachePanel.Visibility = tag == "Cache" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateEnabledStates()
    {
        bool fileEncryptionAllowed = FileEncryptionAllowRadioButton.IsChecked == true;
        SetEnabled(
            fileEncryptionAllowed,
            EccAllowRadioButton,
            EccRequireRadioButton,
            EccDontAllowRadioButton,
            EncryptDocumentsFolderToggle,
            RequireSmartCardToggle,
            CreateSmartCardUserKeyToggle,
            KeyBackupNotificationsToggle,
            TemplateNameTextBox,
            RsaKeySizeComboBox,
            EccKeySizeComboBox,
            AllowSelfSignedToggle,
            ClearCacheOnTimeoutToggle,
            ClearCacheOnLockToggle);
        SetEnabled(fileEncryptionAllowed && ClearCacheOnTimeoutToggle.IsOn, CacheTimeoutNumberBox);
    }

    private EfsFileEncryptionState GetFileEncryptionState()
    {
        if (FileEncryptionAllowRadioButton.IsChecked == true)
        {
            return EfsFileEncryptionState.Allow;
        }

        if (FileEncryptionDontAllowRadioButton.IsChecked == true)
        {
            return EfsFileEncryptionState.DontAllow;
        }

        return EfsFileEncryptionState.NotDefined;
    }

    private EfsEllipticCurveMode GetEllipticCurveMode()
    {
        if (EccRequireRadioButton.IsChecked == true)
        {
            return EfsEllipticCurveMode.Require;
        }

        if (EccDontAllowRadioButton.IsChecked == true)
        {
            return EfsEllipticCurveMode.DontAllow;
        }

        return EfsEllipticCurveMode.Allow;
    }

    private static void SelectComboBoxValue(ComboBox comboBox, int value)
    {
        foreach (object item in comboBox.Items)
        {
            if (item is ComboBoxItem { Tag: int itemValue } comboBoxItem && itemValue == value)
            {
                comboBox.SelectedItem = comboBoxItem;
                return;
            }
        }

        comboBox.SelectedIndex = 0;
    }

    private static int ReadComboBoxValue(ComboBox comboBox, int fallback)
    {
        return comboBox.SelectedItem is ComboBoxItem { Tag: int value }
            ? value
            : fallback;
    }

    private static int ReadNumber(NumberBox numberBox, int fallback)
    {
        return double.IsNaN(numberBox.Value)
            ? fallback
            : Math.Clamp((int)numberBox.Value, 5, 10080);
    }

    private static void SetEnabled(bool isEnabled, params Control[] controls)
    {
        foreach (Control control in controls)
        {
            control.IsEnabled = isEnabled;
        }
    }
}
