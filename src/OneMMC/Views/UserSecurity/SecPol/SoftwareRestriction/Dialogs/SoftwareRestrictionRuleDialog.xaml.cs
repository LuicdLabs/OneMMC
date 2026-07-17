using System;
using System.IO;
using OneMMC.Core.Abstractions.Services;
using OneMMC.Core.Features.UserSecurity.Models.SecPol.SoftwareRestriction;
using OneMMC.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

namespace OneMMC.Views;

/// <summary>
/// Editor dialog for a single Software Restriction Policies additional rule.
/// Creates path, hash, certificate, and network zone rules, and edits path, hash,
/// and network zone rules (certificate rules are view-only, matching secpol.msc).
/// </summary>
public sealed partial class SoftwareRestrictionRuleDialog : ContentDialog
{
    private const string AllFilesFilterPattern = "*.*";
    private const string PathRuleFilePickerSettingsIdentifier = "SoftwareRestrictionPathRuleFilePicker";
    private const string PathRuleFolderPickerSettingsIdentifier = "SoftwareRestrictionPathRuleFolderPicker";
    private const string HashFilePickerSettingsIdentifier = "SoftwareRestrictionHashRuleFilePicker";
    private const string CertificateRuleFilePickerSettingsIdentifier = "SoftwareRestrictionCertificateRuleFilePicker";

    /// <summary>Zone identifiers in the order the zone ComboBox items are declared in XAML.</summary>
    private static readonly string[] NetworkZoneIds = ["3", "0", "1", "4", "2"];
    private const string DefaultNetworkZoneId = "3";

    private readonly IFileDialogService _fileDialogService;
    private readonly SoftwareRestrictionRuleKind _kind;
    private readonly SoftwareRestrictionRule? _existingRule;
    private readonly SoftwareRestrictionSecurityLevel[] _securityLevelValues;
    private readonly string[] _zoneDescriptions;

    /// <summary>Gets the localized strings accessor used by compiled bindings.</summary>
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    /// <summary>
    /// Initializes a new instance of the <see cref="SoftwareRestrictionRuleDialog"/> class.
    /// </summary>
    /// <param name="kind">The rule kind to create or edit.</param>
    /// <param name="existingRule">The rule being edited, or <see langword="null"/> to create a new rule.</param>
    /// <param name="fileDialogService">The native file dialog service used by the browse buttons.</param>
    public SoftwareRestrictionRuleDialog(
        SoftwareRestrictionRuleKind kind,
        SoftwareRestrictionRule? existingRule,
        IFileDialogService fileDialogService)
    {
        _kind = kind;
        _existingRule = existingRule;
        _fileDialogService = fileDialogService;

        InitializeComponent();
        Title = LocalizedStrings.SRP_Dialog_EditRule_Title;

        _zoneDescriptions =
        [
            LocalizedStrings.SRP_NetworkZone_Internet_Description,
            LocalizedStrings.SRP_NetworkZone_LocalComputer_Description,
            LocalizedStrings.SRP_NetworkZone_LocalIntranet_Description,
            LocalizedStrings.SRP_NetworkZone_RestrictedSites_Description,
            LocalizedStrings.SRP_NetworkZone_TrustedSites_Description
        ];

        // Certificate rules are stored in the TrustedPublisher/Disallowed certificate stores, so
        // Basic User is not a valid level for them (matching secpol.msc).
        if (kind == SoftwareRestrictionRuleKind.Certificate)
        {
            _securityLevelValues =
            [
                SoftwareRestrictionSecurityLevel.Disallowed,
                SoftwareRestrictionSecurityLevel.Unrestricted
            ];
        }
        else
        {
            _securityLevelValues =
            [
                SoftwareRestrictionSecurityLevel.Disallowed,
                SoftwareRestrictionSecurityLevel.BasicUser,
                SoftwareRestrictionSecurityLevel.Unrestricted
            ];
        }

        foreach (SoftwareRestrictionSecurityLevel level in _securityLevelValues)
        {
            SecurityLevelComboBox.Items.Add(new ComboBoxItem
            {
                Content = SoftwareRestrictionRule.FormatSecurityLevel(level)
            });
        }

        ConfigureForKind();
        ApplyInitialValues();
        UpdatePrimaryButtonState();
    }

    /// <summary>
    /// Builds the rule described by the current dialog state. When editing, the storage identity
    /// of the original rule is preserved so the service updates it in place.
    /// </summary>
    /// <returns>The rule to save.</returns>
    public SoftwareRestrictionRule BuildRule()
    {
        SoftwareRestrictionRule rule = _existingRule is null
            ? new SoftwareRestrictionRule { Kind = _kind }
            : new SoftwareRestrictionRule
            {
                Id = _existingRule.Id,
                Kind = _existingRule.Kind,
                StoragePath = _existingRule.StoragePath
            };

        int levelIndex = Math.Clamp(SecurityLevelComboBox.SelectedIndex, 0, _securityLevelValues.Length - 1);
        rule.SecurityLevel = _securityLevelValues[levelIndex];
        rule.Description = DescriptionTextBox.Text.Trim();
        rule.Value = _kind switch
        {
            SoftwareRestrictionRuleKind.NetworkZone => NetworkZoneIds[Math.Clamp(ZoneComboBox.SelectedIndex, 0, NetworkZoneIds.Length - 1)],
            SoftwareRestrictionRuleKind.Path => PathTextBox.Text.Trim(),
            _ => FileTextBox.Text.Trim()
        };

        return rule;
    }

    private static nint OwnerWindowHandle
        => App.MainWindowInstance is null ? 0 : WindowNative.GetWindowHandle(App.MainWindowInstance);

    private void ConfigureForKind()
    {
        switch (_kind)
        {
            case SoftwareRestrictionRuleKind.Path:
                PathValuePanel.Visibility = Visibility.Visible;
                break;
            case SoftwareRestrictionRuleKind.NetworkZone:
                ZoneValuePanel.Visibility = Visibility.Visible;
                break;
            case SoftwareRestrictionRuleKind.Certificate:
                FileValuePanel.Visibility = Visibility.Visible;
                FileValueLabel.Text = LocalizedStrings.SRP_Field_CertificateFile;
                break;
            default:
                FileValuePanel.Visibility = Visibility.Visible;
                FileValueLabel.Text = LocalizedStrings.SRP_Field_HashFile;
                break;
        }
    }

    private void ApplyInitialValues()
    {
        SoftwareRestrictionSecurityLevel initialLevel = _existingRule?.SecurityLevel
            ?? SoftwareRestrictionSecurityLevel.Disallowed;
        int levelIndex = Array.IndexOf(_securityLevelValues, initialLevel);
        SecurityLevelComboBox.SelectedIndex = Math.Max(levelIndex, 0);
        DescriptionTextBox.Text = _existingRule?.Description ?? string.Empty;

        switch (_kind)
        {
            case SoftwareRestrictionRuleKind.Path:
                PathTextBox.Text = _existingRule?.Value ?? string.Empty;
                break;
            case SoftwareRestrictionRuleKind.NetworkZone:
                string zoneId = string.IsNullOrWhiteSpace(_existingRule?.Value) ? DefaultNetworkZoneId : _existingRule.Value;
                int zoneIndex = Array.IndexOf(NetworkZoneIds, zoneId);
                ZoneComboBox.SelectedIndex = Math.Max(zoneIndex, 0);
                break;
            default:
                FileTextBox.Text = _existingRule?.Value ?? string.Empty;

                // When editing a hash rule the box shows the stored hash; typing is locked, but the
                // browse button can still replace it with a new file path to re-derive the hash.
                FileTextBox.IsReadOnly = _existingRule is not null && _kind == SoftwareRestrictionRuleKind.Hash;
                break;
        }
    }

    private void UpdatePrimaryButtonState()
    {
        IsPrimaryButtonEnabled = _kind switch
        {
            SoftwareRestrictionRuleKind.NetworkZone => ZoneComboBox.SelectedIndex >= 0,
            SoftwareRestrictionRuleKind.Path => !string.IsNullOrWhiteSpace(PathTextBox.Text),
            _ => !string.IsNullOrWhiteSpace(FileTextBox.Text)
        };
    }

    private void ValueTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdatePrimaryButtonState();
    }

    private void ZoneComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int zoneIndex = ZoneComboBox.SelectedIndex;
        ZoneDescriptionText.Text = zoneIndex >= 0 && zoneIndex < _zoneDescriptions.Length
            ? _zoneDescriptions[zoneIndex]
            : string.Empty;
        UpdatePrimaryButtonState();
    }

    private async void BrowsePathFileButton_Click(object sender, RoutedEventArgs e)
    {
        string? selectedPath = await _fileDialogService.OpenFileAsync(
            OwnerWindowHandle,
            CreateAllFilesFilter(),
            LocalizedStrings.SRP_Dialog_SelectPathFile_Title,
            TryGetExistingDirectory(PathTextBox.Text),
            LocalizedStrings.Common_OpenButton,
            PathRuleFilePickerSettingsIdentifier);

        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            PathTextBox.Text = selectedPath;
        }
    }

    private async void BrowsePathFolderButton_Click(object sender, RoutedEventArgs e)
    {
        string? selectedPath = await _fileDialogService.PickFolderAsync(
            OwnerWindowHandle,
            LocalizedStrings.SRP_Dialog_SelectPathFolder_Title,
            TryGetExistingDirectory(PathTextBox.Text),
            LocalizedStrings.Common_OpenButton,
            PathRuleFolderPickerSettingsIdentifier);

        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            PathTextBox.Text = selectedPath;
        }
    }

    private async void BrowseFileButton_Click(object sender, RoutedEventArgs e)
    {
        bool isCertificateRule = _kind == SoftwareRestrictionRuleKind.Certificate;
        string? selectedPath = await _fileDialogService.OpenFileAsync(
            OwnerWindowHandle,
            isCertificateRule ? CreateCertificateFilesFilter() : CreateAllFilesFilter(),
            isCertificateRule ? LocalizedStrings.SRP_Dialog_SelectCertificateFile_Title : LocalizedStrings.SRP_Dialog_SelectHashFile_Title,
            TryGetExistingDirectory(FileTextBox.Text),
            LocalizedStrings.Common_OpenButton,
            isCertificateRule ? CertificateRuleFilePickerSettingsIdentifier : HashFilePickerSettingsIdentifier);

        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            FileTextBox.Text = selectedPath;
        }
    }

    private string CreateAllFilesFilter()
    {
        return $"{LocalizedStrings.SRP_FileDialog_AllFiles}\0{AllFilesFilterPattern}\0";
    }

    private string CreateCertificateFilesFilter()
    {
        return $"{LocalizedStrings.SRP_FileDialog_CertificateFiles}\0*.cer;*.crt;*.der;*.pem\0"
            + $"{LocalizedStrings.SRP_FileDialog_SignedFiles}\0*.exe;*.dll;*.msi;*.msp;*.cab;*.cat;*.ocx;*.sys\0"
            + $"{LocalizedStrings.SRP_FileDialog_AllFiles}\0{AllFilesFilterPattern}\0";
    }

    private static string? TryGetExistingDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            string expandedPath = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            if (Directory.Exists(expandedPath))
            {
                return expandedPath;
            }

            string? directory = Path.GetDirectoryName(expandedPath);
            return !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory)
                ? directory
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
