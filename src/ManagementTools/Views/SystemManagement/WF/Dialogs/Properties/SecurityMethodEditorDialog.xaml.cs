using System;
using System.Collections.Generic;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Authentication;
using ManagementTools.Core.Features.SystemManagement.Models.WF.ConnectionSecurity;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Monitoring;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Profiles;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Rules;
using ManagementTools.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ManagementTools.Views.Dialogs.WFProperties;

public sealed partial class SecurityMethodEditorDialog : UserControl
{
    public SecurityMethodEntry? Result { get; private set; }
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    public SecurityMethodEditorDialog()
    {
        InitializeComponent();
        RequestedTheme = App.CurrentTheme;
        App.ThemeChanged += OnThemeChanged;
        Unloaded += SecurityMethodEditorDialog_Unloaded;

        IntegrityComboBox.SelectedIndex = 1;
        EncryptionComboBox.SelectedIndex = 0;
        KeyExchangeComboBox.SelectedIndex = 1;
        RefreshDescriptions();
    }

    public void ApplyEntry(SecurityMethodEntry entry)
    {
        SelectComboByContent(IntegrityComboBox, entry.IntegrityAlgorithm);
        SelectComboByContent(EncryptionComboBox, entry.EncryptionAlgorithm);
        SelectComboByContent(KeyExchangeComboBox, entry.KeyExchangeAlgorithm);
        RefreshDescriptions();
    }

    public void CommitResult()
    {
        Result = new SecurityMethodEntry
        {
            IntegrityAlgorithm = GetSelectedText(IntegrityComboBox),
            EncryptionAlgorithm = GetSelectedText(EncryptionComboBox),
            KeyExchangeAlgorithm = GetSelectedText(KeyExchangeComboBox)
        };
    }

    private void IntegrityComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshIntegrityDescription();
    }

    private void EncryptionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshEncryptionDescription();
    }

    private void KeyExchangeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshKeyExchangeDescription();
    }

    private void RefreshDescriptions()
    {
        RefreshIntegrityDescription();
        RefreshEncryptionDescription();
        RefreshKeyExchangeDescription();
    }

    private void RefreshIntegrityDescription()
    {
        string selected = GetSelectedText(IntegrityComboBox);
        IntegrityDescriptionTextBlock.Text = GetIntegrityDescription(selected);
    }

    private void RefreshEncryptionDescription()
    {
        string selected = GetSelectedText(EncryptionComboBox);
        EncryptionDescriptionTextBlock.Text = GetEncryptionDescription(selected);
    }

    private void RefreshKeyExchangeDescription()
    {
        string selected = GetSelectedText(KeyExchangeComboBox);
        KeyExchangeDescriptionTextBlock.Text = GetKeyExchangeDescription(selected);
    }

    private static string GetSelectedText(ComboBox comboBox)
    {
        if (comboBox.SelectedItem is not ComboBoxItem item)
        {
            return string.Empty;
        }

        return item.Tag?.ToString() ?? item.Content?.ToString() ?? string.Empty;
    }

    private static void SelectComboByContent(ComboBox comboBox, string content)
    {
        for (int i = 0; i < comboBox.Items.Count; i++)
        {
            if (comboBox.Items[i] is ComboBoxItem item &&
                (string.Equals(item.Tag?.ToString(), content, StringComparison.Ordinal) ||
                 string.Equals(item.Content?.ToString(), content, StringComparison.Ordinal)))
            {
                comboBox.SelectedIndex = i;
                return;
            }
        }
    }

    private string GetIntegrityDescription(string algorithm)
        => algorithm switch
        {
            "SHA-384" or "SHA-256" => LocalizedStrings.WF_AlgorithmDescription_CompatibleVistaSp1,
            "SHA-1" => LocalizedStrings.WF_AlgorithmDescription_Sha1,
            "MD5" => LocalizedStrings.WF_AlgorithmDescription_Md5,
            _ => string.Empty
        };

    private string GetEncryptionDescription(string algorithm)
        => algorithm switch
        {
            "AES-CBC 256" => LocalizedStrings.WF_AlgorithmDescription_AesCbc256,
            "AES-CBC 192" => LocalizedStrings.WF_AlgorithmDescription_AesCbc192,
            "AES-CBC 128" => LocalizedStrings.WF_AlgorithmDescription_AesCbc128,
            "3DES" => LocalizedStrings.WF_AlgorithmDescription_HigherResourceThanDes,
            "DES" => LocalizedStrings.WF_AlgorithmDescription_Des,
            _ => string.Empty
        };

    private string GetKeyExchangeDescription(string algorithm)
        => algorithm switch
        {
            "Elliptic Curve Diffie-Hellman P-384" => LocalizedStrings.WF_AlgorithmDescription_EcdhP384,
            "Elliptic Curve Diffie-Hellman P-256" => LocalizedStrings.WF_AlgorithmDescription_EcdhP256,
            "Diffie-Hellman Group 24" => LocalizedStrings.WF_AlgorithmDescription_DiffieHellmanGroup24,
            "Diffie-Hellman Group 14" => LocalizedStrings.WF_AlgorithmDescription_DiffieHellmanGroup14,
            "Diffie-Hellman Group 2" => LocalizedStrings.WF_AlgorithmDescription_DiffieHellmanGroup2,
            "Diffie-Hellman Group 1" => LocalizedStrings.WF_AlgorithmDescription_DiffieHellmanGroup1,
            _ => string.Empty
        };

    private void OnThemeChanged(ElementTheme theme)
    {
        RequestedTheme = theme;
    }

    private void SecurityMethodEditorDialog_Unloaded(object sender, RoutedEventArgs e)
    {
        App.ThemeChanged -= OnThemeChanged;
    }
}
