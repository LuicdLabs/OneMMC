using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OneMMC.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OneMMC.Views.Dialogs.Network;

public sealed partial class ICMPSettingsDialog : UserControl
{
    private static readonly Dictionary<string, string> BuiltInTypeMappings = new(StringComparer.Ordinal)
    {
        ["Packet Too Big"] = "2:*",
        ["Destination Unreachable"] = "3:*",
        ["Source Quench"] = "4:*",
        ["Redirect"] = "5:*",
        ["Echo Request"] = "8:*",
        ["Router Advertisement"] = "9:*",
        ["Router Solicitation"] = "10:*",
        ["Time Exceeded"] = "11:*",
        ["Parameter Problem"] = "12:*",
        ["Timestamp Request"] = "13:*",
        ["Address Mask Request"] = "17:*"
    };

    private readonly HashSet<string> _builtInTokens = [];

    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    public string IcmpTypesAndCodesExpression { get; private set; } = string.Empty;

    public ICMPSettingsDialog()
    {
        InitializeComponent();

        SpecificTypesRadio.Checked += (_, _) => SetSpecificMode(true);
        AllTypesRadio.Checked      += (_, _) => SetSpecificMode(false);

        PopulateCodeComboBox();
        InitializeBuiltInMappings();

        SetSpecificMode(false);
    }

    public void ApplyIcmpTypesAndCodes(string expression)
    {
        ClearDynamicCustomEntries();
        ClearSelections();

        if (IsAnyExpression(expression))
        {
            AllTypesRadio.IsChecked = true;
            SetSpecificMode(false);
            IcmpTypesAndCodesExpression = string.Empty;
            return;
        }

        SpecificTypesRadio.IsChecked = true;
        SetSpecificMode(true);

        foreach (string token in ParseTokens(expression))
        {
            if (TryFindCheckboxByToken(token, out CheckBox existing))
            {
                existing.IsChecked = true;
                continue;
            }

            AddCustomCheckbox(token, isChecked: true);
        }

        IcmpTypesAndCodesExpression = NormalizeIcmpExpression(expression);
    }

    public void CommitResult()
    {
        if (AllTypesRadio.IsChecked == true)
        {
            IcmpTypesAndCodesExpression = string.Empty;
            return;
        }

        List<string> selectedTokens = IcmpCheckBoxPanel.Children
            .OfType<CheckBox>()
            .Where(checkBox => checkBox.IsChecked == true)
            .Select(GetTokenFromCheckbox)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        IcmpTypesAndCodesExpression = selectedTokens.Count == 0
            ? string.Empty
            : string.Join(",", selectedTokens);
    }

    private void SetSpecificMode(bool enabled)
    {
        foreach (var child in IcmpCheckBoxPanel.Children)
            if (child is CheckBox cb) cb.IsEnabled = enabled;

        TypeNumberBox.IsEnabled = enabled;
        CodeComboBox.IsEnabled  = enabled;
        AddTypeButton.IsEnabled = enabled;
    }

    private void AddTypeButton_Click(object sender, RoutedEventArgs e)
    {
        int type = (int)TypeNumberBox.Value;
        string codeToken = CodeComboBox.SelectedItem is ComboBoxItem item && item.Tag is int numericCode && numericCode >= 0
            ? numericCode.ToString(CultureInfo.InvariantCulture)
            : "*";

        string token = $"{type}:{codeToken}";
        if (TryFindCheckboxByToken(token, out _))
        {
            return;
        }

        AddCustomCheckbox(token, isChecked: true);
    }

    private void PopulateCodeComboBox()
    {
        CodeComboBox.Items.Add(new ComboBoxItem { Content = LocalizedStrings.WF_Common_Any, Tag = -1 });
        for (int i = 0; i <= 255; i++)
        {
            CodeComboBox.Items.Add(new ComboBoxItem
            {
                Content = i.ToString(CultureInfo.InvariantCulture),
                Tag = i
            });
        }

        CodeComboBox.SelectedIndex = 0;
    }

    private void InitializeBuiltInMappings()
    {
        foreach (CheckBox checkBox in IcmpCheckBoxPanel.Children.OfType<CheckBox>())
        {
            if (checkBox.Tag is string existingToken)
            {
                _builtInTokens.Add(existingToken);
                continue;
            }

            string label = checkBox.Content?.ToString() ?? string.Empty;
            if (BuiltInTypeMappings.TryGetValue(label, out string? token))
            {
                checkBox.Tag = token;
                _builtInTokens.Add(token);
            }
        }
    }

    private void AddCustomCheckbox(string token, bool isChecked)
    {
        var checkBox = new CheckBox
        {
            Content = BuildDisplayLabel(token),
            Tag = token,
            IsEnabled = true,
            IsChecked = isChecked
        };

        IcmpCheckBoxPanel.Children.Add(checkBox);
    }

    private void ClearSelections()
    {
        foreach (CheckBox checkBox in IcmpCheckBoxPanel.Children.OfType<CheckBox>())
        {
            checkBox.IsChecked = false;
        }
    }

    private void ClearDynamicCustomEntries()
    {
        List<CheckBox> dynamicEntries = IcmpCheckBoxPanel.Children
            .OfType<CheckBox>()
            .Where(checkBox => checkBox.Tag is string token && !_builtInTokens.Contains(token))
            .ToList();

        foreach (CheckBox dynamicEntry in dynamicEntries)
        {
            IcmpCheckBoxPanel.Children.Remove(dynamicEntry);
        }
    }

    private bool TryFindCheckboxByToken(string token, out CheckBox checkBox)
    {
        checkBox = IcmpCheckBoxPanel.Children
            .OfType<CheckBox>()
            .FirstOrDefault(item => string.Equals(GetTokenFromCheckbox(item), token, StringComparison.OrdinalIgnoreCase))!;

        return checkBox is not null;
    }

    private static IEnumerable<string> ParseTokens(string expression)
        => expression
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => token.Trim())
            .Where(token => !string.IsNullOrWhiteSpace(token));

    private static bool IsAnyExpression(string expression)
        => string.IsNullOrWhiteSpace(expression) ||
           string.Equals(expression, "Any", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(expression, "*", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeIcmpExpression(string expression)
    {
        List<string> tokens = ParseTokens(expression)
            .Select(token => NormalizeToken(token))
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return tokens.Count == 0 ? string.Empty : string.Join(",", tokens);
    }

    private static string GetTokenFromCheckbox(CheckBox checkBox)
    {
        if (checkBox.Tag is string token)
        {
            return NormalizeToken(token);
        }

        string label = checkBox.Content?.ToString() ?? string.Empty;
        return BuiltInTypeMappings.TryGetValue(label, out string? builtInToken)
            ? builtInToken
            : NormalizeToken(label);
    }

    private static string BuildDisplayLabel(string token)
    {
        string normalized = NormalizeToken(token);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return token;
        }

        int separatorIndex = normalized.IndexOf(':');
        if (separatorIndex <= 0)
        {
            return normalized;
        }

        string type = normalized[..separatorIndex];
        string code = normalized[(separatorIndex + 1)..];
        string codeLabel = string.Equals(code, "*", StringComparison.Ordinal) ? LocalizedStrings.Instance.WF_Common_Any : code;
        return string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Instance.WF_Icmp_CustomTypeFormat, type, codeLabel);
    }

    private static string NormalizeToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return string.Empty;
        }

        string trimmed = token.Trim();
        int separatorIndex = trimmed.IndexOf(':');
        if (separatorIndex <= 0)
        {
            return trimmed;
        }

        string typePart = trimmed[..separatorIndex].Trim();
        string codePart = trimmed[(separatorIndex + 1)..].Trim();
        if (!int.TryParse(typePart, NumberStyles.None, CultureInfo.InvariantCulture, out int typeNumber) ||
            typeNumber < 0 || typeNumber > 255)
        {
            return string.Empty;
        }

        string normalizedCode = string.Equals(codePart, "Any", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(codePart)
            ? "*"
            : codePart;

        if (!string.Equals(normalizedCode, "*", StringComparison.Ordinal) &&
            (!int.TryParse(normalizedCode, NumberStyles.None, CultureInfo.InvariantCulture, out int codeNumber) ||
             codeNumber < 0 || codeNumber > 255))
        {
            return string.Empty;
        }

        return $"{typeNumber}:{normalizedCode}";
    }
}
