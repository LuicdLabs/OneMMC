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

public sealed partial class IntegrityAlgorithmEditorDialog : UserControl
{
    public DataIntegrityAlgorithmEntry? Result { get; private set; }
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    public IntegrityAlgorithmEditorDialog()
    {
        InitializeComponent();
        RequestedTheme = App.CurrentTheme;
        App.ThemeChanged += OnThemeChanged;
        Unloaded += IntegrityAlgorithmEditorDialog_Unloaded;

        IntegrityAlgorithmComboBox.SelectedIndex = 3;
        RefreshDescription();
    }

    public void ApplyEntry(DataIntegrityAlgorithmEntry entry)
    {
        SelectComboByContent(IntegrityAlgorithmComboBox, entry.IntegrityAlgorithm);
        MinutesNumberBox.Value = entry.MinutesLifetime;
        KilobytesNumberBox.Value = entry.KilobytesLifetime;

        switch (entry.Protocol)
        {
            case "AH":
                AhProtocolRadioButton.IsChecked = true;
                break;
            case "Null encapsulation":
                NullEncapsulationProtocolRadioButton.IsChecked = true;
                break;
            default:
                EspProtocolRadioButton.IsChecked = true;
                break;
        }

        RefreshDescription();
    }

    public void CommitResult()
    {
        Result = new DataIntegrityAlgorithmEntry
        {
            Protocol = GetSelectedProtocol(),
            IntegrityAlgorithm = GetSelectedText(IntegrityAlgorithmComboBox),
            MinutesLifetime = (int)Math.Max(0, MinutesNumberBox.Value),
            KilobytesLifetime = (int)Math.Max(0, KilobytesNumberBox.Value)
        };
    }

    private void IntegrityAlgorithmComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshDescription();
    }

    private void RefreshDescription()
    {
        string selected = GetSelectedText(IntegrityAlgorithmComboBox);
        IntegrityDescriptionTextBlock.Text = GetIntegrityDescription(selected);
    }

    private string GetSelectedProtocol()
    {
        if (AhProtocolRadioButton.IsChecked == true)
        {
            return "AH";
        }

        if (NullEncapsulationProtocolRadioButton.IsChecked == true)
        {
            return "Null encapsulation";
        }

        return "ESP";
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

    private void IntegrityAlgorithmEditorDialog_Unloaded(object sender, RoutedEventArgs e)
    {
        App.ThemeChanged -= OnThemeChanged;
    }
}
