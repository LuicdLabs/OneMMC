// ============================================================================
// StorePropertiesDialog.xaml.cs
// 
// Store Properties Dialog - Edit authorization store properties and security settings
// ============================================================================

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ManagementTools.Localization;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ManagementTools.Core.Features.UserSecurity.Models.AzMan;
using ManagementTools.Core.Features.UserSecurity.Services.AzMan;

namespace ManagementTools.Views.UserSecurity.AzMan.Dialogs;

/// <summary>
/// Authorization rule mode for limits tab.
/// </summary>
public enum AuthorizationRulesMode
{
    Disable,
    EnableNoTimeout,
    EnableSpecifiedTimeout
}

/// <summary>
/// Store properties change result
/// </summary>
public class StorePropertiesResult
{
    public string Description { get; set; } = string.Empty;
    public string ApplicationData { get; set; } = string.Empty;
    public bool GenerateAudits { get; set; }
    public bool UpgradeSchemaToV2 { get; set; }

    public AuthorizationRulesMode AuthorizationRulesMode { get; set; } = AuthorizationRulesMode.EnableSpecifiedTimeout;
    public int AuthorizationRuleTimeout { get; set; }
    public int MaximumCachedAuthorizationRules { get; set; }
    public int LdapQueryTimeout { get; set; }
    public bool UseDefaultValues { get; set; }

    public bool RuntimeApplicationInitializationAuditing { get; set; }
    public bool AuthorizationStoreChangeAuditing { get; set; }

    public List<string> AddedPolicyAdmins { get; set; } = [];
    public List<string> RemovedPolicyAdmins { get; set; } = [];
    public List<string> AddedPolicyReaders { get; set; } = [];
    public List<string> RemovedPolicyReaders { get; set; } = [];
}

/// <summary>
/// Store Properties Dialog
/// </summary>
public sealed partial class StorePropertiesDialog : ContentDialog
{
    public const int DefaultAuthorizationRuleTimeout = 45000;
    public const int DefaultMaxCachedAuthorizationRules = 120;
    public const int DefaultLdapQueryTimeout = 15000;

    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    private readonly AzAuthorizationStoreInfo _store;
    private readonly StoreAdvancedProperties _advancedProperties;
    private readonly ObservableCollection<string> _policyAdmins = [];
    private readonly ObservableCollection<string> _policyReaders = [];
    private readonly HashSet<string> _originalPolicyAdmins;
    private readonly HashSet<string> _originalPolicyReaders;
    private bool _upgradeSchemaRequested;
    private bool _useDefaultValuesRequested;
    private TaskCompletionSource<bool>? _reopenTcs;
    private bool _reopenRequested;
    private readonly LocalizedStrings _localizedStrings = LocalizedStrings.Instance;

    /// <summary>
    /// Change result
    /// </summary>
    public StorePropertiesResult? Result { get; private set; }

    /// <summary>
    /// Whether dialog needs to be re-opened by caller
    /// </summary>
    internal bool ReopenRequested => _reopenRequested;

    /// <summary>
    /// Create dialog
    /// </summary>
    public StorePropertiesDialog(AzAuthorizationStoreInfo store, StoreAdvancedProperties? advancedProperties = null)
    {
        InitializeComponent();
        this.RequestedTheme = App.CurrentTheme;
        _store = store;
        _advancedProperties = advancedProperties ?? new StoreAdvancedProperties();

        _originalPolicyAdmins = new HashSet<string>(store.PolicyAdministrators);
        _originalPolicyReaders = new HashSet<string>(store.PolicyReaders);

        LoadData();
    }

    /// <summary>
    /// Load data
    /// </summary>
    private void LoadData()
    {
        StorePathTextBox.Text = _store.StorePath;
        StoreTypeTextBox.Text = _store.StoreType switch
        {
            AzStoreType.Xml => LocalizedStrings.StorePropertiesDialog_StoreType_Xml,
            AzStoreType.ActiveDirectory => LocalizedStrings.StorePropertiesDialog_StoreType_ActiveDirectory,
            AzStoreType.SqlServer => LocalizedStrings.StorePropertiesDialog_StoreType_SqlServer,
            _ => LocalizedStrings.StorePropertiesDialog_StoreType_Unknown
        };

        VersionTextBox.Text = _store.VersionString;
        DescriptionTextBox.Text = _store.Description;
        ApplicationDataTextBox.Text = _store.ApplicationData;

        SchemaUpgradePanel.Visibility = _store.MajorVersion < 2 ? Visibility.Visible : Visibility.Collapsed;
        _upgradeSchemaRequested = false;
        SchemaUpgradeStatusTextBlock.Text = string.Empty;

        _useDefaultValuesRequested = false;
        UseDefaultValuesStatusTextBlock.Text = string.Empty;

        foreach (var admin in _store.PolicyAdministrators)
        {
            _policyAdmins.Add(admin);
        }
        PolicyAdminsListView.ItemsSource = _policyAdmins;

        foreach (var reader in _store.PolicyReaders)
        {
            _policyReaders.Add(reader);
        }
        PolicyReadersListView.ItemsSource = _policyReaders;

        var rawTimeout = _advancedProperties.ScriptEngineTimeout;
        var rawMaxCached = _advancedProperties.MaxScriptEngines;
        var timeout = rawTimeout ?? DefaultAuthorizationRuleTimeout;
        var maxCached = rawMaxCached ?? DefaultMaxCachedAuthorizationRules;
        var ldapTimeout = _advancedProperties.DomainTimeout ?? DefaultLdapQueryTimeout;

        AuthorizationRuleTimeoutNumberBox.Value = timeout;
        MaximumCachedAuthorizationRulesNumberBox.Value = maxCached;
        LdapQueryTimeoutNumberBox.Value = ldapTimeout;

        if (rawTimeout.HasValue && rawTimeout.Value <= 0)
        {
            AuthorizationRulesModeComboBox.SelectedIndex = rawMaxCached.HasValue && rawMaxCached.Value > 0
                ? 1
                : 0;
        }
        else
        {
            AuthorizationRulesModeComboBox.SelectedIndex = 2;
        }

        RuntimeApplicationInitializationAuditingCheckBox.IsChecked = _advancedProperties.RuntimeApplicationInitializationAuditing
            ?? _store.GenerateAudits;
        AuthorizationStoreChangeAuditingCheckBox.IsChecked = _advancedProperties.AuthorizationStoreChangeAuditing
            ?? false;
    }

    /// <summary>
    /// Add policy administrator
    /// </summary>
    private void OnAddPolicyAdminClick(object sender, RoutedEventArgs e)
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
                if (!_policyAdmins.Contains(obj.Name))
                {
                    _policyAdmins.Add(obj.Name);
                }
            }
        }
    }

    /// <summary>
    /// Remove policy administrator
    /// </summary>
    private void OnRemovePolicyAdminClick(object sender, RoutedEventArgs e)
    {
        var selectedItems = PolicyAdminsListView.SelectedItems.Cast<string>().ToList();
        foreach (var item in selectedItems)
        {
            _policyAdmins.Remove(item);
        }
    }

    /// <summary>
    /// Add policy reader
    /// </summary>
    private void OnAddPolicyReaderClick(object sender, RoutedEventArgs e)
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
                if (!_policyReaders.Contains(obj.Name))
                {
                    _policyReaders.Add(obj.Name);
                }
            }
        }
    }

    /// <summary>
    /// Remove policy reader
    /// </summary>
    private void OnRemovePolicyReaderClick(object sender, RoutedEventArgs e)
    {
        var selectedItems = PolicyReadersListView.SelectedItems.Cast<string>().ToList();
        foreach (var item in selectedItems)
        {
            _policyReaders.Remove(item);
        }
    }

    private void AuthorizationRulesModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var mode = GetSelectedAuthorizationRulesMode();
        if (AuthorizationRuleTimeoutNumberBox != null)
        {
            AuthorizationRuleTimeoutNumberBox.IsEnabled = mode == AuthorizationRulesMode.EnableSpecifiedTimeout;
        }

        if (MaximumCachedAuthorizationRulesNumberBox != null)
        {
            MaximumCachedAuthorizationRulesNumberBox.IsEnabled = mode != AuthorizationRulesMode.Disable;
        }
    }

    private void OnUseDefaultValuesButtonClick(object sender, RoutedEventArgs e)
    {
        _useDefaultValuesRequested = true;
        UseDefaultValuesStatusTextBlock.Text = _localizedStrings.StorePropertiesDialog_DefaultsSelected;

        AuthorizationRuleTimeoutNumberBox.Value = DefaultAuthorizationRuleTimeout;
        MaximumCachedAuthorizationRulesNumberBox.Value = DefaultMaxCachedAuthorizationRules;
        LdapQueryTimeoutNumberBox.Value = DefaultLdapQueryTimeout;

        AuthorizationRuleTimeoutNumberBox.IsEnabled = GetSelectedAuthorizationRulesMode() == AuthorizationRulesMode.EnableSpecifiedTimeout;
        MaximumCachedAuthorizationRulesNumberBox.IsEnabled = GetSelectedAuthorizationRulesMode() != AuthorizationRulesMode.Disable;
        LdapQueryTimeoutNumberBox.IsEnabled = true;
    }

    private void OnUpgradeSchemaButtonClick(object sender, RoutedEventArgs e)
    {
        _upgradeSchemaRequested = true;
        SchemaUpgradeStatusTextBlock.Text = LocalizedStrings.StorePropertiesDialog_UpgradeSelected;
        UpgradeSchemaButton.IsEnabled = false;
    }

    /// <summary>
    /// Primary button click
    /// </summary>
    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var currentPolicyAdmins = new HashSet<string>(_policyAdmins);
        var currentPolicyReaders = new HashSet<string>(_policyReaders);

        var mode = GetSelectedAuthorizationRulesMode();
        var authTimeout = (int)AuthorizationRuleTimeoutNumberBox.Value;
        var maxCached = (int)MaximumCachedAuthorizationRulesNumberBox.Value;
        var ldapTimeout = (int)LdapQueryTimeoutNumberBox.Value;

        if (mode == AuthorizationRulesMode.EnableSpecifiedTimeout && authTimeout <= 0)
        {
            ErrorInfoBar.Message = LocalizedStrings.StorePropertiesDialog_Error_AuthRuleTimeout;
            ErrorInfoBar.IsOpen = true;
            args.Cancel = true;
            return;
        }

        ErrorInfoBar.IsOpen = false;

        var runtimeAudit = RuntimeApplicationInitializationAuditingCheckBox.IsChecked == true;
        var storeChangeAudit = AuthorizationStoreChangeAuditingCheckBox.IsChecked == true;

        Result = new StorePropertiesResult
        {
            Description = DescriptionTextBox.Text.Trim(),
            ApplicationData = ApplicationDataTextBox.Text.Trim(),
            GenerateAudits = runtimeAudit,
            UpgradeSchemaToV2 = _upgradeSchemaRequested,
            AuthorizationRulesMode = mode,
            AuthorizationRuleTimeout = authTimeout,
            MaximumCachedAuthorizationRules = maxCached,
            LdapQueryTimeout = ldapTimeout,
            UseDefaultValues = _useDefaultValuesRequested,
            RuntimeApplicationInitializationAuditing = runtimeAudit,
            AuthorizationStoreChangeAuditing = storeChangeAudit,
            AddedPolicyAdmins = currentPolicyAdmins.Except(_originalPolicyAdmins).ToList(),
            RemovedPolicyAdmins = _originalPolicyAdmins.Except(currentPolicyAdmins).ToList(),
            AddedPolicyReaders = currentPolicyReaders.Except(_originalPolicyReaders).ToList(),
            RemovedPolicyReaders = _originalPolicyReaders.Except(currentPolicyReaders).ToList()
        };
    }

    private AuthorizationRulesMode GetSelectedAuthorizationRulesMode()
    {
        return AuthorizationRulesModeComboBox.SelectedIndex switch
        {
            0 => AuthorizationRulesMode.Disable,
            1 => AuthorizationRulesMode.EnableNoTimeout,
            _ => AuthorizationRulesMode.EnableSpecifiedTimeout
        };
    }

    private void StorePropertiesSelectorBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        GeneralPanel.Visibility = Visibility.Collapsed;
        SecurityPanel.Visibility = Visibility.Collapsed;
        LimitsPanel.Visibility = Visibility.Collapsed;
        AuditingPanel.Visibility = Visibility.Collapsed;

        if (sender.SelectedItem is SelectorBarItem { Tag: string tag })
        {
            switch (tag)
            {
                case "General":
                    GeneralPanel.Visibility = Visibility.Visible;
                    break;
                case "Security":
                    SecurityPanel.Visibility = Visibility.Visible;
                    break;
                case "Limits":
                    LimitsPanel.Visibility = Visibility.Visible;
                    break;
                case "Auditing":
                    AuditingPanel.Visibility = Visibility.Visible;
                    break;
            }
        }
    }

    private void BeginReopen()
    {
        _reopenRequested = true;
        _reopenTcs = new TaskCompletionSource<bool>();
        Hide();
    }

    private void EndReopen()
    {
        _reopenTcs?.TrySetResult(true);
    }

    internal async Task WaitForReopenAsync()
    {
        if (_reopenTcs != null)
        {
            await _reopenTcs.Task;
        }

        _reopenRequested = false;
        _reopenTcs = null;
    }
}
