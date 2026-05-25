using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;
using System;
using System.Linq;
using ManagementTools.Localization;
using ManagementTools.Core.Features.PolicyManagement.Services.GpEdit;
using ManagementTools.Core.Features.PolicyManagement.Models.GpEdit;

namespace ManagementTools.Views.PolicyManagement.GpEdit
{
    public sealed partial class PolicyEditorDialog : ContentDialog
    {
	public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

        private PolicyManagerPolicy _policy;
        private Dictionary<string, object> _currentOptions;
        
        public PolicyState ResultState { get; private set; }
        public Dictionary<string, object> ResultOptions { get; private set; } = new Dictionary<string, object>();

        private enum OptionValueKind
        {
            Text,
            Bool,
            UInt,
            EnumIndex,
            ListSimple,
            ListDictionary,
            MultiText
        }

        private sealed record OptionTag(string Id, OptionValueKind Kind);

        public PolicyEditorDialog(PolicyManagerPolicy policy, PolicyState initialState, Dictionary<string, object> currentOptions, bool isComputerPolicy = true)
        {
            this.InitializeComponent();
            _policy = policy;
            _currentOptions = currentOptions ?? new Dictionary<string, object>();

            PolicyTitle.Text = policy.DisplayName;
            ExplainText.Text = policy.DisplayExplanation;
            if (policy.SupportedOn != null)
                SupportedOnText.Text = string.Concat(LocalizedStrings.Policy_SupportedOn_Prefix, " ", policy.SupportedOn.DisplayName);

            // Show target registry hive
            HiveInfoText.Text = isComputerPolicy 
                ? LocalizedStrings.Policy_HiveInfo_Machine
                : LocalizedStrings.Policy_HiveInfo_User;

            // Initialize the ComboBox selection to match initial state
            switch (initialState)
            {
                case PolicyState.NotConfigured: StateCombo.SelectedIndex = 0; break;
                case PolicyState.Enabled: StateCombo.SelectedIndex = 1; break;
                case PolicyState.Disabled: StateCombo.SelectedIndex = 2; break;
            }

            // Initialize ResultState to match initial state
            ResultState = initialState;

            RenderPresentation();
            UpdateOptionsState();
        }

        private void StateCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (StateCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                switch (tag)
                {
                    case "Enabled": ResultState = PolicyState.Enabled; break;
                    case "Disabled": ResultState = PolicyState.Disabled; break;
                    default: ResultState = PolicyState.NotConfigured; break;
                }
            }
            else
            {
                ResultState = PolicyState.NotConfigured;
            }
            
            UpdateOptionsState();
        }

        private void UpdateOptionsState()
        {
            bool enabled = ResultState == PolicyState.Enabled;
            foreach (var child in OptionsPanel.Children)
            {
                if (child is Control ctrl)
                    ctrl.IsEnabled = enabled;
            }
        }

        private void RenderPresentation()
        {
            OptionsPanel.Children.Clear();
            NoOptionsText.Visibility = Visibility.Collapsed;
            if (_policy.Presentation == null || _policy.Presentation.Elements.Count == 0)
            {
                NoOptionsText.Visibility = Visibility.Visible;
                OptionsPanel.Children.Add(NoOptionsText);
                return;
            }

            foreach (var elem in _policy.Presentation.Elements)
            {
                UIElement? control = null;
                switch (elem)
                {
                    case LabelPresentationElement label:
                        control = new TextBlock { Text = label.Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
                        break;
                    case TextBoxPresentationElement textBox:
                        var sp = new StackPanel();
                        if (!string.IsNullOrEmpty(textBox.Label))
                            sp.Children.Add(new TextBlock { Text = textBox.Label, Margin = new Thickness(0, 0, 0, 4) });
                        var tb = new TextBox();
                        if (_currentOptions.ContainsKey(textBox.ID)) tb.Text = _currentOptions[textBox.ID]?.ToString() ?? string.Empty;
                        else tb.Text = textBox.DefaultValue ?? string.Empty;
                        tb.Tag = new OptionTag(textBox.ID, OptionValueKind.Text);
                        sp.Children.Add(tb);
                        control = sp;
                        break;
                    case CheckBoxPresentationElement checkBox:
                        var cb = new CheckBox { Content = checkBox.Text };
                        if (_currentOptions.ContainsKey(checkBox.ID)) cb.IsChecked = (bool?)_currentOptions[checkBox.ID] ?? false; 
                        else cb.IsChecked = checkBox.DefaultState;
                        cb.Tag = new OptionTag(checkBox.ID, OptionValueKind.Bool);
                        control = cb;
                        break;
                    case NumericBoxPresentationElement numBox:
                        var nsp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                        if (!string.IsNullOrEmpty(numBox.Label))
                            nsp.Children.Add(new TextBlock { Text = numBox.Label, VerticalAlignment = VerticalAlignment.Center });
                        var nb = new NumberBox { SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, Width = 150 };
                         if (_currentOptions.ContainsKey(numBox.ID)) nb.Value = Convert.ToDouble(_currentOptions[numBox.ID]);
                         else nb.Value = numBox.DefaultValue;
                        nb.Tag = new OptionTag(numBox.ID, OptionValueKind.UInt);
                        nsp.Children.Add(nb);
                         control = nsp;
                        break;
                    case ComboBoxPresentationElement comboBox:
                        var csp = new StackPanel();
                         if (!string.IsNullOrEmpty(comboBox.Label))
                            csp.Children.Add(new TextBlock { Text = comboBox.Label, Margin = new Thickness(0, 0, 0, 4) });
                        var cmb = new ComboBox { IsEditable = true, Width = 200 };
                        foreach(var s in comboBox.Suggestions) cmb.Items.Add(s);
                         if (_currentOptions.ContainsKey(comboBox.ID)) cmb.Text = _currentOptions[comboBox.ID]?.ToString() ?? string.Empty;
                         else cmb.Text = comboBox.DefaultText ?? string.Empty;
                        cmb.Tag = new OptionTag(comboBox.ID, OptionValueKind.Text);
                        csp.Children.Add(cmb);
                        control = csp;
                        break;
                     case DropDownPresentationElement dropDown:
                        var dsp = new StackPanel();
                         if (!string.IsNullOrEmpty(dropDown.Label))
                            dsp.Children.Add(new TextBlock { Text = dropDown.Label, Margin = new Thickness(0, 0, 0, 4) });
                        var ddl = new ComboBox { Width = 200 };
                        var linkedElem = _policy.RawPolicy.Elements.FirstOrDefault(e => e.ID == dropDown.ID) as EnumPolicyElement;
                        if (linkedElem != null)
                        {
                            foreach(var item in linkedElem.Items) ddl.Items.Add(item.DisplayCode);
                            if (_currentOptions.ContainsKey(dropDown.ID)) ddl.SelectedIndex = (int)_currentOptions[dropDown.ID];
                            else if (dropDown.DefaultItemID.HasValue) ddl.SelectedIndex = dropDown.DefaultItemID.Value;
                        }
                        ddl.Tag = new OptionTag(dropDown.ID, OptionValueKind.EnumIndex);
                        dsp.Children.Add(ddl);
                        control = dsp;
                        break;
                    case ListPresentationElement listElem:
                        var lsp = new StackPanel();
                        if (!string.IsNullOrEmpty(listElem.Label))
                            lsp.Children.Add(new TextBlock { Text = listElem.Label, Margin = new Thickness(0, 0, 0, 4) });

                        var listTextBox = new TextBox
                        {
                            AcceptsReturn = true,
                            TextWrapping = TextWrapping.Wrap,
                            MinHeight = 120
                        };

                        var listMeta = FindListElement(listElem.ID);
                        listTextBox.Text = BuildListEditorText(listElem.ID);
                        var listKind = (listMeta != null && listMeta.UserProvidesNames) ? OptionValueKind.ListDictionary : OptionValueKind.ListSimple;
                        listTextBox.Tag = new OptionTag(listElem.ID, listKind);
                        lsp.Children.Add(listTextBox);
                        control = lsp;
                        break;
                    case MultiTextPresentationElement multiText:
                        var mtsp = new StackPanel();
                        if (!string.IsNullOrEmpty(multiText.Label))
                            mtsp.Children.Add(new TextBlock { Text = multiText.Label, Margin = new Thickness(0, 0, 0, 4) });

                        var multiTb = new TextBox
                        {
                            AcceptsReturn = true,
                            TextWrapping = TextWrapping.Wrap,
                            MinHeight = 120,
                            Text = BuildMultiTextEditorText(multiText.ID)
                        };
                        multiTb.Tag = new OptionTag(multiText.ID, OptionValueKind.MultiText);
                        mtsp.Children.Add(multiTb);
                        control = mtsp;
                        break;
                }

                if (control != null)
                {
                    OptionsPanel.Children.Add(control);
                }
            }
        }

        private ListPolicyElement? FindListElement(string id)
        {
            return _policy.RawPolicy?.Elements?.FirstOrDefault(e => e.ID == id) as ListPolicyElement;
        }

        private string BuildListEditorText(string id)
        {
            if (_currentOptions.TryGetValue(id, out var value))
            {
                if (value is Dictionary<string, string> dict)
                    return string.Join(Environment.NewLine, dict.Select(kv => $"{kv.Key}={kv.Value}"));
                if (value is List<string> list)
                    return string.Join(Environment.NewLine, list);
            }
            return string.Empty;
        }

        private string BuildMultiTextEditorText(string id)
        {
            if (_currentOptions.TryGetValue(id, out var value))
            {
                if (value is string[] arr) return string.Join(Environment.NewLine, arr);
                if (value is IEnumerable<string> enumerable) return string.Join(Environment.NewLine, enumerable);
                return value?.ToString() ?? string.Empty;
            }
            return string.Empty;
        }

        private static List<string> SplitLinesToList(string text)
        {
            return (text ?? string.Empty)
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrEmpty(l))
                .ToList();
        }

        private Dictionary<string, string> ParseDictionaryLines(string text, bool strict, out string? error)
        {
            var dict = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);
            error = null;
            foreach (var line in (text ?? string.Empty).Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                var parts = trimmed.Split(new[] { '=' }, 2);
                if (parts.Length != 2)
                {
                    if (strict)
                    {
                        error = string.Format(LocalizedStrings.Policy_Error_InvalidFormat_Format ?? "Invalid format: {0}", trimmed);
                        return dict;
                    }
                    continue;
                }

                dict[parts[0].Trim()] = parts[1].Trim();
            }
            return dict;
        }

        private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            // Ensure ResultState matches the current ComboBox selection
            if (StateCombo.SelectedItem is ComboBoxItem sel && sel.Tag is string tag)
            {
                switch (tag)
                {
                    case "Enabled": ResultState = PolicyState.Enabled; break;
                    case "Disabled": ResultState = PolicyState.Disabled; break;
                    default: ResultState = PolicyState.NotConfigured; break;
                }
            }

            // Hide any previous validation error
            ValidationErrorText.Visibility = Visibility.Collapsed;

            // Always collect options so we preserve user input
            // Only validate when in Enabled state, as options are only applied in that state
            bool valid = ResultState == PolicyState.Enabled ? CollectOptionsWithValidation() : CollectOptions();
            
            if (!valid)
            {
                args.Cancel = true; // Cancel dialog if validation fails
            }
        }

        // Collect options without validation (for Disabled/NotConfigured states)
        private bool CollectOptions()
        {
            ResultOptions.Clear();
            foreach (var child in OptionsPanel.Children)
            {
                if (child is StackPanel sp)
                {
                    foreach (var inner in sp.Children)
                    {
                        CheckElement(inner);
                    }
                }
                else
                {
                    CheckElement(child);
                }
            }
            return true;
        }

        private void CheckElement(UIElement element)
        {
            if (element is FrameworkElement fe && fe.Tag is OptionTag tag)
            {
                switch (element)
                {
                    case TextBox tb:
                        switch (tag.Kind)
                        {
                            case OptionValueKind.ListSimple:
                                ResultOptions[tag.Id] = SplitLinesToList(tb.Text);
                                break;
                            case OptionValueKind.ListDictionary:
                                ResultOptions[tag.Id] = ParseDictionaryLines(tb.Text, false, out _);
                                break;
                            case OptionValueKind.MultiText:
                                ResultOptions[tag.Id] = SplitLinesToList(tb.Text).ToArray();
                                break;
                            default:
                                ResultOptions[tag.Id] = tb.Text ?? string.Empty;
                                break;
                        }
                        break;
                    case CheckBox cb:
                        ResultOptions[tag.Id] = cb.IsChecked ?? false;
                        break;
                    case NumberBox nb:
                        ResultOptions[tag.Id] = (uint)nb.Value;
                        break;
                    case ComboBox cmb:
                        if (tag.Kind == OptionValueKind.EnumIndex)
                            ResultOptions[tag.Id] = cmb.SelectedIndex;
                        else
                            ResultOptions[tag.Id] = cmb.Text ?? string.Empty;
                        break;
                }
            }
        }

        // Collect options and validate (used when Enabled)
        private bool CollectOptionsWithValidation()
        {
            ResultOptions.Clear();
            bool valid = true;
            string? errorMsg = null;
            foreach (var child in OptionsPanel.Children)
            {
                if (child is StackPanel sp)
                {
                    foreach (var inner in sp.Children)
                    {
                        if (!CheckElementWithValidation(inner, ref errorMsg))
                            valid = false;
                    }
                }
                else
                {
                    if (!CheckElementWithValidation(child, ref errorMsg))
                        valid = false;
                }
            }
            if (!valid && !string.IsNullOrEmpty(errorMsg))
            {
                ShowValidationError(errorMsg);
            }
            return valid;
        }

        // Inspect elements and perform validation
        private bool CheckElementWithValidation(UIElement element, ref string? errorMsg)
        {
            if (element is FrameworkElement fe && fe.Tag is OptionTag tag)
            {
                switch (element)
                {
                    case TextBox tb:
                        switch (tag.Kind)
                        {
                            case OptionValueKind.ListDictionary:
                                var dict = ParseDictionaryLines(tb.Text, true, out var parseError);
                                if (parseError != null)
                                {
                                    errorMsg = parseError;
                                    return false;
                                }
                                ResultOptions[tag.Id] = dict;
                                break;
                            case OptionValueKind.ListSimple:
                                ResultOptions[tag.Id] = SplitLinesToList(tb.Text);
                                break;
                            case OptionValueKind.MultiText:
                                ResultOptions[tag.Id] = SplitLinesToList(tb.Text).ToArray();
                                break;
                            default:
                                if (string.IsNullOrWhiteSpace(tb.Text))
                                {
                                    errorMsg = string.Format(LocalizedStrings.Policy_Error_PleaseEnter_Format ?? "Please enter a value for {0}", tag.Id);
                                    return false;
                                }
                                ResultOptions[tag.Id] = tb.Text ?? string.Empty;
                                break;
                        }
                        break;
                    case CheckBox cb:
                        ResultOptions[tag.Id] = cb.IsChecked ?? false;
                        break;
                    case NumberBox nb:
                        if (double.IsNaN(nb.Value) || nb.Value < 0)
                        {
                            errorMsg = string.Format(LocalizedStrings.Policy_Error_EnterValidNumber_Format ?? "Please enter a valid number for {0}", tag.Id);
                            return false;
                        }
                        try
                        {
                            ResultOptions[tag.Id] = Convert.ToUInt32(nb.Value);
                        }
                        catch
                        {
                            errorMsg = string.Format(LocalizedStrings.Policy_Error_ValueOutOfRange_Format ?? "Value out of range for {0}", tag.Id);
                            return false;
                        }
                        break;
                    case ComboBox cmb:
                        if (tag.Kind == OptionValueKind.EnumIndex)
                        {
                            if (cmb.SelectedIndex < 0)
                            {
                                errorMsg = string.Format(LocalizedStrings.Policy_Error_PleaseSelect_Format ?? "Please select a value for {0}", tag.Id);
                                return false;
                            }
                            ResultOptions[tag.Id] = cmb.SelectedIndex;
                        }
                        else
                        {
                            if (string.IsNullOrWhiteSpace(cmb.Text))
                            {
                                errorMsg = string.Format(LocalizedStrings.Policy_Error_PleaseSelectOrEnter_Format ?? "Please select or enter a value for {0}", tag.Id);
                                return false;
                            }
                            ResultOptions[tag.Id] = cmb.Text ?? string.Empty;
                        }
                        break;
                }
            }
            return true;
        }

        // Show validation error messages
        private void ShowValidationError(string msg)
        {
            ValidationErrorText.Text = msg;
            ValidationErrorText.Visibility = Visibility.Visible;
        }
        
        private void PolicySelectorBar_SelectionChanged(Microsoft.UI.Xaml.Controls.SelectorBar sender, Microsoft.UI.Xaml.Controls.SelectorBarSelectionChangedEventArgs args)
        {
            if (sender.SelectedItem is Microsoft.UI.Xaml.Controls.SelectorBarItem item && item.Tag is string tag)
            {
                if (tag == "General")
                {
                    GeneralPanel.Visibility = Visibility.Visible;
                    ExplainPanel.Visibility = Visibility.Collapsed;
                }
                else
                {
                    GeneralPanel.Visibility = Visibility.Collapsed;
                    ExplainPanel.Visibility = Visibility.Visible;
                }
            }
        }
    }
}



