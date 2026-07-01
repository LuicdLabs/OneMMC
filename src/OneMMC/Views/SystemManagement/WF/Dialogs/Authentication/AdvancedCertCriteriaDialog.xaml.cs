using System;
using System.Collections.Generic;
using System.Linq;
using OneMMC.Core.Features.SystemManagement.Models.WF.Authentication;
using OneMMC.Core.Features.SystemManagement.Models.WF.ConnectionSecurity;
using OneMMC.Core.Features.SystemManagement.Models.WF.Monitoring;
using OneMMC.Core.Features.SystemManagement.Models.WF.Profiles;
using OneMMC.Core.Features.SystemManagement.Models.WF.Rules;
using OneMMC.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OneMMC.Views.Dialogs.Authentication;

public sealed partial class AdvancedCertCriteriaDialog : UserControl
{
    private readonly Action<ElementTheme> _themeChangedHandler;

    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    public AdvancedCertCriteriaResult? Result { get; private set; }

    public AdvancedCertCriteriaDialog()
    {
        InitializeComponent();
        RequestedTheme = App.CurrentTheme;
        _themeChangedHandler = theme => RequestedTheme = theme;
        Loaded += AdvancedCertCriteriaDialog_Loaded;
        Unloaded += AdvancedCertCriteriaDialog_Unloaded;
    }

    private void AdvancedCertCriteriaDialog_Loaded(object sender, RoutedEventArgs e)
    {
        App.ThemeChanged -= _themeChangedHandler;
        App.ThemeChanged += _themeChangedHandler;
    }

    private void AdvancedCertCriteriaDialog_Unloaded(object sender, RoutedEventArgs e)
    {
        App.ThemeChanged -= _themeChangedHandler;
    }

    private void EkuCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CustomEkuTextBox is null) return;
        var tag = (EkuCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        CustomEkuTextBox.Visibility = tag == "Custom" ? Visibility.Visible : Visibility.Collapsed;
        ResetValidationMessage();
    }

    private void EkuAddButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = EkuCombo.SelectedItem as ComboBoxItem;
        var tag = selected?.Tag as string;
        var value = tag == "Custom" ? CustomEkuTextBox.Text.Trim() : tag;
        if (tag == "Custom" && string.IsNullOrWhiteSpace(value))
        {
            ShowValidationMessage(LocalizedStrings.WF_Validation_CustomEkuRequired);
            return;
        }

        if (!string.IsNullOrEmpty(value))
        {
            RequiredEkuList.Items.Add(value);
            ResetValidationMessage();
        }
    }

    private void EkuRemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (RequiredEkuList.SelectedItem != null)
        {
            RequiredEkuList.Items.Remove(RequiredEkuList.SelectedItem);
            ResetValidationMessage();
        }
    }

    public void ClearAll()
    {
        NoRestrictionRadio.IsChecked = true;
        EkuCombo.SelectedIndex = 0;
        RequiredEkuList.Items.Clear();
        CustomEkuTextBox.Text = string.Empty;
        CustomEkuTextBox.Visibility = Visibility.Collapsed;
        NameTypeCombo.SelectedIndex = 0;
        CertNameTextBox.Text = string.Empty;
        ThumbprintTextBox.Text = string.Empty;
        FollowRenewalCheckBox.IsChecked = false;
        Result = null;
        ResetValidationMessage();
    }

    public bool TryCommitResult()
    {
        string? validationError = GetValidationErrorMessage();
        if (validationError is not null)
        {
            ShowValidationMessage(validationError);
            return false;
        }

        ResetValidationMessage();
        CommitResult();
        return true;
    }

    public void CommitResult()
    {
        string certificateName = CertNameTextBox.Text.Trim();
        string thumbprint = NormalizeThumbprint(ThumbprintTextBox.Text);
        string restrictUsage = NoRestrictionRadio.IsChecked == true
            ? "No restriction"
            : SelectionOnlyRadio.IsChecked == true
                ? "Selection only"
                : "Validation only";

        List<string> requiredEkus = RequiredEkuList.Items
            .OfType<object>()
            .Select(item => item.ToString())
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Cast<string>()
            .ToList();

        string nameTypeTag = (NameTypeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "None";

        Result = new AdvancedCertCriteriaResult
        {
            RestrictUsage = restrictUsage,
            RequiredEkus = requiredEkus,
            NameTypeTag = nameTypeTag,
            CertificateName = certificateName,
            Thumbprint = thumbprint,
            FollowRenewal = FollowRenewalCheckBox.IsChecked == true
        };
    }

    public void ApplyResult(AdvancedCertCriteriaResult? result)
    {
        if (result is null)
        {
            return;
        }

        switch (result.RestrictUsage)
        {
            case "Selection only":
                SelectionOnlyRadio.IsChecked = true;
                break;
            case "Validation only":
                ValidationOnlyRadio.IsChecked = true;
                break;
            default:
                NoRestrictionRadio.IsChecked = true;
                break;
        }

        RequiredEkuList.Items.Clear();
        foreach (string eku in result.RequiredEkus)
        {
            RequiredEkuList.Items.Add(eku);
        }

        SelectComboByTag(NameTypeCombo, result.NameTypeTag);
        CertNameTextBox.Text = result.CertificateName;
        ThumbprintTextBox.Text = result.Thumbprint;
        FollowRenewalCheckBox.IsChecked = result.FollowRenewal;
        Result = result;
        ResetValidationMessage();
    }

    private string? GetValidationErrorMessage()
    {
        string certificateName = CertNameTextBox.Text.Trim();
        string nameTypeTag = (NameTypeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "None";
        bool hasCertificateName = !string.IsNullOrWhiteSpace(certificateName);
        bool hasNameType = !string.Equals(nameTypeTag, "None", StringComparison.Ordinal);
        if (hasCertificateName != hasNameType)
        {
            return LocalizedStrings.WF_Validation_CertificateNameRestrictionIncomplete;
        }

        if (FollowRenewalCheckBox.IsChecked == true &&
            string.IsNullOrWhiteSpace(NormalizeThumbprint(ThumbprintTextBox.Text)))
        {
            return LocalizedStrings.WF_Validation_FollowRenewalRequiresThumbprint;
        }

        return null;
    }

    private void ShowValidationMessage(string message)
    {
        ValidationMessageTextBlock.Text = message;
        ValidationMessageTextBlock.Visibility = Visibility.Visible;
    }

    private void ResetValidationMessage()
    {
        ValidationMessageTextBlock.Text = string.Empty;
        ValidationMessageTextBlock.Visibility = Visibility.Collapsed;
    }

    private static string NormalizeThumbprint(string? thumbprint)
        => string.Concat((thumbprint ?? string.Empty).Where(ch => !char.IsWhiteSpace(ch)));

    private static void SelectComboByTag(ComboBox comboBox, string tag)
    {
        for (int i = 0; i < comboBox.Items.Count; i++)
        {
            if (comboBox.Items[i] is ComboBoxItem item &&
                string.Equals(item.Tag?.ToString(), tag, StringComparison.Ordinal))
            {
                comboBox.SelectedIndex = i;
                return;
            }
        }
    }
}
