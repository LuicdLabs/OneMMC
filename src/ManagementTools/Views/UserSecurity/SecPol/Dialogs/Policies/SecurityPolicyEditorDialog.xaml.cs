using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using ManagementTools.Core.Features.UserSecurity.Models.SecPol;
using ManagementTools.Core.Features.UserSecurity.Services.SecPol;
using ManagementTools.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ManagementTools.Views
{
    /// <summary>
    /// A unified dialog for editing any type of security policy.
    /// Dynamically shows the appropriate editor based on the policy type,
    /// matching the Windows <c>secpol.msc</c> property dialog.
    /// </summary>
    public sealed partial class SecurityPolicyEditorDialog : ContentDialog
    {
        private SecurityPolicyValue? _policyValue;
        private readonly ObservableCollection<string> _accounts = new();

        /// <summary>Whether this policy supports the "Not Defined" state.</summary>
        private bool _allowNotDefined;

        /// <summary>Tracks the active editor type for value collection on save.</summary>
        private SecurityPolicyType _activeEditorType;

        public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

        /// <summary>
        /// Gets the edited policy value after the dialog is closed with OK.
        /// </summary>
        public SecurityPolicyValue? EditedValue => _policyValue;

        public SecurityPolicyEditorDialog()
        {
            ManagementTools.Services.Logging.UiLogger.LogDebug("[SecurityPolicyEditorDialog] Initializing");
            InitializeComponent();
        }

        /// <summary>
        /// Configures the dialog for editing the specified policy value.
        /// </summary>
        public void SetPolicy(SecurityPolicyValue policyValue)
        {
            ManagementTools.Services.Logging.UiLogger.LogDebug($"[SecurityPolicyEditorDialog] Setting policy: {policyValue.Definition.Key}, type={policyValue.Definition.PolicyType}");

            _policyValue = ClonePolicyValue(policyValue);
            PolicyTitleText.Text = _policyValue.Definition.DisplayName;

            var explainText = SecurityPolicyResourceLoader.Instance.GetExplainText(_policyValue.Definition);
            ExplainText.Text = explainText;

            // Hide all editors first
            NumericEditor.Visibility = Visibility.Collapsed;
            BooleanEditor.Visibility = Visibility.Collapsed;
            StringEditor.Visibility = Visibility.Collapsed;
            MultiStringEditor.Visibility = Visibility.Collapsed;
            AuditEditor.Visibility = Visibility.Collapsed;
            UserRightsEditor.Visibility = Visibility.Collapsed;
            DropdownEditor.Visibility = Visibility.Collapsed;
            BitmaskFlagsEditor.Visibility = Visibility.Collapsed;

            // Set up "Define this policy" checkbox for AllowNotDefined policies
            _allowNotDefined = _policyValue.Definition.AllowNotDefined;
            if (_allowNotDefined)
            {
                DefinePolicyCheckBox.Visibility = Visibility.Visible;
                DefinePolicyCheckBox.IsChecked = _policyValue.IsDefined;
                EditorContainer.Opacity = _policyValue.IsDefined ? 1.0 : 0.4;
                SetEditorContainerEnabled(_policyValue.IsDefined);
            }
            else
            {
                DefinePolicyCheckBox.Visibility = Visibility.Collapsed;
                EditorContainer.Opacity = 1.0;
                SetEditorContainerEnabled(true);
            }

            // Show the appropriate editor
            var policyType = _policyValue.Definition.PolicyType;
            switch (policyType)
            {
                case SecurityPolicyType.Numeric:
                    ConfigureNumericEditor();
                    _activeEditorType = SecurityPolicyType.Numeric;
                    break;
                case SecurityPolicyType.Boolean:
                    ConfigureBooleanEditor();
                    _activeEditorType = SecurityPolicyType.Boolean;
                    break;
                case SecurityPolicyType.String:
                    ConfigureSingleStringEditor();
                    _activeEditorType = SecurityPolicyType.String;
                    break;
                case SecurityPolicyType.MultiString:
                    ConfigureMultiStringEditor();
                    _activeEditorType = SecurityPolicyType.MultiString;
                    break;
                case SecurityPolicyType.Audit:
                    ConfigureAuditEditor();
                    _activeEditorType = SecurityPolicyType.Audit;
                    break;
                case SecurityPolicyType.UserRightsAssignment:
                    ConfigureUserRightsEditor();
                    _activeEditorType = SecurityPolicyType.UserRightsAssignment;
                    break;
                case SecurityPolicyType.Dropdown:
                    if (_policyValue.Definition.DropdownOptions.Count == 0)
                    {
                        ManagementTools.Services.Logging.UiLogger.LogDebug($"[SecurityPolicyEditorDialog] Dropdown '{_policyValue.Definition.Key}' has no options; using fallback editor");
                        if (!string.IsNullOrEmpty(_policyValue.StringValue))
                        {
                            ConfigureSingleStringEditor();
                            _activeEditorType = SecurityPolicyType.String;
                        }
                        else
                        {
                            ConfigureNumericEditor();
                            _activeEditorType = SecurityPolicyType.Numeric;
                        }
                    }
                    else
                    {
                        ConfigureDropdownEditor();
                        _activeEditorType = SecurityPolicyType.Dropdown;
                    }
                    break;
                case SecurityPolicyType.BitmaskFlags:
                    ConfigureBitmaskFlagsEditor();
                    _activeEditorType = SecurityPolicyType.BitmaskFlags;
                    break;
            }
        }

        #region Editor Configuration

        private void ConfigureNumericEditor()
        {
            NumericEditor.Visibility = Visibility.Visible;
            NumericLabel.Text = _policyValue!.Definition.DisplayName;
            NumericValueBox.Minimum = _policyValue.Definition.MinValue;
            NumericValueBox.Maximum = _policyValue.Definition.MaxValue;
            NumericValueBox.Value = _policyValue.NumericValue;

            string unit = _policyValue.Definition.Unit;
            if (!string.IsNullOrEmpty(unit))
            {
                NumericUnitText.Text = $"({_policyValue.Definition.MinValue} - {_policyValue.Definition.MaxValue} {unit})";
                NumericUnitText.Visibility = Visibility.Visible;
            }
            else
            {
                NumericUnitText.Visibility = Visibility.Collapsed;
            }

            ManagementTools.Services.Logging.UiLogger.LogDebug($"[SecurityPolicyEditorDialog] Numeric editor: value={_policyValue.NumericValue}, range=[{_policyValue.Definition.MinValue}, {_policyValue.Definition.MaxValue}]");
        }

        private void ConfigureBooleanEditor()
        {
            BooleanEditor.Visibility = Visibility.Visible;
            EnabledRadio.IsChecked = _policyValue!.NumericValue != 0;
            DisabledRadio.IsChecked = _policyValue.NumericValue == 0;

            ManagementTools.Services.Logging.UiLogger.LogDebug($"[SecurityPolicyEditorDialog] Boolean editor: value={_policyValue.NumericValue}");
        }

        private void ConfigureSingleStringEditor()
        {
            StringEditor.Visibility = Visibility.Visible;
            StringValueBox.Text = _policyValue!.StringValue;

            ManagementTools.Services.Logging.UiLogger.LogDebug($"[SecurityPolicyEditorDialog] String editor: value='{_policyValue.StringValue}'");
        }

        private void ConfigureMultiStringEditor()
        {
            MultiStringEditor.Visibility = Visibility.Visible;
            MultiStringValueBox.Text = _policyValue!.StringValue;

            ManagementTools.Services.Logging.UiLogger.LogDebug($"[SecurityPolicyEditorDialog] MultiString editor: value length={_policyValue.StringValue?.Length ?? 0}");
        }

        private void ConfigureAuditEditor()
        {
            AuditEditor.Visibility = Visibility.Visible;
            var flags = (AuditPolicyFlags)_policyValue!.NumericValue;
            AuditSuccessCheck.IsChecked = flags.HasFlag(AuditPolicyFlags.Success);
            AuditFailureCheck.IsChecked = flags.HasFlag(AuditPolicyFlags.Failure);

            ManagementTools.Services.Logging.UiLogger.LogDebug($"[SecurityPolicyEditorDialog] Audit editor: flags={flags}");
        }

        private void ConfigureUserRightsEditor()
        {
            UserRightsEditor.Visibility = Visibility.Visible;
            _accounts.Clear();
            foreach (var account in _policyValue!.AccountList)
            {
                _accounts.Add(account);
            }
            AccountsList.ItemsSource = _accounts;

            ManagementTools.Services.Logging.UiLogger.LogDebug($"[SecurityPolicyEditorDialog] User rights editor: {_accounts.Count} accounts");
        }

        private void ConfigureDropdownEditor()
        {
            DropdownEditor.Visibility = Visibility.Visible;
            DropdownValueCombo.Items.Clear();

            int selectedIndex = -1;
            for (int i = 0; i < _policyValue!.Definition.DropdownOptions.Count; i++)
            {
                var option = _policyValue.Definition.DropdownOptions[i];
                DropdownValueCombo.Items.Add(new ComboBoxItem
                {
                    Content = option.DisplayName,
                    Tag = option.Value
                });

                // Match current value against option
                if (option.Value is long lv && lv == _policyValue.NumericValue)
                    selectedIndex = i;
                else if (option.Value is int iv && iv == _policyValue.NumericValue)
                    selectedIndex = i;
                else if (option.Value is string sv && sv == _policyValue.StringValue)
                    selectedIndex = i;
            }

            DropdownValueCombo.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;

            ManagementTools.Services.Logging.UiLogger.LogDebug($"[SecurityPolicyEditorDialog] Dropdown editor: {_policyValue.Definition.DropdownOptions.Count} options, selectedIndex={selectedIndex}");
        }

        private void ConfigureBitmaskFlagsEditor()
        {
            BitmaskFlagsEditor.Visibility = Visibility.Visible;
            BitmaskFlagsLabel.Text = _policyValue!.Definition.DisplayName;
            BitmaskFlagsOptionsPanel.Children.Clear();

            foreach (var option in _policyValue.Definition.DropdownOptions)
            {
                long flagValue;
                if (option.Value is long lv)
                    flagValue = lv;
                else if (option.Value is int iv)
                    flagValue = iv;
                else if (!long.TryParse(option.Value?.ToString(), out flagValue))
                    continue;

                BitmaskFlagsOptionsPanel.Children.Add(new CheckBox
                {
                    Content = option.DisplayName,
                    Tag = flagValue,
                    IsChecked = flagValue != 0 && (_policyValue.NumericValue & flagValue) == flagValue
                });
            }

            ManagementTools.Services.Logging.UiLogger.LogDebug($"[SecurityPolicyEditorDialog] Bitmask flags editor: value={_policyValue.NumericValue}, options={BitmaskFlagsOptionsPanel.Children.Count}");
        }

        #endregion

        #region Event Handlers

        private void EditorSelectorBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
        {
            var selectedItem = sender.SelectedItem;
            if (selectedItem?.Tag is string tag)
            {
                GeneralPanel.Visibility = tag == "General" ? Visibility.Visible : Visibility.Collapsed;
                ExplainPanel.Visibility = tag == "Explain" ? Visibility.Visible : Visibility.Collapsed;

                ManagementTools.Services.Logging.UiLogger.LogDebug($"[SecurityPolicyEditorDialog] Tab switched to: {tag}");
            }
        }

        private void DefinePolicyCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            bool isDefined = DefinePolicyCheckBox.IsChecked == true;
            EditorContainer.Opacity = isDefined ? 1.0 : 0.4;
            SetEditorContainerEnabled(isDefined);

            ManagementTools.Services.Logging.UiLogger.LogDebug($"[SecurityPolicyEditorDialog] Define policy checkbox changed: isDefined={isDefined}");
        }

        private void SetEditorContainerEnabled(bool enabled)
        {
            // Enable/disable all interactive controls in EditorContainer
            NumericValueBox.IsEnabled = enabled;
            EnabledRadio.IsEnabled = enabled;
            DisabledRadio.IsEnabled = enabled;
            StringValueBox.IsEnabled = enabled;
            MultiStringValueBox.IsEnabled = enabled;
            AuditSuccessCheck.IsEnabled = enabled;
            AuditFailureCheck.IsEnabled = enabled;
            DropdownValueCombo.IsEnabled = enabled;
            AccountsList.IsEnabled = enabled;
            AddAccountButton.IsEnabled = enabled;
            RemoveAccountButton.IsEnabled = enabled;
        }

        private void NumericValueBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            ValidationError.IsOpen = false;
        }

        private void AddAccountButton_Click(object sender, RoutedEventArgs e)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
            var selections = DirectoryObjectPickerService.ShowDialog(
                hwnd,
                ObjectPickerTypes.UsersAndGroups,
                multiSelect: true);

            if (selections is { Count: > 0 })
            {
                foreach (var obj in selections)
                {
                    if (!string.IsNullOrWhiteSpace(obj.Name) && !_accounts.Contains(obj.Name))
                    {
                        _accounts.Add(obj.Name);
                        ManagementTools.Services.Logging.UiLogger.LogDebug($"[SecurityPolicyEditorDialog] Added account: {obj.Name}");
                    }
                }
            }
        }

        private void RemoveAccountButton_Click(object sender, RoutedEventArgs e)
        {
            if (AccountsList.SelectedItem is string selected)
            {
                _accounts.Remove(selected);
                ManagementTools.Services.Logging.UiLogger.LogDebug($"[SecurityPolicyEditorDialog] Removed account: {selected}");
            }
        }

        private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            ManagementTools.Services.Logging.UiLogger.LogDebug("[SecurityPolicyEditorDialog] OK clicked, collecting values");

            if (_policyValue == null)
            {
                args.Cancel = true;
                return;
            }

            try
            {
                CollectCurrentValues();
                ManagementTools.Services.Logging.UiLogger.LogDebug($"[SecurityPolicyEditorDialog] Collected value for '{_policyValue.Definition.Key}', isDefined={_policyValue.IsDefined}");
            }
            catch (Exception ex)
            {
                ManagementTools.Services.Logging.UiLogger.LogDebug($"[SecurityPolicyEditorDialog] Validation error: {ex.Message}");
                ValidationError.Message = ex.Message;
                ValidationError.IsOpen = true;
                args.Cancel = true;
            }
        }

        private void CollectCurrentValues()
        {
            // Handle "Not Defined" state
            if (_allowNotDefined && DefinePolicyCheckBox.IsChecked != true)
            {
                _policyValue!.IsDefined = false;
                return;
            }

            _policyValue!.IsDefined = true;

            switch (_activeEditorType)
            {
                case SecurityPolicyType.Numeric:
                    CollectNumericValue();
                    break;
                case SecurityPolicyType.Boolean:
                    _policyValue.NumericValue = EnabledRadio.IsChecked == true ? 1 : 0;
                    break;
                case SecurityPolicyType.String:
                    _policyValue.StringValue = StringValueBox.Text;
                    break;
                case SecurityPolicyType.MultiString:
                    _policyValue.StringValue = MultiStringValueBox.Text;
                    break;
                case SecurityPolicyType.Audit:
                    CollectAuditValue();
                    break;
                case SecurityPolicyType.UserRightsAssignment:
                    _policyValue.AccountList = _accounts.ToList();
                    break;
                case SecurityPolicyType.Dropdown:
                    CollectDropdownValue();
                    break;
                case SecurityPolicyType.BitmaskFlags:
                    CollectBitmaskFlagsValue();
                    break;
            }
        }

        private void CollectNumericValue()
        {
            double rawValue = NumericValueBox.Value;
            if (double.IsNaN(rawValue))
            {
                throw new ArgumentException(LocalizedStrings.SecPol_Error_EnterValidNumber);
            }

            long longValue = (long)rawValue;
            if (longValue < _policyValue!.Definition.MinValue || longValue > _policyValue.Definition.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    null,
                    string.Format(LocalizedStrings.SecPol_Error_ValueOutOfRange,
                        _policyValue.Definition.MinValue, _policyValue.Definition.MaxValue));
            }

            _policyValue.NumericValue = longValue;
        }

        private void CollectAuditValue()
        {
            AuditPolicyFlags flags = AuditPolicyFlags.None;
            if (AuditSuccessCheck.IsChecked == true) flags |= AuditPolicyFlags.Success;
            if (AuditFailureCheck.IsChecked == true) flags |= AuditPolicyFlags.Failure;
            _policyValue!.NumericValue = (long)flags;
        }

        private void CollectDropdownValue()
        {
            if (DropdownValueCombo.SelectedItem is ComboBoxItem selected && selected.Tag != null)
            {
                if (selected.Tag is long lv)
                    _policyValue!.NumericValue = lv;
                else if (selected.Tag is int iv)
                    _policyValue!.NumericValue = iv;
                else if (selected.Tag is string sv)
                {
                    _policyValue!.StringValue = sv;
                    if (long.TryParse(sv, out long parsed))
                        _policyValue.NumericValue = parsed;
                }
            }
        }

        private void CollectBitmaskFlagsValue()
        {
            long flags = 0;

            foreach (var child in BitmaskFlagsOptionsPanel.Children)
            {
                if (child is CheckBox checkbox && checkbox.IsChecked == true)
                {
                    if (checkbox.Tag is long lv)
                        flags |= lv;
                    else if (checkbox.Tag is int iv)
                        flags |= unchecked((long)(uint)iv);
                }
            }

            _policyValue!.NumericValue = flags;
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Creates a deep clone of a SecurityPolicyValue for editing.
        /// </summary>
        private static SecurityPolicyValue ClonePolicyValue(SecurityPolicyValue source)
        {
            return new SecurityPolicyValue
            {
                Definition = source.Definition,
                NumericValue = source.NumericValue,
                StringValue = source.StringValue,
                IsDefined = source.IsDefined,
                AccountList = new System.Collections.Generic.List<string>(source.AccountList)
            };
        }

        #endregion
    }
}

