using System.Collections.ObjectModel;
using OneMMC.Core.Features.UserSecurity.Models.SecPol.IPSecurity;
using OneMMC.Core.Features.UserSecurity.Services.SecPol.IPSecurity;
using OneMMC.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OneMMC.Views.UserSecurity.SecPol.IPSecurity.Editors;

/// <summary>
/// Edits a legacy static IPsec rule and its ordered authentication methods without executing commands.
/// </summary>
public sealed partial class IPSecurityRuleEditorControl : UserControl
{
    private readonly IPSecurityEditorMode _mode;
    private readonly string _originalName;
    private readonly string _policyName;
    private readonly bool _originallyUsedTunnel;
    private readonly IPSecurityRuleDefinition? _rule;

    /// <summary>Gets localized strings used by the control.</summary>
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    /// <summary>Gets the ordered authentication methods being edited.</summary>
    internal ObservableCollection<IPSecurityAuthenticationEditorItem> AuthenticationItems { get; } = [];

    /// <summary>
    /// Initializes a rule editor.
    /// </summary>
    /// <param name="mode">The editor mode.</param>
    /// <param name="policyName">The policy that owns the rule.</param>
    /// <param name="filterListNames">Available filter-list names.</param>
    /// <param name="filterActionNames">Available filter-action names.</param>
    /// <param name="rule">The rule to edit, or <see langword="null"/> when creating one.</param>
    public IPSecurityRuleEditorControl(
        IPSecurityEditorMode mode,
        string policyName,
        IEnumerable<string> filterListNames,
        IEnumerable<string> filterActionNames,
        IPSecurityRuleDefinition? rule = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyName);
        ArgumentNullException.ThrowIfNull(filterListNames);
        ArgumentNullException.ThrowIfNull(filterActionNames);
        if (mode == IPSecurityEditorMode.Edit)
        {
            ArgumentNullException.ThrowIfNull(rule);
        }

        _mode = mode;
        _originalName = rule?.Name ?? string.Empty;
        _policyName = policyName;
        _rule = rule;
        _originallyUsedTunnel = !string.IsNullOrWhiteSpace(rule?.TunnelEndpoint);

        InitializeComponent();
        NameTextBox.Text = rule?.Name ?? string.Empty;
        DescriptionTextBox.Text = rule?.Description ?? string.Empty;
        PopulateReferenceComboBox(FilterListComboBox, filterListNames, rule?.FilterListName);
        PopulateReferenceComboBox(FilterActionComboBox, filterActionNames, rule?.FilterActionName);
        ConnectionTypeComboBox.SelectedIndex = ParseConnectionTypeIndex(rule?.ConnectionType);
        ActiveToggleSwitch.IsOn = rule?.IsActive ?? true;
        UseTunnelToggleSwitch.IsOn = _originallyUsedTunnel;
        TunnelEndpointTextBox.Text = rule?.TunnelEndpoint ?? string.Empty;
        bool isDefaultResponseRule = rule?.IsDefaultResponseRule == true;
        NormalRulePanel.Visibility = isDefaultResponseRule ? Visibility.Collapsed : Visibility.Visible;
        DefaultResponseSecurityPanel.Visibility = isDefaultResponseRule ? Visibility.Visible : Visibility.Collapsed;
        DefaultResponsePfsToggleSwitch.IsOn = rule?.FilterAction?.UseQuickModePerfectForwardSecrecy ?? false;
        DefaultResponseMethodsEditor.SetMethods(rule?.FilterAction?.QuickModeSecurityMethods ?? []);

        foreach (IPSecurityAuthenticationMethodDefinition method in rule?.AuthenticationMethods ?? [])
        {
            AuthenticationItems.Add(CreateAuthenticationItem(method));
        }

        AuthenticationKindComboBox.SelectedIndex = 0;
        UpdateTunnelState();
        UpdateAuthenticationEditorState();
        AuthenticationItems.CollectionChanged += (_, _) => UpdateEmptyState();
        UpdateEmptyState();
    }

    /// <summary>
    /// Shows or hides the authentication list's empty state. Driven from code-behind so the
    /// collection stays a plain <see cref="ObservableCollection{T}"/> with no wrapper view model.
    /// </summary>
    private void UpdateEmptyState()
    {
        EmptyAuthenticationText.Visibility =
            AuthenticationItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Builds and validates command options for the current rule values.
    /// </summary>
    /// <param name="options">The validated options, or <see langword="null"/> when validation fails.</param>
    /// <returns><see langword="true"/> when the options are valid.</returns>
    public bool TryBuildResult(out IPSecurityRuleCommandOptions? options)
    {
        if (!TryBuildAuthenticationMethods(out IReadOnlyList<IPSecurityRuleAuthenticationCommand>? methods))
        {
            options = null;
            return false;
        }

        string currentName = NameTextBox.Text;
        bool isDefaultResponseRule = _rule?.IsDefaultResponseRule == true;
        options = new IPSecurityRuleCommandOptions
        {
            Identifier = _rule?.Identifier,
            Name = _mode == IPSecurityEditorMode.Create ? currentName : _originalName,
            PolicyName = _policyName,
            NewName = IPSecurityEditorValidation.RenamedValue(_mode, _originalName, currentName),
            Description = isDefaultResponseRule ? null : DescriptionTextBox.Text,
            FilterListName = isDefaultResponseRule ? null : FilterListComboBox.SelectedItem as string,
            FilterActionName = isDefaultResponseRule ? null : FilterActionComboBox.SelectedItem as string,
            TunnelEndpoint = isDefaultResponseRule ? null : GetTunnelEndpoint(),
            ConnectionType = isDefaultResponseRule ? null : GetConnectionType(),
            IsActive = isDefaultResponseRule ? null : ActiveToggleSwitch.IsOn,
            AuthenticationMethods = methods,
            UseQuickModePerfectForwardSecrecy = isDefaultResponseRule
                ? DefaultResponsePfsToggleSwitch.IsOn
                : null,
            QuickModeSecurityMethods = isDefaultResponseRule
                ? DefaultResponseMethodsEditor.GetMethods()
                : null
        };

        IPSecurityRuleCommandOptions candidate = options;
        bool isValid = IPSecurityEditorValidation.TryValidate(
            () =>
            {
                if (_mode == IPSecurityEditorMode.Create)
                {
                    _ = IPSecurityCommandBuilder.BuildAddRule(candidate);
                }
                else
                {
                    _ = IPSecurityCommandBuilder.BuildSetRule(candidate);
                }
            },
            ValidationInfoBar,
            LocalizedStrings.IPSec_Editor_ValidationInvalid);
        if (!isValid)
        {
            options = null;
        }

        return isValid;
    }

    private void UseTunnelToggleSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        UpdateTunnelState();
    }

    private void AuthenticationKindComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateAuthenticationEditorState();
    }

    private void AuthenticationListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AuthenticationListView.SelectedItem is not IPSecurityAuthenticationEditorItem selected)
        {
            return;
        }

        AuthenticationKindComboBox.SelectedIndex = selected.Kind switch
        {
            IPSecurityAuthenticationMethodKind.CertificateAuthority => 1,
            IPSecurityAuthenticationMethodKind.PreSharedKey => 2,
            _ => 0
        };
        CertificateAuthorityNameTextBox.Text = selected.Kind == IPSecurityAuthenticationMethodKind.CertificateAuthority
            ? selected.Detail
            : string.Empty;
        CertificateMappingCheckBox.IsChecked = selected.EnableCertificateToAccountMapping;
        ExcludeCertificateAuthorityNameCheckBox.IsChecked = selected.ExcludeCertificateAuthorityName;
        PreSharedKeyPasswordBox.Password = string.Empty;
        UpdateAuthenticationEditorState();
    }

    private void AddOrReplaceAuthenticationButton_Click(object sender, RoutedEventArgs e)
    {
        IPSecurityAuthenticationMethodKind kind = GetAuthenticationKind();
        IPSecurityAuthenticationEditorItem? selected = AuthenticationListView.SelectedItem
            as IPSecurityAuthenticationEditorItem;

        string preSharedKey = PreSharedKeyPasswordBox.Password;
        bool preserveSelectedPreSharedKey = kind == IPSecurityAuthenticationMethodKind.PreSharedKey
            && selected?.Kind == IPSecurityAuthenticationMethodKind.PreSharedKey
            && !selected.RequiresPreSharedKeyReentry
            && string.IsNullOrEmpty(preSharedKey);
        if (preserveSelectedPreSharedKey)
        {
            preSharedKey = selected!.PreSharedKey;
        }

        if (kind == IPSecurityAuthenticationMethodKind.PreSharedKey && string.IsNullOrEmpty(preSharedKey))
        {
            ShowValidation(LocalizedStrings.IPSec_Editor_PskReentryRequired);
            return;
        }

        var item = new IPSecurityAuthenticationEditorItem
        {
            Kind = kind,
            DisplayName = GetAuthenticationDisplayName(kind),
            Detail = kind == IPSecurityAuthenticationMethodKind.CertificateAuthority
                ? CertificateAuthorityNameTextBox.Text
                : kind == IPSecurityAuthenticationMethodKind.PreSharedKey
                    ? LocalizedStrings.IPSec_Editor_PskConfigured
                    : string.Empty,
            EnableCertificateToAccountMapping =
                kind == IPSecurityAuthenticationMethodKind.CertificateAuthority
                && CertificateMappingCheckBox.IsChecked == true,
            ExcludeCertificateAuthorityName =
                kind == IPSecurityAuthenticationMethodKind.CertificateAuthority
                && ExcludeCertificateAuthorityNameCheckBox.IsChecked == true,
            PreSharedKey = kind == IPSecurityAuthenticationMethodKind.PreSharedKey
                ? preSharedKey
                : string.Empty
        };

        if (selected is null)
        {
            AuthenticationItems.Add(item);
            AuthenticationListView.SelectedItem = item;
        }
        else
        {
            int index = AuthenticationItems.IndexOf(selected);
            AuthenticationItems[index] = item;
            AuthenticationListView.SelectedIndex = index;
        }

        ValidationInfoBar.IsOpen = false;
    }

    private void MoveAuthenticationUpButton_Click(object sender, RoutedEventArgs e)
    {
        MoveSelectedAuthentication(-1);
    }

    private void MoveAuthenticationDownButton_Click(object sender, RoutedEventArgs e)
    {
        MoveSelectedAuthentication(1);
    }

    private void DeleteAuthenticationButton_Click(object sender, RoutedEventArgs e)
    {
        if (AuthenticationListView.SelectedItem is IPSecurityAuthenticationEditorItem selected)
        {
            AuthenticationItems.Remove(selected);
        }
    }

    private void MoveSelectedAuthentication(int offset)
    {
        if (AuthenticationListView.SelectedItem is not IPSecurityAuthenticationEditorItem selected)
        {
            return;
        }

        int oldIndex = AuthenticationItems.IndexOf(selected);
        int newIndex = oldIndex + offset;
        if (newIndex < 0 || newIndex >= AuthenticationItems.Count)
        {
            return;
        }

        AuthenticationItems.Move(oldIndex, newIndex);
        AuthenticationListView.SelectedIndex = newIndex;
    }

    private bool TryBuildAuthenticationMethods(
        out IReadOnlyList<IPSecurityRuleAuthenticationCommand>? methods)
    {
        List<IPSecurityRuleAuthenticationCommand> result = [];
        IPSecurityAuthenticationEditorItem? selected = AuthenticationListView.SelectedItem
            as IPSecurityAuthenticationEditorItem;
        foreach (IPSecurityAuthenticationEditorItem item in AuthenticationItems)
        {
            switch (item.Kind)
            {
                case IPSecurityAuthenticationMethodKind.Kerberos:
                    result.Add(new IPSecurityKerberosAuthenticationCommand());
                    break;
                case IPSecurityAuthenticationMethodKind.CertificateAuthority:
                    result.Add(new IPSecurityCertificateAuthenticationCommand
                    {
                        CertificateAuthorityName = item.Detail,
                        EnableCertificateToAccountMapping = item.EnableCertificateToAccountMapping,
                        ExcludeCertificateAuthorityName = item.ExcludeCertificateAuthorityName
                    });
                    break;
                case IPSecurityAuthenticationMethodKind.PreSharedKey:
                    string preSharedKey = item.PreSharedKey;
                    if (ReferenceEquals(item, selected) && !string.IsNullOrEmpty(PreSharedKeyPasswordBox.Password))
                    {
                        preSharedKey = PreSharedKeyPasswordBox.Password;
                    }

                    if (string.IsNullOrEmpty(preSharedKey))
                    {
                        ShowValidation(LocalizedStrings.IPSec_Editor_PskReentryRequired);
                        methods = null;
                        return false;
                    }

                    result.Add(new IPSecurityPreSharedKeyAuthenticationCommand(preSharedKey));
                    break;
            }
        }

        methods = result;
        return true;
    }

    private IPSecurityAuthenticationEditorItem CreateAuthenticationItem(
        IPSecurityAuthenticationMethodDefinition method)
    {
        bool requiresPskReentry = method.Kind == IPSecurityAuthenticationMethodKind.PreSharedKey;
        return new IPSecurityAuthenticationEditorItem
        {
            Kind = method.Kind,
            DisplayName = GetAuthenticationDisplayName(method.Kind),
            Detail = requiresPskReentry
                ? LocalizedStrings.IPSec_Editor_PskReentryRequired
                : method.Detail,
            EnableCertificateToAccountMapping = method.EnableCertificateToAccountMapping,
            ExcludeCertificateAuthorityName = method.ExcludeCertificateAuthorityName,
            RequiresPreSharedKeyReentry = requiresPskReentry
        };
    }

    private void UpdateTunnelState()
    {
        TunnelEndpointTextBox.IsEnabled = UseTunnelToggleSwitch.IsOn;
    }

    private void UpdateAuthenticationEditorState()
    {
        IPSecurityAuthenticationMethodKind kind = GetAuthenticationKind();
        CertificateAuthenticationPanel.Visibility =
            kind == IPSecurityAuthenticationMethodKind.CertificateAuthority
                ? Visibility.Visible
                : Visibility.Collapsed;
        PreSharedKeyAuthenticationPanel.Visibility =
            kind == IPSecurityAuthenticationMethodKind.PreSharedKey
                ? Visibility.Visible
                : Visibility.Collapsed;

        IPSecurityAuthenticationEditorItem? selected = AuthenticationListView.SelectedItem
            as IPSecurityAuthenticationEditorItem;
        PreSharedKeyStatusTextBlock.Text =
            selected?.Kind == IPSecurityAuthenticationMethodKind.PreSharedKey
                && selected.RequiresPreSharedKeyReentry
                    ? LocalizedStrings.IPSec_Editor_PskReentryRequired
                    : LocalizedStrings.IPSec_Editor_PskConfigured;
    }

    private string? GetTunnelEndpoint()
    {
        if (UseTunnelToggleSwitch.IsOn)
        {
            return TunnelEndpointTextBox.Text;
        }

        return _mode == IPSecurityEditorMode.Edit && _originallyUsedTunnel ? "no" : null;
    }

    private IPSecurityRuleConnectionType GetConnectionType()
    {
        return ConnectionTypeComboBox.SelectedIndex switch
        {
            1 => IPSecurityRuleConnectionType.Lan,
            2 => IPSecurityRuleConnectionType.DialUp,
            _ => IPSecurityRuleConnectionType.All
        };
    }

    private IPSecurityAuthenticationMethodKind GetAuthenticationKind()
    {
        return AuthenticationKindComboBox.SelectedIndex switch
        {
            1 => IPSecurityAuthenticationMethodKind.CertificateAuthority,
            2 => IPSecurityAuthenticationMethodKind.PreSharedKey,
            _ => IPSecurityAuthenticationMethodKind.Kerberos
        };
    }

    private string GetAuthenticationDisplayName(IPSecurityAuthenticationMethodKind kind)
    {
        return kind switch
        {
            IPSecurityAuthenticationMethodKind.CertificateAuthority =>
                LocalizedStrings.IPSec_Editor_AuthenticationCertificate,
            IPSecurityAuthenticationMethodKind.PreSharedKey =>
                LocalizedStrings.IPSec_Editor_AuthenticationPsk,
            _ => LocalizedStrings.IPSec_Editor_AuthenticationKerberos
        };
    }

    private void ShowValidation(string message)
    {
        ValidationInfoBar.Message = message;
        ValidationInfoBar.IsOpen = true;
    }

    private static int ParseConnectionTypeIndex(string? connectionType)
    {
        return connectionType?.ToLowerInvariant() switch
        {
            "lan" => 1,
            "dialup" => 2,
            _ => 0
        };
    }

    private static void PopulateReferenceComboBox(
        ComboBox comboBox,
        IEnumerable<string> values,
        string? selectedValue)
    {
        List<string> items = values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(static value => value, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (!string.IsNullOrWhiteSpace(selectedValue)
            && !items.Contains(selectedValue, StringComparer.CurrentCultureIgnoreCase))
        {
            items.Add(selectedValue);
        }

        comboBox.ItemsSource = items;
        comboBox.SelectedItem = selectedValue;
        if (comboBox.SelectedIndex < 0 && items.Count > 0)
        {
            comboBox.SelectedIndex = 0;
        }
    }
}
