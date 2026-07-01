using System;
using System.Collections.Generic;
using OneMMC.Core.Features.SystemManagement.Models.WF.Authentication;
using OneMMC.Core.Features.SystemManagement.Models.WF.ConnectionSecurity;
using OneMMC.Core.Features.SystemManagement.Models.WF.Monitoring;
using OneMMC.Core.Features.SystemManagement.Models.WF.Profiles;
using OneMMC.Core.Features.SystemManagement.Models.WF.Rules;
using OneMMC.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OneMMC.Views.Dialogs.WFProperties;

public sealed partial class IntegrityEncryptionAlgorithmEditorDialog : UserControl
{
    public IntegrityEncryptionAlgorithmEntry? Result { get; private set; }

    public bool CanSave { get; private set; } = true;
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    public IntegrityEncryptionAlgorithmEditorDialog()
    {
        InitializeComponent();
        RequestedTheme = App.CurrentTheme;
        App.ThemeChanged += OnThemeChanged;
        Unloaded += IntegrityEncryptionAlgorithmEditorDialog_Unloaded;

        EncryptionAlgorithmComboBox.SelectedIndex = 0;
        IntegrityAlgorithmComboBox.SelectedIndex = 0;
        RefreshState();
    }

    public void ApplyEntry(IntegrityEncryptionAlgorithmEntry entry)
    {
        SelectComboByContent(EncryptionAlgorithmComboBox, entry.EncryptionAlgorithm);
        SelectIntegrityComboByContent(entry.IntegrityAlgorithm, entry.EncryptionAlgorithm);
        MinutesNumberBox.Value = entry.MinutesLifetime;
        KilobytesNumberBox.Value = entry.KilobytesLifetime;

        if (string.Equals(entry.Protocol, "ESP and AH", StringComparison.Ordinal))
        {
            EspAndAhProtocolRadioButton.IsChecked = true;
        }
        else
        {
            EspProtocolRadioButton.IsChecked = true;
        }

        RefreshState();
    }

    public void CommitResult()
    {
        if (!CanSave)
        {
            Result = null;
            return;
        }

        Result = new IntegrityEncryptionAlgorithmEntry
        {
            Protocol = EspAndAhProtocolRadioButton.IsChecked == true ? "ESP and AH" : "ESP",
            EncryptionAlgorithm = GetSelectedText(EncryptionAlgorithmComboBox),
            IntegrityAlgorithm = GetSelectedText(IntegrityAlgorithmComboBox),
            MinutesLifetime = (int)Math.Max(0, MinutesNumberBox.Value),
            KilobytesLifetime = (int)Math.Max(0, KilobytesNumberBox.Value)
        };
    }

    private void EncryptionAlgorithmComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshState();
    }

    private void IntegrityAlgorithmComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshState();
    }

    private void ProtocolRadioButton_Checked(object sender, RoutedEventArgs e)
    {
        RefreshState();
    }

    private void RefreshState()
    {
        if (EncryptionAlgorithmComboBox is null ||
            IntegrityAlgorithmComboBox is null ||
            EspAndAhProtocolRadioButton is null ||
            EncryptionDescriptionTextBlock is null ||
            IntegrityDescriptionTextBlock is null ||
            ValidationWarningTextBlock is null)
        {
            return;
        }

        string selectedEncryption = GetSelectedText(EncryptionAlgorithmComboBox);
        string selectedIntegrity = GetSelectedText(IntegrityAlgorithmComboBox);

        EncryptionDescriptionTextBlock.Text = GetEncryptionDescription(selectedEncryption);
        IntegrityDescriptionTextBlock.Text = GetIntegrityDescription(selectedIntegrity);

        CanSave = IsValidCombination(selectedEncryption, selectedIntegrity);
        ValidationWarningTextBlock.Visibility = CanSave ? Visibility.Collapsed : Visibility.Visible;
    }

    private static bool IsValidCombination(string encryptionAlgorithm, string integrityAlgorithm)
    {
        // AES-GCM is AEAD and requires an exact AES-GCM integrity match.
        if (encryptionAlgorithm.StartsWith("AES-GCM", StringComparison.Ordinal))
        {
            return string.Equals(encryptionAlgorithm, integrityAlgorithm, StringComparison.Ordinal);
        }

        // Non-AES-GCM encryption cannot pair with AES-GCM or AES-GMAC integrity.
        if (integrityAlgorithm.StartsWith("AES-GCM", StringComparison.Ordinal) ||
            integrityAlgorithm.StartsWith("AES-GMAC", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private void SelectIntegrityComboByContent(string integrityAlgorithm, string encryptionAlgorithm)
    {
        if (SelectComboByContent(IntegrityAlgorithmComboBox, integrityAlgorithm))
        {
            return;
        }

        string fallback = encryptionAlgorithm.StartsWith("AES-GCM", StringComparison.Ordinal)
            ? encryptionAlgorithm
            : "SHA-256";

        if (!SelectComboByContent(IntegrityAlgorithmComboBox, fallback))
        {
            IntegrityAlgorithmComboBox.SelectedIndex = 0;
        }
    }

    private static string GetSelectedText(ComboBox comboBox)
    {
        if (comboBox.SelectedItem is not ComboBoxItem item)
        {
            return string.Empty;
        }

        return item.Tag?.ToString() ?? item.Content?.ToString() ?? string.Empty;
    }

    private static bool SelectComboByContent(ComboBox comboBox, string content)
    {
        for (int i = 0; i < comboBox.Items.Count; i++)
        {
            if (comboBox.Items[i] is ComboBoxItem item &&
                (string.Equals(item.Tag?.ToString(), content, StringComparison.Ordinal) ||
                 string.Equals(item.Content?.ToString(), content, StringComparison.Ordinal)))
            {
                comboBox.SelectedIndex = i;
                return true;
            }
        }

        return false;
    }

    private string GetEncryptionDescription(string algorithm)
        => algorithm switch
        {
            "AES-GCM 256" => LocalizedStrings.WF_AlgorithmDescription_AesGcm256,
            "AES-GCM 192" => LocalizedStrings.WF_AlgorithmDescription_AesGcm192,
            "AES-GCM 128" => LocalizedStrings.WF_AlgorithmDescription_AesGcm128,
            "AES-CBC 256" => LocalizedStrings.WF_AlgorithmDescription_DataAesCbc256,
            "AES-CBC 192" => LocalizedStrings.WF_AlgorithmDescription_DataAesCbc192,
            "AES-CBC 128" => LocalizedStrings.WF_AlgorithmDescription_DataAesCbc128,
            "3DES" => LocalizedStrings.WF_AlgorithmDescription_HigherResourceThanDes,
            "DES" => LocalizedStrings.WF_AlgorithmDescription_Des,
            _ => string.Empty
        };

    private string GetIntegrityDescription(string algorithm)
        => algorithm switch
        {
            "AES-GCM 256" => LocalizedStrings.WF_AlgorithmDescription_AesGmac256,
            "AES-GCM 192" => LocalizedStrings.WF_AlgorithmDescription_AesGmac192,
            "AES-GCM 128" => LocalizedStrings.WF_AlgorithmDescription_AesGmac128,
            "AES-GMAC 256" => LocalizedStrings.WF_AlgorithmDescription_AesGmac256,
            "AES-GMAC 192" => LocalizedStrings.WF_AlgorithmDescription_AesGmac192,
            "AES-GMAC 128" => LocalizedStrings.WF_AlgorithmDescription_AesGmac128,
            "SHA-256" => LocalizedStrings.WF_AlgorithmDescription_CompatibleVistaSp1,
            "SHA-1" => LocalizedStrings.WF_AlgorithmDescription_Sha1,
            "MD5" => LocalizedStrings.WF_AlgorithmDescription_Md5,
            _ => string.Empty
        };

    private void OnThemeChanged(ElementTheme theme)
    {
        RequestedTheme = theme;
    }

    private void IntegrityEncryptionAlgorithmEditorDialog_Unloaded(object sender, RoutedEventArgs e)
    {
        App.ThemeChanged -= OnThemeChanged;
    }
}
