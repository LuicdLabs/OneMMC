using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Authentication;
using ManagementTools.Core.Features.SystemManagement.Models.WF.ConnectionSecurity;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Monitoring;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Profiles;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Rules;
using ManagementTools.Views.Dialogs.Authentication;
using ManagementTools.Core.Features.SystemManagement.Services.WF.ConnectionSecurity;
using ManagementTools.Core.Features.SystemManagement.Services.WF.Monitoring;
using ManagementTools.Core.Features.SystemManagement.Services.WF.Profiles;
using ManagementTools.Core.Features.SystemManagement.Services.WF.Rules;
using ManagementTools.Core.Features.SystemManagement.Infrastructure.WF;
using ManagementTools.Localization;
using ManagementTools.Services;
using ManagementTools.Helpers;
using ManagementTools.Views.Dialogs.ConnectionSecurity;
using ManagementTools.Views.Dialogs.Network;
using ManagementTools.Views.Dialogs.Scope;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace ManagementTools.Views;

public sealed partial class FirewallRuleInfoPage : Page
{
    private const int NetFwAuthenticateNone = 0;
    private const int NetFwAuthenticateNoEncapsulation = 1;
    private const int NetFwAuthenticateWithIntegrity = 2;
    private const int NetFwAuthenticateAndNegotiateEncryption = 3;
    private const int NetFwAuthenticateAndEncrypt = 4;

    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;
    public FirewallRuleModel Rule { get; private set; } = new();
    private readonly ILogger<FirewallRuleInfoPage> _logger;
    private IDisposable? _firewallChangeSubscription;
    private ConnectionSecurityRuleModel? _connectionSecurityRule;
    private int _allowIfSecureSecureFlags = NetFwAuthenticateWithIntegrity;
    private bool _allowIfSecureOverrideBlockRules;
    private bool _isRefreshingFromSystemChange;
    private bool _isPopulatingRuleUi;
    private bool _isSynchronizingProtocolNumber;

    // Unsaved-changes guard: set when the user edits the rule (Rule.PropertyChanged outside of load /
    // system-refresh), cleared on load and after a successful Apply; prompts on back-navigation.
    private FirewallRuleModel? _trackedRule;
    private bool _hasUnsavedChanges;
    private bool _bypassNavGuard;

    public FirewallRuleInfoPage()
    {
        _logger = App.GetRequiredService<ILogger<FirewallRuleInfoPage>>();
        InitializeComponent();
        _firewallChangeSubscription = App.GetRequiredService<WindowsFirewallRuleChangeService>()
            .Subscribe(OnFirewallRulesChanged);
        Unloaded += OnUnloaded;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ApplyButton.IsEnabled = true;
        DisableButton.IsEnabled = true;
        DeleteMenuItem.IsEnabled = true;
        PredefinedRuleInfoBar.IsOpen = false;

        if (e.Parameter is ConnectionSecurityRuleModel connectionSecurityRule)
        {
            _connectionSecurityRule = connectionSecurityRule;
            Rule = CreateFirewallRuleModel(connectionSecurityRule);
            TrackRuleChanges(Rule);
            RestoreRuleAvailabilityUi();
            _isPopulatingRuleUi = true;
            try
            {
                ApplyDirectionUI(FirewallRuleDirection.ConnectionSecurity);
                PopulateComboBoxes(Rule);
                PopulateConnectionSecurityFields(connectionSecurityRule);
                InitializeAllowIfSecureState();
            }
            finally
            {
                _isPopulatingRuleUi = false;
            }

            UpdateDisableButton();
            _hasUnsavedChanges = false;
            return;
        }

        if (e.Parameter is FirewallRuleModel rule)
        {
            _connectionSecurityRule = null;
            Rule = rule;
            TrackRuleChanges(Rule);
            RestoreRuleAvailabilityUi();
            _isPopulatingRuleUi = true;
            try
            {
                ApplyDirectionUI(rule.Direction);
                PopulateComboBoxes(rule);
                InitializeAllowIfSecureState();
            }
            finally
            {
                _isPopulatingRuleUi = false;
            }

            UpdateDisableButton();
            ApplyPredefinedRuleUI(rule);
            _hasUnsavedChanges = false;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _firewallChangeSubscription?.Dispose();
        _firewallChangeSubscription = null;
        if (_trackedRule is not null)
        {
            _trackedRule.PropertyChanged -= OnRulePropertyChanged;
            _trackedRule = null;
        }
        Unloaded -= OnUnloaded;
    }

    // ----- Unsaved-changes guard -----

    private void TrackRuleChanges(FirewallRuleModel rule)
    {
        if (ReferenceEquals(_trackedRule, rule))
        {
            return;
        }
        if (_trackedRule is not null)
        {
            _trackedRule.PropertyChanged -= OnRulePropertyChanged;
        }
        _trackedRule = rule;
        _trackedRule.PropertyChanged += OnRulePropertyChanged;
    }

    private void OnRulePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Ignore programmatic edits made while loading the page or refreshing from a system change.
        if (!_isPopulatingRuleUi && !_isRefreshingFromSystemChange)
        {
            _hasUnsavedChanges = true;
        }
    }

    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        base.OnNavigatingFrom(e);
        if (_bypassNavGuard || !_hasUnsavedChanges)
        {
            return;
        }
        e.Cancel = true;
        _ = ResolveUnsavedChangesAsync(e.NavigationMode, e.SourcePageType, e.Parameter);
    }

    private async Task ResolveUnsavedChangesAsync(NavigationMode mode, Type? sourcePageType, object? parameter)
    {
        var choice = await UnsavedChangesPrompt.ShowAsync(this.XamlRoot);
        if (choice == UnsavedChangesChoice.Cancel)
        {
            return;
        }
        if (choice == UnsavedChangesChoice.Save && !await SaveAsync())
        {
            return; // save failed (e.g. needs elevation or validation) — stay on the page
        }
        _bypassNavGuard = true;
        if (mode == NavigationMode.Back && Frame.CanGoBack)
        {
            Frame.GoBack();
        }
        else if (sourcePageType is not null)
        {
            Frame.Navigate(sourcePageType, parameter);
        }
    }

    private void InitializeAllowIfSecureState()
    {
        _allowIfSecureSecureFlags = NormalizeSecureFlags(Rule.SecureFlags);
        _allowIfSecureOverrideBlockRules = Rule.OverrideBlockRules;

        if (Rule.Direction == FirewallRuleDirection.ConnectionSecurity)
        {
            return;
        }

        if (Rule.ConnectionAction != FirewallConnectionAction.AllowIfSecure)
        {
            _allowIfSecureOverrideBlockRules = false;
            Rule.OverrideBlockRules = false;
            return;
        }

        var firewallRuleService = App.GetRequiredService<WindowsFirewallRuleService>();
        if (firewallRuleService.TryGetOverrideBlockRules(GetFirewallRuleLookupNames(Rule), out bool overrideBlockRules))
        {
            _allowIfSecureOverrideBlockRules = overrideBlockRules;
            Rule.OverrideBlockRules = overrideBlockRules;
        }
    }

    private void ApplyPredefinedRuleUI(FirewallRuleModel rule)
    {
        if (!rule.IsPredefined)
            return;

        PredefinedRuleInfoBar.Title = LocalizedStrings.WF_PredefinedRule_InfoBar_Title;
        PredefinedRuleInfoBar.Message = LocalizedStrings.WF_PredefinedRule_InfoBar_Message;
        PredefinedRuleInfoBar.Severity = InfoBarSeverity.Informational;
        PredefinedRuleInfoBar.IsOpen = true;

        // Disable editable fields that cannot be changed on predefined rules
        ConnectionActionBox.IsEnabled = true;
        CustomizeAllowSecureCard.IsEnabled = true;
        ProtocolTypeBox.IsEnabled = false;
        LocalPortOptionBox.IsEnabled = false;
        RemotePortOptionBox.IsEnabled = false;
        LocalPortInputBox.IsReadOnly = true;
        RemotePortInputBox.IsReadOnly = true;
        ICMPCustomizeButton.IsEnabled = false;
        ProgramComboBox.IsEnabled = false;
        ProgramPathBox.IsReadOnly = true;
        ProgramBrowseButton.IsEnabled = false;
        CompartmentsBox.IsEnabled = false;
        CompartmentInputBox.IsReadOnly = true;
        EdgeTraversalBox.IsEnabled = true;
        ProgramsServicesExpander2.IsEnabled = false;
    }

    private void ApplyDirectionUI(FirewallRuleDirection direction)
    {
        bool isInbound = direction == FirewallRuleDirection.Inbound;
        bool isConnectionSecurity = direction == FirewallRuleDirection.ConnectionSecurity;

        // Action, Programs & Services, Scope: standard rules only
        ActionExpander.Visibility = isConnectionSecurity ? Visibility.Collapsed : Visibility.Visible;
        ProgramsServicesLabel.Visibility = isConnectionSecurity ? Visibility.Collapsed : Visibility.Visible;
        ProgramsServicesExpander1.Visibility = isConnectionSecurity ? Visibility.Collapsed : Visibility.Visible;
        ProgramsServicesExpander2.Visibility = isConnectionSecurity ? Visibility.Collapsed : Visibility.Visible;
        ScopeLabel.Visibility = isConnectionSecurity ? Visibility.Collapsed : Visibility.Visible;
        ScopeCard.Visibility = isConnectionSecurity ? Visibility.Collapsed : Visibility.Visible;

        // Protocols and ports: swap between standard and CSR variants
        StandardProtocolsExpander.Visibility = isConnectionSecurity ? Visibility.Collapsed : Visibility.Visible;
        CsrProtocolsExpander.Visibility = isConnectionSecurity ? Visibility.Visible : Visibility.Collapsed;

        // Remote Computers: standard shows "Manage Authorized computers", CSR shows "Manage remote endpoints"
        RemoteComputersCard.Visibility = isConnectionSecurity ? Visibility.Collapsed : Visibility.Visible;
        CsrRemoteEndpointsCard.Visibility = isConnectionSecurity ? Visibility.Visible : Visibility.Collapsed;

        // Verification: Connection Security only
        VerificationLabel.Visibility = isConnectionSecurity ? Visibility.Visible : Visibility.Collapsed;
        VerificationModeCard.Visibility = isConnectionSecurity ? Visibility.Visible : Visibility.Collapsed;
        VerificationMethodsExpander.Visibility = isConnectionSecurity ? Visibility.Visible : Visibility.Collapsed;

        // Configure IPsec channel: Connection Security only
        ConfigureIPsecCard.Visibility = isConnectionSecurity ? Visibility.Visible : Visibility.Collapsed;

        // Edge traversal: Inbound only (not Connection Security)
        EdgeTraversalExpander.Visibility = isInbound ? Visibility.Visible : Visibility.Collapsed;

        // Local Principals: Inbound + Outbound (not Connection Security)
        bool showLocalPrincipals = !isConnectionSecurity;
        LocalPrincipalsLabel.Visibility = showLocalPrincipals ? Visibility.Visible : Visibility.Collapsed;
        LocalPrincipalsCard.Visibility = showLocalPrincipals ? Visibility.Visible : Visibility.Collapsed;

        // Remote Users: Inbound only (not Connection Security)
        RemoteUsersLabel.Visibility = isInbound ? Visibility.Visible : Visibility.Collapsed;
        RemoteUsersCard.Visibility = isInbound ? Visibility.Visible : Visibility.Collapsed;

        // Remote Computers label: visible for all directions
        RemoteComputersLabel.Visibility = Visibility.Visible;
    }

    private void PopulateComboBoxes(FirewallRuleModel rule)
    {
        bool isConnectionSecurity = rule.Direction == FirewallRuleDirection.ConnectionSecurity;

        if (isConnectionSecurity)
        {
            // Map ProtocolNumber back to the correct ComboBox item by Tag
            // Tag "256" = Any (ProtocolNumber == 0 in model), "-1" = Custom
            string targetTag = rule.ProtocolNumber switch
            {
                0   => "256",  // Any
                1   => "1",    // ICMPv4
                2   => "2",    // IGMP
                6   => "6",    // TCP
                17  => "17",   // UDP
                41  => "41",   // IPv6
                43  => "43",   // IPv6-Route
                44  => "44",   // IPv6-Frag
                47  => "47",   // GRE
                58  => "58",   // ICMPv6
                59  => "59",   // IPv6-NoNxt
                60  => "60",   // IPv6-Opts
                112 => "112",  // VRRP
                113 => "113",  // PGM
                115 => "115",  // L2TP
                _   => "-1"    // Custom
            };

            for (int i = 0; i < CsrProtocolTypeBox.Items.Count; i++)
            {
                if ((CsrProtocolTypeBox.Items[i] as ComboBoxItem)?.Tag as string == targetTag)
                {
                    CsrProtocolTypeBox.SelectedIndex = i;
                    break;
                }
            }

            // Endpoint ports
            CsrEndpoint1PortBox.SelectedIndex = IsAnyPortExpression(rule.LocalPort) ? 0 : 1;
            CsrEndpoint2PortBox.SelectedIndex = IsAnyPortExpression(rule.RemotePort) ? 0 : 1;
            NormalizeConnectionSecurityProtocolUiState(targetTag, clearInvalidValues: false);
            UpdateConnectionSecurityPortInputVisibility();

            // Verification defaults
            VerificationModeBox.SelectedIndex = 0;
            VerificationMethodsBox.SelectedIndex = 0;
            return;
        }

        // Connection action
        ConnectionActionBox.SelectedIndex = rule.ConnectionAction switch
        {
            FirewallConnectionAction.AllowIfSecure => 1,
            FirewallConnectionAction.Block => 2,
            _ => 0
        };

        // Protocol
        ProtocolTypeBox.SelectedIndex = rule.Protocol switch
        {
            FirewallRuleProtocol.HOPOPT    => 2,
            FirewallRuleProtocol.ICMPv4   => 3,
            FirewallRuleProtocol.IGMP     => 4,
            FirewallRuleProtocol.TCP      => 5,
            FirewallRuleProtocol.UDP      => 6,
            FirewallRuleProtocol.IPv6     => 7,
            FirewallRuleProtocol.IPv6Route => 8,
            FirewallRuleProtocol.IPv6Frag => 9,
            FirewallRuleProtocol.GRE      => 10,
            FirewallRuleProtocol.ICMPv6   => 11,
            FirewallRuleProtocol.IPv6NoNxt => 12,
            FirewallRuleProtocol.IPv6Opts => 13,
            FirewallRuleProtocol.VRRP     => 14,
            FirewallRuleProtocol.PGM      => 15,
            FirewallRuleProtocol.L2TP     => 16,
            FirewallRuleProtocol.Custom   => 1,
            _ => 0
        };
        NormalizeStandardProtocolUiState(GetSelectedTag(ProtocolTypeBox), clearInvalidValues: false);

        // Port options — only TCP/UDP support ports
        bool portEnabled = rule.Protocol is FirewallRuleProtocol.TCP or FirewallRuleProtocol.UDP;
        UpdatePortSectionEnabled(portEnabled);
        LocalPortOptionBox.SelectedIndex = portEnabled && !IsAnyPortExpression(rule.LocalPort) ? 1 : 0;
        RemotePortOptionBox.SelectedIndex = portEnabled && !IsAnyPortExpression(rule.RemotePort) ? 1 : 0;
        UpdateStandardPortInputVisibility();

        // Edge traversal
        EdgeTraversalBox.SelectedIndex = rule.EdgeTraversal switch
        {
            FirewallEdgeTraversal.Allow => 1,
            FirewallEdgeTraversal.DeferToUser => 2,
            FirewallEdgeTraversal.DeferToApp => 3,
            _ => 0
        };
        UpdateEdgeTraversalDescription();

        // Program
        ProgramComboBox.SelectedIndex = string.IsNullOrWhiteSpace(rule.Program) ? 0 : 1;
        ProgramPathCard.Visibility = string.IsNullOrWhiteSpace(rule.Program)
            ? Visibility.Collapsed : Visibility.Visible;

        // Compartments
        CompartmentsBox.SelectedIndex = string.IsNullOrWhiteSpace(rule.Compartments) ? 0 : 1;
        CompartmentInputCard.Visibility = string.IsNullOrWhiteSpace(rule.Compartments)
            ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ConnectionActionBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var tag = (ConnectionActionBox.SelectedItem as ComboBoxItem)?.Tag as string;
        Rule.ConnectionAction = tag switch
        {
            "AllowIfSecure" => FirewallConnectionAction.AllowIfSecure,
            "Block" => FirewallConnectionAction.Block,
            _ => FirewallConnectionAction.Allow
        };

        Rule.Action = Rule.ConnectionAction == FirewallConnectionAction.Block
            ? FirewallRuleAction.Block
            : FirewallRuleAction.Allow;

        if (Rule.ConnectionAction == FirewallConnectionAction.AllowIfSecure)
        {
            _allowIfSecureSecureFlags = NormalizeSecureFlags(Rule.SecureFlags);
            Rule.SecureFlags = _allowIfSecureSecureFlags;
            Rule.OverrideBlockRules = _allowIfSecureOverrideBlockRules;
        }
        else
        {
            Rule.SecureFlags = NetFwAuthenticateNone;
            Rule.OverrideBlockRules = false;
        }

        CustomizeAllowSecureCard.Visibility = tag == "AllowIfSecure"
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ProtocolTypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isPopulatingRuleUi)
        {
            return;
        }

        var tag = (ProtocolTypeBox.SelectedItem as ComboBoxItem)?.Tag as string;
        (Rule.Protocol, Rule.ProtocolNumber) = tag switch
        {
            "HOPOPT"    => (FirewallRuleProtocol.HOPOPT,    0),
            "ICMPv4"    => (FirewallRuleProtocol.ICMPv4,    1),
            "IGMP"      => (FirewallRuleProtocol.IGMP,      2),
            "TCP"       => (FirewallRuleProtocol.TCP,       6),
            "UDP"       => (FirewallRuleProtocol.UDP,       17),
            "IPv6"      => (FirewallRuleProtocol.IPv6,      41),
            "IPv6Route" => (FirewallRuleProtocol.IPv6Route, 43),
            "IPv6Frag"  => (FirewallRuleProtocol.IPv6Frag,  44),
            "GRE"       => (FirewallRuleProtocol.GRE,       47),
            "ICMPv6"    => (FirewallRuleProtocol.ICMPv6,    58),
            "IPv6NoNxt" => (FirewallRuleProtocol.IPv6NoNxt, 59),
            "IPv6Opts"  => (FirewallRuleProtocol.IPv6Opts,  60),
            "VRRP"      => (FirewallRuleProtocol.VRRP,      112),
            "PGM"       => (FirewallRuleProtocol.PGM,       113),
            "L2TP"      => (FirewallRuleProtocol.L2TP,      115),
            "Custom"    => (FirewallRuleProtocol.Custom,    Rule.ProtocolNumber),
            _           => (FirewallRuleProtocol.Any,       256)
        };
        NormalizeStandardProtocolUiState(tag, clearInvalidValues: true);
    }

    private void ProtocolNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_isPopulatingRuleUi || _isSynchronizingProtocolNumber)
        {
            return;
        }

        if (string.Equals(GetSelectedTag(ProtocolTypeBox), "Custom", StringComparison.Ordinal) &&
            TryGetWholeNumber(sender, out int protocolNumber))
        {
            SetCustomProtocolNumber(protocolNumber);
        }
    }

    private void UpdatePortSectionEnabled(bool enabled)
    {
        LocalPortOptionBox.IsEnabled = enabled;
        RemotePortOptionBox.IsEnabled = enabled;
    }

    private static bool IsAnyPortExpression(string? value)
        => string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), "*", StringComparison.Ordinal);

    private void NormalizeStandardProtocolUiState(string? tag, bool clearInvalidValues)
    {
        bool isAny = string.Equals(tag, "Any", StringComparison.Ordinal);
        bool isCustom = string.Equals(tag, "Custom", StringComparison.Ordinal);
        bool isIcmp = tag is "ICMPv4" or "ICMPv6";
        bool isTcpOrUdp = tag is "TCP" or "UDP";

        ProtocolNumberBox.Maximum = isCustom ? 255 : 256;
        ProtocolNumberBox.IsEnabled = isCustom;
        ICMPCustomizeButton.IsEnabled = isIcmp;
        UpdatePortSectionEnabled(isTcpOrUdp);

        if (isAny)
        {
            Rule.ProtocolNumber = 256;
        }
        else if (isCustom && (Rule.ProtocolNumber < 0 || Rule.ProtocolNumber > 255))
        {
            Rule.ProtocolNumber = 0;
        }

        if (clearInvalidValues)
        {
            if (!isIcmp)
            {
                Rule.IcmpTypesAndCodes = string.Empty;
            }

            if (!isTcpOrUdp)
            {
                LocalPortOptionBox.SelectedIndex = 0;
                RemotePortOptionBox.SelectedIndex = 0;
                Rule.LocalPort = string.Empty;
                Rule.RemotePort = string.Empty;
            }
        }

        UpdateStandardPortInputVisibility();
    }

    private void NormalizeConnectionSecurityProtocolUiState(string? tag, bool clearInvalidValues)
    {
        bool isCustom = string.Equals(tag, "-1", StringComparison.Ordinal);
        bool isTcpOrUdp = tag is "6" or "17";

        CsrProtocolNumberBox.IsEnabled = isCustom;
        CsrEndpoint1PortBox.IsEnabled = isTcpOrUdp;
        CsrEndpoint2PortBox.IsEnabled = isTcpOrUdp;

        if (isCustom && (Rule.ProtocolNumber < 0 || Rule.ProtocolNumber > 255))
        {
            Rule.ProtocolNumber = 0;
        }

        if (clearInvalidValues && !isTcpOrUdp)
        {
            CsrEndpoint1PortBox.SelectedIndex = 0;
            CsrEndpoint2PortBox.SelectedIndex = 0;
            Rule.LocalPort = string.Empty;
            Rule.RemotePort = string.Empty;
        }

        UpdateConnectionSecurityPortInputVisibility();
    }

    private void UpdateStandardPortInputVisibility()
    {
        bool showLocalPort = LocalPortOptionBox.IsEnabled &&
            string.Equals(GetSelectedTag(LocalPortOptionBox), "SpecificPorts", StringComparison.Ordinal);
        bool showRemotePort = RemotePortOptionBox.IsEnabled &&
            string.Equals(GetSelectedTag(RemotePortOptionBox), "SpecificPorts", StringComparison.Ordinal);

        LocalPortInputCard.Visibility = showLocalPort ? Visibility.Visible : Visibility.Collapsed;
        RemotePortInputCard.Visibility = showRemotePort ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateConnectionSecurityPortInputVisibility()
    {
        bool showEndpoint1Port = CsrEndpoint1PortBox.IsEnabled &&
            string.Equals(GetSelectedTag(CsrEndpoint1PortBox), "SpecificPorts", StringComparison.Ordinal);
        bool showEndpoint2Port = CsrEndpoint2PortBox.IsEnabled &&
            string.Equals(GetSelectedTag(CsrEndpoint2PortBox), "SpecificPorts", StringComparison.Ordinal);

        CsrEndpoint1PortInputCard.Visibility = showEndpoint1Port ? Visibility.Visible : Visibility.Collapsed;
        CsrEndpoint2PortInputCard.Visibility = showEndpoint2Port ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string? GetSelectedTag(ComboBox comboBox)
        => (comboBox.SelectedItem as ComboBoxItem)?.Tag as string;

    private void LocalPortOptionBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!string.Equals(GetSelectedTag(LocalPortOptionBox), "SpecificPorts", StringComparison.Ordinal))
        {
            Rule.LocalPort = string.Empty;
        }

        UpdateStandardPortInputVisibility();
    }

    private void RemotePortOptionBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!string.Equals(GetSelectedTag(RemotePortOptionBox), "SpecificPorts", StringComparison.Ordinal))
        {
            Rule.RemotePort = string.Empty;
        }

        UpdateStandardPortInputVisibility();
    }

    private void ProgramComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var tag = (ProgramComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
        ProgramPathCard.Visibility = tag == "This"
            ? Visibility.Visible : Visibility.Collapsed;
        if (tag == "All")
            Rule.Program = string.Empty;
    }

    private async void ProgramBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        var path = await App.GetRequiredService<ManagementTools.Core.Abstractions.Services.IFileDialogService>().OpenFileAsync(
            hwnd,
            filter: GetExecutableFilesFilter(),
            title: LocalizedStrings.WF_Field_Program);
        if (path is not null)
            Rule.Program = path;
    }

    private void CompartmentsBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var tag = (CompartmentsBox.SelectedItem as ComboBoxItem)?.Tag as string;
        CompartmentInputCard.Visibility = tag == "This"
            ? Visibility.Visible : Visibility.Collapsed;
        if (tag == "All")
            Rule.Compartments = string.Empty;
    }

    private async void ApplyButton_Click(object sender, RoutedEventArgs e) => await SaveAsync();

    /// <summary>Applies the pending edits to the firewall/connection-security rule. Returns true on success.</summary>
    private async Task<bool> SaveAsync()
    {
        var adminService = App.GetRequiredService<ManagementTools.Core.Abstractions.Services.IAdminService>();
        if (!adminService.IsRunningAsAdmin)
        {
            await ManagementTools.Helpers.AdminDialogHelper.ShowAdminRequiredDialogAsync(this.XamlRoot);
            return false;
        }

        if (!await CommitProtocolNumberBeforeApplyAsync())
        {
            return false;
        }

        if (!await ValidateRuleBeforeApplyAsync())
        {
            return false;
        }

        SetApplyInProgress(true);

        if (_connectionSecurityRule is not null)
        {
            try
            {
                UpdateConnectionSecurityRuleFromUi();
                var connectionSecurityService = App.GetRequiredService<ConnectionSecurityService>();
                await Task.Run(() => connectionSecurityService.UpdateRule(_connectionSecurityRule));
                _hasUnsavedChanges = false;
                return true;
            }
            catch (Exception ex)
            {
                await ShowErrorDialogAsync(LocalizedStrings.WF_Error_UpdateConnectionSecurityRule, ex.Message);
                return false;
            }
            finally
            {
                SetApplyInProgress(false);
            }
        }

        Rule.Action = Rule.ConnectionAction == FirewallConnectionAction.Block
            ? FirewallRuleAction.Block
            : FirewallRuleAction.Allow;
        Rule.SecureFlags = Rule.ConnectionAction == FirewallConnectionAction.AllowIfSecure
            ? NormalizeSecureFlags(_allowIfSecureSecureFlags)
            : NetFwAuthenticateNone;
        Rule.OverrideBlockRules = Rule.ConnectionAction == FirewallConnectionAction.AllowIfSecure &&
                                  _allowIfSecureOverrideBlockRules;

        try
        {
            var firewallService = App.GetRequiredService<WindowsFirewallService>();
            var firewallRuleService = App.GetRequiredService<WindowsFirewallRuleService>();
            FirewallRuleModel ruleToApply = CreateRuleApplySnapshot(Rule);
            await Task.Run(() =>
            {
                firewallService.UpdateRule(ruleToApply);
                if (ruleToApply.ConnectionAction == FirewallConnectionAction.AllowIfSecure)
                {
                    firewallRuleService.SetOverrideBlockRules(GetFirewallRuleLookupNames(ruleToApply), ruleToApply.OverrideBlockRules);
                }
            });
            Rule.OriginalName = ruleToApply.OriginalName;
            await RefreshRuleStatusFromSystemChangeAsync(includeMutableState: true);
            _hasUnsavedChanges = false;
            return true;
        }
        catch (Exception ex)
        {
            await ShowErrorDialogAsync(LocalizedStrings.WF_Error_UpdateRule, ex.Message);
            return false;
        }
        finally
        {
            SetApplyInProgress(false);
        }
    }

    private async void DisableButton_Click(object sender, RoutedEventArgs e)
    {
        var adminService = App.GetRequiredService<ManagementTools.Core.Abstractions.Services.IAdminService>();
        if (!adminService.IsRunningAsAdmin)
        {
            await ManagementTools.Helpers.AdminDialogHelper.ShowAdminRequiredDialogAsync(this.XamlRoot);
            return;
        }

        if (_connectionSecurityRule is not null)
        {
            bool previousConnectionSecurityEnabled = _connectionSecurityRule.Enabled;
            _connectionSecurityRule.Enabled = !_connectionSecurityRule.Enabled;
            Rule.Enabled = _connectionSecurityRule.Enabled;
            UpdateDisableButton();

            try
            {
                var connectionSecurityService = App.GetRequiredService<ConnectionSecurityService>();
                connectionSecurityService.SetRuleEnabled(GetConnectionSecurityRuleLookupName(_connectionSecurityRule), _connectionSecurityRule.Enabled);
            }
            catch (Exception ex)
            {
                _connectionSecurityRule.Enabled = previousConnectionSecurityEnabled;
                Rule.Enabled = previousConnectionSecurityEnabled;
                UpdateDisableButton();
                await ShowErrorDialogAsync(LocalizedStrings.WF_Error_UpdateConnectionSecurityRule, ex.Message);
            }

            return;
        }

        bool previousEnabled = Rule.Enabled;
        Rule.Enabled = !Rule.Enabled;
        UpdateDisableButton();

        try
        {
            var firewallService = App.GetRequiredService<WindowsFirewallService>();
            firewallService.SetRuleEnabled(Rule.OriginalName, Rule.Enabled);
        }
        catch (Exception ex)
        {
            Rule.Enabled = previousEnabled;
            UpdateDisableButton();
            await ShowErrorDialogAsync(LocalizedStrings.WF_Error_UpdateRule, ex.Message);
        }
    }

    private void UpdateDisableButton()
    {
        // When enabled, offer to disable; when disabled, offer to enable
        DisableButton.Label = Rule.Enabled
            ? LocalizedStrings.WF_Rule_Disable
            : LocalizedStrings.WF_Rule_Enable;
        ((FontIcon)DisableButton.Icon).Glyph = Rule.Enabled ? "\uE71A" : "\uE768";
    }

    private async void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var adminService = App.GetRequiredService<ManagementTools.Core.Abstractions.Services.IAdminService>();
        if (!adminService.IsRunningAsAdmin)
        {
            await ManagementTools.Helpers.AdminDialogHelper.ShowAdminRequiredDialogAsync(this.XamlRoot);
            return;
        }

        if (await ShowDeleteConfirmationDialogAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            if (_connectionSecurityRule is not null)
            {
                var connectionSecurityService = App.GetRequiredService<ConnectionSecurityService>();
                connectionSecurityService.DeleteRule(GetConnectionSecurityRuleLookupName(_connectionSecurityRule));
            }
            else
            {
                var firewallService = App.GetRequiredService<WindowsFirewallService>();
                firewallService.DeleteRule(GetFirewallRuleLookupName(Rule));
            }
        }
        catch (Exception ex)
        {
            await ShowErrorDialogAsync(LocalizedStrings.WF_Error_DeleteRule, ex.Message);
            return;
        }

        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
    }

    private void EdgeTraversalBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var tag = (EdgeTraversalBox.SelectedItem as ComboBoxItem)?.Tag as string;
        Rule.EdgeTraversal = tag switch
        {
            "Allow" => FirewallEdgeTraversal.Allow,
            "DeferToUser" => FirewallEdgeTraversal.DeferToUser,
            "DeferToApp" => FirewallEdgeTraversal.DeferToApp,
            _ => FirewallEdgeTraversal.Block
        };
        UpdateEdgeTraversalDescription();
    }

    private void UpdateEdgeTraversalDescription()
    {
        var tag = (EdgeTraversalBox.SelectedItem as ComboBoxItem)?.Tag as string;
        EdgeTraversalDescText.Text = tag switch
        {
            "Allow" => LocalizedStrings.WF_EdgeTraversal_Allow_Desc,
            "DeferToUser" => LocalizedStrings.WF_EdgeTraversal_DeferToUser_Desc,
            "DeferToApp" => LocalizedStrings.WF_EdgeTraversal_DeferToApp_Desc,
            _ => LocalizedStrings.WF_EdgeTraversal_Block_Desc
        };
    }

    private void CsrProtocolTypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isPopulatingRuleUi)
        {
            return;
        }

        var tag = (CsrProtocolTypeBox.SelectedItem as ComboBoxItem)?.Tag as string;
        bool isCustom = string.Equals(tag, "-1", StringComparison.Ordinal);

        // Tag "256" = Any, "-1" = Custom (user enters number manually), otherwise IANA number
        if (!isCustom && int.TryParse(tag, out int proto))
        {
            // 256 is the WFP sentinel for "Any" (maps to ProtocolNumber = 0 in our model)
            Rule.ProtocolNumber = proto == 256 ? 0 : proto;
        }

        NormalizeConnectionSecurityProtocolUiState(tag, clearInvalidValues: true);
    }

    private void CsrProtocolNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_isPopulatingRuleUi || _isSynchronizingProtocolNumber)
        {
            return;
        }

        if (string.Equals(GetSelectedTag(CsrProtocolTypeBox), "-1", StringComparison.Ordinal) &&
            TryGetWholeNumber(sender, out int protocolNumber))
        {
            SetConnectionSecurityProtocolNumber(protocolNumber);
        }
    }

    private void CsrEndpoint1PortBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!string.Equals(GetSelectedTag(CsrEndpoint1PortBox), "SpecificPorts", StringComparison.Ordinal))
        {
            Rule.LocalPort = string.Empty;
        }

        UpdateConnectionSecurityPortInputVisibility();
    }

    private void CsrEndpoint2PortBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!string.Equals(GetSelectedTag(CsrEndpoint2PortBox), "SpecificPorts", StringComparison.Ordinal))
        {
            Rule.RemotePort = string.Empty;
        }

        UpdateConnectionSecurityPortInputVisibility();
    }

    private void VerificationMethodsBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var tag = (VerificationMethodsBox.SelectedItem as ComboBoxItem)?.Tag as string;
        CustomizeMethodsCard.Visibility = tag == "Advanced"
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void VerificationModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var tag = (VerificationModeBox.SelectedItem as ComboBoxItem)?.Tag as string;
        VerificationMethodsExpander.IsEnabled = tag != "DoNotAuthenticate";
    }

    // ── Dialog openers ──────────────────────────────────────────────────────

    private async void CustomizeAllowSecureButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CustomizeAllowIfSecureDialog();
        dialog.ConfigureEncryptionNegotiationOption(Rule.Direction == FirewallRuleDirection.Inbound);
        dialog.ApplySelection(_allowIfSecureSecureFlags, _allowIfSecureOverrideBlockRules);
        WindowDialogResult result = await dialog.ShowDialogAsync(this.XamlRoot);
        if (result == WindowDialogResult.Primary)
        {
            _allowIfSecureSecureFlags = dialog.SelectedSecureFlags;
            _allowIfSecureOverrideBlockRules = dialog.OverrideBlockRules;
            Rule.SecureFlags = _allowIfSecureSecureFlags;
            Rule.OverrideBlockRules = _allowIfSecureOverrideBlockRules;
        }
    }

    private async void ApplicationPackagesButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ApplicationPackagesDialog { XamlRoot = this.XamlRoot };
        dialog.ApplyLocalAppPackageId(Rule.LocalAppPackageId);
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            Rule.LocalAppPackageId = dialog.LocalAppPackageIdExpression;
        }
    }

    private async void ServicesButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ServicesDialog();
        dialog.ApplyServiceExpression(Rule.ServiceName);

        if (await dialog.ShowDialogAsync(this.XamlRoot) != WindowDialogResult.Primary)
        {
            return;
        }

        Rule.ServiceName = ResolveServicesExpression(dialog);
    }

    private async void GeneralEditButton_Click(object sender, RoutedEventArgs e)
    {
        bool isPredefined = _connectionSecurityRule is null && Rule.IsPredefined;

        var editableNameBox = new TextBox
        {
            MinWidth = 320,
            Text = Rule.Name,
            Visibility = isPredefined ? Visibility.Collapsed : Visibility.Visible
        };
        var predefinedNameBox = new TextBox
        {
            MinWidth = 320,
            IsReadOnly = true,
            Text = Rule.DisplayName,
            Visibility = isPredefined ? Visibility.Visible : Visibility.Collapsed
        };
        var descriptionBox = new TextBox
        {
            Height = 96,
            MinWidth = 320,
            AcceptsReturn = true,
            IsReadOnly = isPredefined,
            Text = Rule.Description,
            TextWrapping = TextWrapping.Wrap
        };

        var content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = LocalizedStrings.WF_Field_Name },
                editableNameBox,
                predefinedNameBox,
                new TextBlock { Text = LocalizedStrings.WF_Field_Description },
                descriptionBox
            }
        };

        var dialog = new ContentDialog
        {
            Title = LocalizedStrings.WF_Section_General,
            Content = content,
            RequestedTheme = App.CurrentTheme,
            XamlRoot = XamlRoot,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style
        };

        if (isPredefined)
        {
            dialog.CloseButtonText = LocalizedStrings.Common_OKButton;
        }
        else
        {
            dialog.PrimaryButtonText = LocalizedStrings.Common_SaveButton;
            dialog.CloseButtonText = LocalizedStrings.Common_CancelButton;
        }

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            Rule.Name = editableNameBox.Text;
            Rule.Description = descriptionBox.Text;
        }
    }

    private async void ICMPCustomizeButton_Click(object sender, RoutedEventArgs e)
    {
        var content = new ICMPSettingsDialog();
        content.ApplyIcmpTypesAndCodes(Rule.IcmpTypesAndCodes);

        var dialog = new ModalDialogWindow(new ModalDialogOptions
        {
            Title = LocalizedStrings.WF_CustomizeIcmpSettings_Title,
            Content = content,
            OwnerXamlRoot = this.XamlRoot,
            PrimaryButtonText = LocalizedStrings.Common_OKButton,
            CloseButtonText = LocalizedStrings.Common_CancelButton,
            Width = 560,
            Height = 560,
            OnPrimaryButtonClick = () =>
            {
                content.CommitResult();
                return true;
            }
        });

        if (await dialog.ShowDialogAsync() == WindowDialogResult.Primary)
        {
            Rule.IcmpTypesAndCodes = content.IcmpTypesAndCodesExpression;
        }
    }

    private static string ResolveServicesExpression(ServicesDialog dialog)
    {
        if (dialog.ApplyToAllServices)
        {
            return string.Empty;
        }

        if (dialog.ApplyOnlyToServices)
        {
            return "*";
        }

        if (dialog.ApplyToSpecificService)
        {
            return dialog.SelectedService?.ShortName ?? string.Empty;
        }

        return dialog.SelectedServiceShortName.Trim();
    }

    private async void ScopeManageButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ScopeIPAddressDialog();
        dialog.ApplyAddressExpressions(Rule.LocalAddress, Rule.RemoteAddress);
        if (await dialog.ShowDialogAsync(this.XamlRoot) == WindowDialogResult.Primary)
        {
            Rule.LocalAddress = dialog.LocalAddressExpression;
            Rule.RemoteAddress = dialog.RemoteAddressExpression;
        }
    }

    private async void RemoteComputersManageButton_Click(object sender, RoutedEventArgs e)
    {
        bool isSecureConnection = ConnectionActionBox.SelectedItem is ComboBoxItem item &&
                                   item.Tag?.ToString() == "AllowIfSecure";

        var dialog = new RemoteComputersDialog(isCsrMode: false, isSecureConnection: isSecureConnection)
        {
            XamlRoot = this.XamlRoot
        };
        dialog.ApplySddl(
            BuildAllowOnlySddl(Rule.RemoteMachineAuthorizedList),
            BuildDenyOnlySddl(Rule.RemoteMachineAuthorizedList));

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            Rule.RemoteMachineAuthorizedList = BuildCombinedSddl(dialog.AuthorizedSddl, dialog.ExceptionSddl);
        }
    }

    private async void CsrRemoteEndpointsManageButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new RemoteEndpointsDialog();
        if (_connectionSecurityRule is not null)
        {
            dialog.ApplyAddressExpressions(_connectionSecurityRule.Endpoint1Expression, _connectionSecurityRule.Endpoint2Expression);
        }

        if (await dialog.ShowDialogAsync(this.XamlRoot) == WindowDialogResult.Primary && _connectionSecurityRule is not null)
        {
            _connectionSecurityRule.Endpoint1Expression = dialog.Endpoint1AddressExpression;
            _connectionSecurityRule.Endpoint2Expression = dialog.Endpoint2AddressExpression;
            _connectionSecurityRule.Summary = string.Format(
                CultureInfo.CurrentCulture,
                LocalizedStrings.WF_Summary_EndpointArrowFormat,
                _connectionSecurityRule.Endpoint1Expression,
                _connectionSecurityRule.Endpoint2Expression);
        }
    }

    private async void CustomizeAuthMethodsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_connectionSecurityRule is null)
        {
            return;
        }

        var dialog = new CustomizeAuthMethodsDialog();
        dialog.ApplySelections(
            _connectionSecurityRule.FirstAuthMethods.ToList(),
            _connectionSecurityRule.SecondAuthMethods.ToList(),
            _connectionSecurityRule.IsFirstAuthOptional,
            _connectionSecurityRule.IsSecondAuthOptional);

        if (await dialog.ShowDialogAsync(this.XamlRoot) == WindowDialogResult.Primary)
        {
            CopyAuthMethodResults(_connectionSecurityRule.FirstAuthMethods, dialog.FirstMethods);
            CopyAuthMethodResults(_connectionSecurityRule.SecondAuthMethods, dialog.SecondMethods);
            _connectionSecurityRule.IsFirstAuthOptional = dialog.IsFirstAuthOptional;
            _connectionSecurityRule.IsSecondAuthOptional = dialog.IsSecondAuthOptional;
            VerificationMethodsBox.SelectedIndex =
                _connectionSecurityRule.FirstAuthMethods.Count > 0 || _connectionSecurityRule.SecondAuthMethods.Count > 0
                    ? 4
                    : 0;
        }
    }

    private async void ConfigureIPsecButton_Click(object sender, RoutedEventArgs e)
    {
        var content = new ConfigureIPsecDialog();
        if (_connectionSecurityRule is not null)
        {
            content.ApplyFromRule(_connectionSecurityRule);
        }

        var dialog = new ModalDialogWindow(new ModalDialogOptions
        {
            Title = LocalizedStrings.WF_CustomizeIpsecTunnelSettings_Title,
            Content = content,
            OwnerXamlRoot = this.XamlRoot,
            PrimaryButtonText = LocalizedStrings.Common_OKButton,
            CloseButtonText = LocalizedStrings.Common_CancelButton,
            Width = 640,
            Height = 560,
        });

        if (await dialog.ShowDialogAsync() == WindowDialogResult.Primary && _connectionSecurityRule is not null)
        {
            _connectionSecurityRule.Mode = content.UseIpsecTunnel
                ? ConnectionSecurityMode.Tunnel
                : ConnectionSecurityMode.Transport;
            _connectionSecurityRule.RequireAuthorization = content.ApplyAuthorization;
            _connectionSecurityRule.BypassTunnelIfEncrypted = content.ExemptIpsecProtectedConnections;
            _connectionSecurityRule.LocalTunnelEndpoint = content.LocalTunnelEndpointExpression;
            _connectionSecurityRule.RemoteTunnelEndpoint = content.RemoteTunnelEndpointExpression;
        }
    }

    private async void InterfaceTypesButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new InterfaceTypesDialog();
        dialog.ApplyInterfaceTypes(Rule.InterfaceTypes);
        if (await dialog.ShowDialogAsync(XamlRoot) == WindowDialogResult.Primary)
        {
            Rule.InterfaceTypes = dialog.SelectedInterfaceTypes;
            Rule.Interfaces = string.Empty;
        }
    }

    private async void LocalPrincipalsManageButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PrincipalsDialog(PrincipalsDialog.PrincipalsMode.LocalPrincipals) { XamlRoot = this.XamlRoot };
        dialog.ApplySddl(
            BuildAllowOnlySddl(Rule.LocalUserAuthorizedList),
            BuildDenyOnlySddl(Rule.LocalUserAuthorizedList));

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            Rule.LocalUserAuthorizedList = BuildCombinedSddl(dialog.AuthorizedSddl, dialog.ExceptionSddl);
        }
    }

    private async void RemoteUsersManageButton_Click(object sender, RoutedEventArgs e)
    {
        bool isSecureConnection = Rule.ConnectionAction == FirewallConnectionAction.AllowIfSecure;
        var dialog = new PrincipalsDialog(PrincipalsDialog.PrincipalsMode.RemoteUsers, isSecureConnection)
        {
            XamlRoot = this.XamlRoot
        };
        dialog.ApplySddl(
            BuildAllowOnlySddl(Rule.RemoteUserAuthorizedList),
            BuildDenyOnlySddl(Rule.RemoteUserAuthorizedList));

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            Rule.RemoteUserAuthorizedList = BuildCombinedSddl(dialog.AuthorizedSddl, dialog.ExceptionSddl);
        }
    }

    private async Task<bool> CommitProtocolNumberBeforeApplyAsync()
    {
        if (_connectionSecurityRule is not null)
        {
            if (!string.Equals(GetSelectedTag(CsrProtocolTypeBox), "-1", StringComparison.Ordinal))
            {
                return true;
            }

            if (!TryGetWholeNumber(CsrProtocolNumberBox, out int csrProtocolNumber))
            {
                await ShowValidationDialogAsync(LocalizedStrings.WF_Validation_ProtocolNumberInvalid);
                return false;
            }

            SetConnectionSecurityProtocolNumber(csrProtocolNumber);
            return true;
        }

        if (!string.Equals(GetSelectedTag(ProtocolTypeBox), "Custom", StringComparison.Ordinal))
        {
            return true;
        }

        if (!TryGetWholeNumber(ProtocolNumberBox, out int protocolNumber))
        {
            await ShowValidationDialogAsync(LocalizedStrings.WF_Validation_ProtocolNumberInvalid);
            return false;
        }

        SetCustomProtocolNumber(protocolNumber);
        return true;
    }

    private void SetCustomProtocolNumber(int protocolNumber)
    {
        try
        {
            _isSynchronizingProtocolNumber = true;
            if (Rule.Protocol != FirewallRuleProtocol.Custom)
            {
                Rule.Protocol = FirewallRuleProtocol.Custom;
            }

            if (Rule.ProtocolNumber != protocolNumber)
            {
                Rule.ProtocolNumber = protocolNumber;
            }
        }
        finally
        {
            _isSynchronizingProtocolNumber = false;
        }
    }

    private void SetConnectionSecurityProtocolNumber(int protocolNumber)
    {
        try
        {
            _isSynchronizingProtocolNumber = true;
            if (Rule.ProtocolNumber != protocolNumber)
            {
                Rule.ProtocolNumber = protocolNumber;
            }
        }
        finally
        {
            _isSynchronizingProtocolNumber = false;
        }
    }

    private static bool TryGetWholeNumber(NumberBox numberBox, out int value)
    {
        value = 0;
        TextBox? innerTextBox = FindVisualDescendant<TextBox>(numberBox);
        if (!string.IsNullOrWhiteSpace(innerTextBox?.Text))
        {
            if (!int.TryParse(innerTextBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedValue) ||
                parsedValue < numberBox.Minimum ||
                parsedValue > numberBox.Maximum)
            {
                return false;
            }

            value = parsedValue;
            return true;
        }

        double rawValue = numberBox.Value;
        if (double.IsNaN(rawValue) || double.IsInfinity(rawValue))
        {
            return false;
        }

        double wholeValue = Math.Round(rawValue);
        if (Math.Abs(rawValue - wholeValue) > double.Epsilon ||
            wholeValue < numberBox.Minimum ||
            wholeValue > numberBox.Maximum)
        {
            return false;
        }

        value = (int)wholeValue;
        return true;
    }

    private static T? FindVisualDescendant<T>(DependencyObject parent)
        where T : DependencyObject
    {
        Queue<DependencyObject> pending = new();
        pending.Enqueue(parent);

        while (pending.Count > 0)
        {
            DependencyObject current = pending.Dequeue();
            int childCount = VisualTreeHelper.GetChildrenCount(current);
            for (int index = 0; index < childCount; index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(current, index);
                if (child is T match)
                {
                    return match;
                }

                pending.Enqueue(child);
            }
        }

        return null;
    }

    private async Task<bool> ValidateRuleBeforeApplyAsync()
    {
        if (string.IsNullOrWhiteSpace(Rule.Name))
        {
            await ShowValidationDialogAsync(LocalizedStrings.WF_Error_NameRequired);
            return false;
        }

        if (_connectionSecurityRule is not null)
        {
            return true;
        }

        if (string.Equals(GetSelectedTag(ProgramComboBox), "This", StringComparison.Ordinal) &&
            string.IsNullOrWhiteSpace(Rule.Program))
        {
            await ShowValidationDialogAsync(LocalizedStrings.WF_Validation_ProgramPathRequired);
            return false;
        }

        if (string.Equals(GetSelectedTag(CompartmentsBox), "This", StringComparison.Ordinal) &&
            string.IsNullOrWhiteSpace(Rule.Compartments))
        {
            await ShowValidationDialogAsync(LocalizedStrings.WF_Validation_CompartmentRequired);
            return false;
        }

        if (string.Equals(GetSelectedTag(CompartmentsBox), "This", StringComparison.Ordinal))
        {
            if (!ushort.TryParse(Rule.Compartments, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                await ShowValidationDialogAsync(LocalizedStrings.WF_Validation_CompartmentInvalid);
                return false;
            }
        }

        return true;
    }

    private void SetApplyInProgress(bool isInProgress)
    {
        ApplyProgressOverlay.Visibility = isInProgress ? Visibility.Visible : Visibility.Collapsed;

        if (isInProgress)
        {
            ApplyButton.IsEnabled = false;
            DisableButton.IsEnabled = false;
            DeleteMenuItem.IsEnabled = false;
            return;
        }

        RestoreRuleAvailabilityUi();
        UpdateDisableButton();
    }

    private static FirewallRuleModel CreateRuleApplySnapshot(FirewallRuleModel source)
    {
        return new FirewallRuleModel
        {
            Name = source.Name,
            DisplayName = source.DisplayName,
            Description = source.Description,
            OriginalName = source.OriginalName,
            DisplayDescription = source.DisplayDescription,
            Enabled = source.Enabled,
            Direction = source.Direction,
            Action = source.Action,
            Protocol = source.Protocol,
            LocalPort = source.LocalPort,
            RemotePort = source.RemotePort,
            LocalAddress = source.LocalAddress,
            RemoteAddress = source.RemoteAddress,
            Program = source.Program,
            Profile = source.Profile,
            Compartments = source.Compartments,
            ApplicationPackages = source.ApplicationPackages,
            Services = source.Services,
            ConnectionAction = source.ConnectionAction,
            LocalPortOption = source.LocalPortOption,
            RemotePortOption = source.RemotePortOption,
            ProtocolNumber = source.ProtocolNumber,
            ProfilesMask = source.ProfilesMask,
            EdgeTraversal = source.EdgeTraversal,
            Grouping = source.Grouping,
            DisplayGrouping = source.DisplayGrouping,
            InterfaceTypes = source.InterfaceTypes,
            Interfaces = source.Interfaces,
            IcmpTypesAndCodes = source.IcmpTypesAndCodes,
            LocalAppPackageId = source.LocalAppPackageId,
            SecureFlags = source.SecureFlags,
            OverrideBlockRules = source.OverrideBlockRules,
            LocalUserAuthorizedList = source.LocalUserAuthorizedList,
            LocalUserOwner = source.LocalUserOwner,
            RemoteMachineAuthorizedList = source.RemoteMachineAuthorizedList,
            RemoteUserAuthorizedList = source.RemoteUserAuthorizedList,
            EdgeTraversalOptions = source.EdgeTraversalOptions,
            PolicyModifyState = source.PolicyModifyState,
            IsRuleGroupEnabled = source.IsRuleGroupEnabled,
            ServiceName = source.ServiceName,
            PolicyStoreSource = source.PolicyStoreSource,
            PolicyStoreSourceType = source.PolicyStoreSourceType
        };
    }

    private static FirewallRuleModel CreateFirewallRuleModel(ConnectionSecurityRuleModel rule)
    {
        var model = new FirewallRuleModel
        {
            Name = rule.Name,
            OriginalName = rule.OriginalName,
            Description = rule.Description,
            Enabled = rule.Enabled,
            Direction = FirewallRuleDirection.ConnectionSecurity,
            ProfileDomain = rule.ProfileDomain,
            ProfilePrivate = rule.ProfilePrivate,
            ProfilePublic = rule.ProfilePublic,
            Profile = rule.ProfileDisplay,
            ProfilesMask = rule.ProfilesMask,
            InterfaceTypes = rule.InterfaceTypes,
            LocalPort = string.Equals(rule.LocalPort, "Any", StringComparison.OrdinalIgnoreCase) ? string.Empty : rule.LocalPort,
            RemotePort = string.Equals(rule.RemotePort, "Any", StringComparison.OrdinalIgnoreCase) ? string.Empty : rule.RemotePort,
            ProtocolNumber = int.TryParse(rule.Protocol, out int protocolNumber)
                ? protocolNumber
                : rule.Protocol switch
                {
                    "TCP" => 6,
                    "UDP" => 17,
                    _ => 0
                }
        };

        return model;
    }

    private void PopulateConnectionSecurityFields(ConnectionSecurityRuleModel rule)
    {
        VerificationModeBox.SelectedIndex = rule switch
        {
            { InboundSecurity: ConnectionSecurityRequirement.None, OutboundSecurity: ConnectionSecurityRequirement.None } => 0,
            { InboundSecurity: ConnectionSecurityRequirement.Request, OutboundSecurity: ConnectionSecurityRequirement.Request } => 1,
            { InboundSecurity: ConnectionSecurityRequirement.Require, OutboundSecurity: ConnectionSecurityRequirement.Request } => 2,
            { InboundSecurity: ConnectionSecurityRequirement.Require, OutboundSecurity: ConnectionSecurityRequirement.Require } => 3,
            { InboundSecurity: ConnectionSecurityRequirement.Require, OutboundSecurity: ConnectionSecurityRequirement.None } => 4,
            _ => 0
        };

        VerificationMethodsBox.SelectedIndex = rule.FirstAuthMethods.Count > 0 || rule.SecondAuthMethods.Count > 0 ? 4 : 0;
    }

    private void UpdateConnectionSecurityRuleFromUi()
    {
        if (_connectionSecurityRule is null)
        {
            return;
        }

        _connectionSecurityRule.Name = Rule.Name;
        _connectionSecurityRule.Description = Rule.Description;
        _connectionSecurityRule.Enabled = Rule.Enabled;
        _connectionSecurityRule.ProfileDomain = Rule.ProfileDomain;
        _connectionSecurityRule.ProfilePrivate = Rule.ProfilePrivate;
        _connectionSecurityRule.ProfilePublic = Rule.ProfilePublic;
        _connectionSecurityRule.ProfilesMask =
            (_connectionSecurityRule.ProfileDomain ? 1 : 0) |
            (_connectionSecurityRule.ProfilePrivate ? 2 : 0) |
            (_connectionSecurityRule.ProfilePublic ? 4 : 0);
        _connectionSecurityRule.ProfileDisplay = _connectionSecurityRule.ProfilesMask switch
        {
            7 => LocalizedStrings.WF_Common_All,
            0 => LocalizedStrings.WF_Common_All,
            _ => string.Join(", ", new[]
            {
                _connectionSecurityRule.ProfileDomain ? LocalizedStrings.WF_Profile_Domain : null,
                _connectionSecurityRule.ProfilePrivate ? LocalizedStrings.WF_Profile_Private : null,
                _connectionSecurityRule.ProfilePublic ? LocalizedStrings.WF_Profile_Public : null
            }.Where(value => value is not null))
        };
        _connectionSecurityRule.InterfaceTypes = Rule.InterfaceTypes;
        _connectionSecurityRule.LocalPort = CsrEndpoint1PortBox.SelectedIndex == 1 ? Rule.LocalPort : "Any";
        _connectionSecurityRule.RemotePort = CsrEndpoint2PortBox.SelectedIndex == 1 ? Rule.RemotePort : "Any";

        string? protocolTag = (CsrProtocolTypeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        _connectionSecurityRule.Protocol = protocolTag switch
        {
            "-1" => CsrProtocolNumberBox.Value.ToString("0"),
            "256" or null or "" => "Any",
            "6" => "TCP",
            "17" => "UDP",
            _ => protocolTag
        };

        switch ((VerificationModeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString())
        {
            case "RequestInboundOutbound":
                _connectionSecurityRule.InboundSecurity = ConnectionSecurityRequirement.Request;
                _connectionSecurityRule.OutboundSecurity = ConnectionSecurityRequirement.Request;
                break;
            case "RequireInboundRequestOutbound":
                _connectionSecurityRule.InboundSecurity = ConnectionSecurityRequirement.Require;
                _connectionSecurityRule.OutboundSecurity = ConnectionSecurityRequirement.Request;
                break;
            case "RequireInboundOutbound":
                _connectionSecurityRule.InboundSecurity = ConnectionSecurityRequirement.Require;
                _connectionSecurityRule.OutboundSecurity = ConnectionSecurityRequirement.Require;
                break;
            case "RequireInboundClearOutbound":
                _connectionSecurityRule.InboundSecurity = ConnectionSecurityRequirement.Require;
                _connectionSecurityRule.OutboundSecurity = ConnectionSecurityRequirement.None;
                break;
            default:
                _connectionSecurityRule.InboundSecurity = ConnectionSecurityRequirement.None;
                _connectionSecurityRule.OutboundSecurity = ConnectionSecurityRequirement.None;
                break;
        }

        _connectionSecurityRule.Summary = string.Format(
            CultureInfo.CurrentCulture,
            LocalizedStrings.WF_Summary_EndpointSecurityFormat,
            _connectionSecurityRule.Endpoint1Expression,
            _connectionSecurityRule.Endpoint2Expression,
            GetSecurityRequirementDisplay(_connectionSecurityRule.InboundSecurity),
            GetSecurityRequirementDisplay(_connectionSecurityRule.OutboundSecurity));
    }

    private string GetSecurityRequirementDisplay(ConnectionSecurityRequirement requirement)
        => requirement switch
        {
            ConnectionSecurityRequirement.Request => LocalizedStrings.WF_SecurityRequirement_Request,
            ConnectionSecurityRequirement.Require => LocalizedStrings.WF_SecurityRequirement_Require,
            _ => LocalizedStrings.WF_SecurityRequirement_None
        };

    private string GetExecutableFilesFilter()
        => $"{LocalizedStrings.WF_FileDialog_ExecutableFiles}\0*.exe\0{LocalizedStrings.WF_FileDialog_AllFiles}\0*.*\0";

    private static string BuildAllowOnlySddl(string? source)
    {
        IReadOnlyList<TunnelAuthorizationItem> allowItems = WindowsFirewallSupport.ParseAuthorizationSddl(source, allowEntries: true);
        return WindowsFirewallSupport.BuildAuthorizationSddl(allowItems, null);
    }

    private static string BuildDenyOnlySddl(string? source)
    {
        IReadOnlyList<TunnelAuthorizationItem> denyItems = WindowsFirewallSupport.ParseAuthorizationSddl(source, allowEntries: false);
        return WindowsFirewallSupport.BuildAuthorizationSddl(null, denyItems);
    }

    private static string BuildCombinedSddl(string? allowSource, string? denySource)
    {
        IReadOnlyList<TunnelAuthorizationItem> allowItems = WindowsFirewallSupport.ParseAuthorizationSddl(allowSource, allowEntries: true);
        IReadOnlyList<TunnelAuthorizationItem> denyItems = WindowsFirewallSupport.ParseAuthorizationSddl(denySource, allowEntries: false);
        return WindowsFirewallSupport.BuildAuthorizationSddl(allowItems, denyItems);
    }

    private static int NormalizeSecureFlags(int secureFlags)
    {
        return secureFlags switch
        {
            NetFwAuthenticateNoEncapsulation => NetFwAuthenticateNoEncapsulation,
            NetFwAuthenticateWithIntegrity => NetFwAuthenticateWithIntegrity,
            NetFwAuthenticateAndNegotiateEncryption => NetFwAuthenticateAndNegotiateEncryption,
            NetFwAuthenticateAndEncrypt => NetFwAuthenticateAndEncrypt,
            _ => NetFwAuthenticateWithIntegrity
        };
    }

    private static void CopyAuthMethodResults(
        System.Collections.ObjectModel.ObservableCollection<AuthMethodListItem> target,
        System.Collections.Generic.IReadOnlyList<AuthMethodDialogResult> source)
    {
        target.Clear();
        foreach (AuthMethodDialogResult method in source)
        {
            target.Add(new AuthMethodListItem
            {
                Method = method.Method,
                Details = method.Details,
                Result = method
            });
        }
    }

    private async Task<ContentDialogResult> ShowDeleteConfirmationDialogAsync()
    {
        string ruleName = string.IsNullOrWhiteSpace(Rule.DisplayName) ? Rule.Name : Rule.DisplayName;
        var dialog = new ContentDialog
        {
            Title = LocalizedStrings.WF_DeleteRule_ConfirmationTitle,
            Content = string.Format(
                CultureInfo.CurrentCulture,
                LocalizedStrings.WF_DeleteRule_ConfirmationMessage,
                ruleName),
            PrimaryButtonText = LocalizedStrings.WF_DeleteRule_ConfirmButton,
            CloseButtonText = LocalizedStrings.Common_CancelButton,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            RequestedTheme = App.CurrentTheme,
            XamlRoot = XamlRoot
        };

        return await dialog.ShowAsync();
    }

    private Task ShowValidationDialogAsync(string message)
        => ShowErrorDialogAsync(LocalizedStrings.Common_ErrorTitle, message);

    private void OnFirewallRulesChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            await RefreshRuleStatusFromSystemChangeAsync();
        });
    }

    private async Task RefreshRuleStatusFromSystemChangeAsync(bool includeMutableState = false)
    {
        if (_isRefreshingFromSystemChange || string.IsNullOrWhiteSpace(Rule.Name))
        {
            return;
        }

        _isRefreshingFromSystemChange = true;
        try
        {
            if (_connectionSecurityRule is not null)
            {
                var connectionSecurityService = App.GetRequiredService<ConnectionSecurityService>();
                string lookupName = GetConnectionSecurityRuleLookupName(_connectionSecurityRule);
                ConnectionSecurityRuleModel? latestRule = await Task.Run(() => connectionSecurityService.GetRule(lookupName));
                if (latestRule is null)
                {
                    MarkRuleUnavailable();
                    return;
                }

                _connectionSecurityRule.OriginalName = latestRule.OriginalName;
                _connectionSecurityRule.Enabled = latestRule.Enabled;
                Rule.Enabled = latestRule.Enabled;
                RestoreRuleAvailabilityUi();
                UpdateDisableButton();
                return;
            }

            var firewallRuleService = App.GetRequiredService<WindowsFirewallRuleService>();
            FirewallRuleModel? latest = await Task.Run(() => firewallRuleService.GetRule(GetFirewallRuleLookupName(Rule)));
            if (latest is null)
            {
                MarkRuleUnavailable();
                return;
            }

            Rule.OriginalName = latest.OriginalName;
            Rule.Enabled = latest.Enabled;
            Rule.PolicyModifyState = latest.PolicyModifyState;
            Rule.IsRuleGroupEnabled = latest.IsRuleGroupEnabled;
            if (includeMutableState)
            {
                ApplyLatestFirewallRuleMutableState(latest, firewallRuleService);
            }

            RestoreRuleAvailabilityUi();
            UpdateDisableButton();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh Windows Firewall rule details after a system change.");
        }
        finally
        {
            _isRefreshingFromSystemChange = false;
        }
    }

    private void ApplyLatestFirewallRuleMutableState(
        FirewallRuleModel latest,
        WindowsFirewallRuleService firewallRuleService)
    {
        Rule.Action = latest.Action;
        Rule.ConnectionAction = latest.ConnectionAction;
        Rule.LocalAddress = latest.LocalAddress;
        Rule.RemoteAddress = latest.RemoteAddress;
        Rule.ProfilesMask = latest.ProfilesMask;
        Rule.Profile = latest.Profile;
        Rule.InterfaceTypes = latest.InterfaceTypes;
        Rule.Interfaces = latest.Interfaces;
        Rule.SecureFlags = latest.SecureFlags;
        Rule.EdgeTraversalOptions = latest.EdgeTraversalOptions;
        Rule.EdgeTraversal = latest.EdgeTraversal;
        Rule.LocalUserAuthorizedList = latest.LocalUserAuthorizedList;
        Rule.LocalUserOwner = latest.LocalUserOwner;
        Rule.RemoteMachineAuthorizedList = latest.RemoteMachineAuthorizedList;
        Rule.RemoteUserAuthorizedList = latest.RemoteUserAuthorizedList;

        if (latest.ConnectionAction == FirewallConnectionAction.AllowIfSecure &&
            firewallRuleService.TryGetOverrideBlockRules(GetFirewallRuleLookupNames(latest), out bool overrideBlockRules))
        {
            _allowIfSecureOverrideBlockRules = overrideBlockRules;
            Rule.OverrideBlockRules = overrideBlockRules;
            return;
        }

        _allowIfSecureOverrideBlockRules = false;
        Rule.OverrideBlockRules = false;
    }

    private void MarkRuleUnavailable()
    {
        ApplyButton.IsEnabled = false;
        DisableButton.IsEnabled = false;
        DeleteMenuItem.IsEnabled = false;
        PredefinedRuleInfoBar.Severity = InfoBarSeverity.Warning;
        PredefinedRuleInfoBar.Title = LocalizedStrings.WF_RuleUnavailable_Title;
        PredefinedRuleInfoBar.Message = LocalizedStrings.WF_RuleUnavailable_Message;
        PredefinedRuleInfoBar.IsOpen = true;
    }

    private void RestoreRuleAvailabilityUi()
    {
        ApplyButton.IsEnabled = true;
        DisableButton.IsEnabled = true;
        DeleteMenuItem.IsEnabled = true;

        if (_connectionSecurityRule is null && Rule.IsPredefined)
        {
            PredefinedRuleInfoBar.Severity = InfoBarSeverity.Informational;
            PredefinedRuleInfoBar.Title = LocalizedStrings.WF_PredefinedRule_InfoBar_Title;
            PredefinedRuleInfoBar.Message = LocalizedStrings.WF_PredefinedRule_InfoBar_Message;
            PredefinedRuleInfoBar.IsOpen = true;
            return;
        }

        PredefinedRuleInfoBar.IsOpen = false;
    }

    private static string GetFirewallRuleLookupName(FirewallRuleModel rule)
        => string.IsNullOrWhiteSpace(rule.OriginalName) ? rule.Name : rule.OriginalName;

    private static string?[] GetFirewallRuleLookupNames(FirewallRuleModel rule)
        => [rule.OriginalName, rule.Name, rule.DisplayName];

    private static string GetConnectionSecurityRuleLookupName(ConnectionSecurityRuleModel rule)
        => string.IsNullOrWhiteSpace(rule.OriginalName) ? rule.Name : rule.OriginalName;

    private async Task ShowErrorDialogAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = LocalizedStrings.Common_OKButton,
            RequestedTheme = App.CurrentTheme,
            XamlRoot = XamlRoot
        };

        await dialog.ShowAsync();
    }
}

