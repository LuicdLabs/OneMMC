using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OneMMC.Core.Features.SystemManagement.Models.WF.Authentication;
using OneMMC.Core.Features.SystemManagement.Models.WF.ConnectionSecurity;
using OneMMC.Core.Features.SystemManagement.Models.WF.Monitoring;
using OneMMC.Core.Features.SystemManagement.Models.WF.Profiles;
using OneMMC.Core.Features.SystemManagement.Models.WF.Rules;
using OneMMC.Core.Features.SystemManagement.Services.WF.ConnectionSecurity;
using OneMMC.Core.Features.SystemManagement.Services.WF.Monitoring;
using OneMMC.Core.Features.SystemManagement.Services.WF.Profiles;
using OneMMC.Core.Features.SystemManagement.Services.WF.Rules;
using OneMMC.Helpers;
using OneMMC.Localization;
using OneMMC.Views.Dialogs.ConnectionSecurity;
using OneMMC.Views.Dialogs.Network;
using OneMMC.Views.Dialogs.Scope;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OneMMC.Views.Dialogs.NewRule;

public sealed partial class NewFirewallRuleDialog : ContentDialog
{
    private const int NetFwAuthenticateNone = 0;
    private const int NetFwAuthenticateNoEncapsulation = 1;
    private const int NetFwAuthenticateWithIntegrity = 2;
    private const int NetFwAuthenticateAndNegotiateEncryption = 3;
    private const int NetFwAuthenticateAndEncrypt = 4;

    private readonly WindowsFirewallService _firewallService;
    private readonly FirewallRuleDirection _direction;

    private IReadOnlyList<PredefinedFirewallRuleGroup> _predefinedGroups = [];
    private readonly List<FirewallRuleModel> _createdRules = [];

    private string _servicesExpression = string.Empty;
    private string _scopeLocalAddress = "*";
    private string _scopeRemoteAddress = "*";
    private int _allowIfSecureSecureFlags = NetFwAuthenticateWithIntegrity;
    private bool _allowIfSecureOverrideBlockRules;
    private string _customIcmpTypesAndCodes = string.Empty;
    private bool _isInitializing = true;

    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    public IReadOnlyList<FirewallRuleModel> CreatedRules => _createdRules;

    public NewFirewallRuleDialog(FirewallRuleDirection direction, XamlRoot xamlRoot)
    {
        _direction = direction;
        _firewallService = App.GetRequiredService<WindowsFirewallService>();

        InitializeComponent();
        XamlRoot = xamlRoot;
        RequestedTheme = App.CurrentTheme;
        Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style;

        PrimaryButtonClick += OnPrimaryButtonClick;

        LoadPredefinedGroups();
        UpdateSectionVisibility();
        UpdateProgramPathVisibility();
        UpdateCustomProgramPathVisibility();
        UpdatePortInputVisibility();
        UpdateCustomPortInputVisibility();
        UpdateCustomProtocolState();
        UpdateActionCustomizationVisibility();

        _isInitializing = false;
    }

    public async Task<ContentDialogResult> ShowAsync(XamlRoot xamlRoot)
    {
        XamlRoot = xamlRoot;
        return await ShowAsync();
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            ClearValidationError();
            _createdRules.Clear();

            bool success = TryBuildRules();
            if (!success)
            {
                args.Cancel = true;
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void LoadPredefinedGroups()
    {
        _predefinedGroups = _firewallService.GetPredefinedRuleGroups(_direction);
        PredefinedGroupBox.ItemsSource = _predefinedGroups;
        if (_predefinedGroups.Count > 0)
        {
            PredefinedGroupBox.SelectedIndex = 0;
            PredefinedRulesListView.ItemsSource = _predefinedGroups[0].Rules;
        }
        else
        {
            PredefinedRulesListView.ItemsSource = null;
        }
    }

    private bool TryBuildRules()
    {
        string typeTag = GetSelectedTag(RuleTypeBox);
        if (string.Equals(typeTag, "Predefined", StringComparison.Ordinal))
        {
            return BuildPredefinedRules();
        }

        if (!ValidateCommonInputs(typeTag))
        {
            return false;
        }

        var connectionAction = GetConnectionAction();
        var rule = new FirewallRuleModel
        {
            Name = RuleNameTextBox.Text.Trim(),
            Description = RuleDescriptionTextBox.Text.Trim(),
            Enabled = true,
            Direction = _direction,
            Action = connectionAction == FirewallConnectionAction.Block ? FirewallRuleAction.Block : FirewallRuleAction.Allow,
            ConnectionAction = connectionAction,
            SecureFlags = connectionAction == FirewallConnectionAction.AllowIfSecure
                ? NormalizeSecureFlags(_allowIfSecureSecureFlags)
                : NetFwAuthenticateNone,
            OverrideBlockRules = connectionAction == FirewallConnectionAction.AllowIfSecure &&
                                 _allowIfSecureOverrideBlockRules,
            ProfileDomain = DomainCheckBox.IsChecked == true,
            ProfilePrivate = PrivateCheckBox.IsChecked == true,
            ProfilePublic = PublicCheckBox.IsChecked == true,
            Profile = BuildProfileDisplay(),
            LocalAddress = "*",
            RemoteAddress = "*"
        };

        switch (typeTag)
        {
            case "Program":
                BuildProgramRule(rule);
                break;
            case "Port":
                BuildPortRule(rule);
                break;
            default:
                BuildCustomRule(rule);
                break;
        }

        _createdRules.Add(rule);
        return true;
    }

    private bool BuildPredefinedRules()
    {
        if (PredefinedGroupBox.SelectedItem is not PredefinedFirewallRuleGroup group)
        {
            ShowValidationError(LocalizedStrings.WF_Validation_PredefinedRuleGroupRequired);
            return false;
        }

        var selectedRules = group.Rules.Where(rule => rule.IsSelected).ToList();
        if (selectedRules.Count == 0)
        {
            ShowValidationError(LocalizedStrings.WF_Validation_PredefinedRuleRequired);
            return false;
        }

        var connectionAction = GetConnectionAction();
        foreach (var selected in selectedRules)
        {
            _createdRules.Add(new FirewallRuleModel
            {
                Name = selected.RuleName,
                Description = selected.Description,
                Enabled = true,
                Direction = _direction,
                Grouping = group.GroupKey,
                Action = connectionAction == FirewallConnectionAction.Block ? FirewallRuleAction.Block : FirewallRuleAction.Allow,
                ConnectionAction = connectionAction,
                OverrideBlockRules = connectionAction == FirewallConnectionAction.AllowIfSecure &&
                                     _allowIfSecureOverrideBlockRules,
                DisplayDescription = string.IsNullOrWhiteSpace(selected.Service)
                    ? selected.Description
                    : $"{selected.Description}{Environment.NewLine}{selected.Service}"
            });
        }

        return true;
    }

    private bool ValidateCommonInputs(string typeTag)
    {
        if (string.IsNullOrWhiteSpace(RuleNameTextBox.Text))
        {
            ShowValidationError(LocalizedStrings.WF_Validation_RuleNameRequired);
            return false;
        }

        if (DomainCheckBox.IsChecked != true && PrivateCheckBox.IsChecked != true && PublicCheckBox.IsChecked != true)
        {
            ShowValidationError(LocalizedStrings.WF_Validation_ProfileRequired);
            return false;
        }

        if (string.Equals(typeTag, "Program", StringComparison.Ordinal) &&
            string.Equals(GetSelectedTag(ProgramScopeBox), "This", StringComparison.Ordinal) &&
            string.IsNullOrWhiteSpace(ProgramPathTextBox.Text))
        {
            ShowValidationError(LocalizedStrings.WF_Validation_ProgramPathRequired);
            return false;
        }

        if (string.Equals(typeTag, "Port", StringComparison.Ordinal) &&
            string.Equals(GetSelectedTag(PortLocalPortBox), "SpecificPorts", StringComparison.Ordinal) &&
            string.IsNullOrWhiteSpace(PortInputTextBox.Text))
        {
            ShowValidationError(LocalizedStrings.WF_Validation_LocalPortsRequired);
            return false;
        }

        if (string.Equals(typeTag, "Custom", StringComparison.Ordinal))
        {
            if (string.Equals(GetSelectedTag(CustomProgramScopeBox), "This", StringComparison.Ordinal) &&
                string.IsNullOrWhiteSpace(CustomProgramPathTextBox.Text))
            {
                ShowValidationError(LocalizedStrings.WF_Validation_ProgramPathRequired);
                return false;
            }

            if (string.Equals(GetSelectedTag(CustomLocalPortBox), "SpecificPorts", StringComparison.Ordinal) &&
                string.IsNullOrWhiteSpace(CustomLocalPortInput.Text))
            {
                ShowValidationError(LocalizedStrings.WF_Validation_LocalPortsRequired);
                return false;
            }

            if (string.Equals(GetSelectedTag(CustomRemotePortBox), "SpecificPorts", StringComparison.Ordinal) &&
                string.IsNullOrWhiteSpace(CustomRemotePortInput.Text))
            {
                ShowValidationError(LocalizedStrings.WF_Validation_RemotePortsRequired);
                return false;
            }
        }

        return true;
    }

    private void BuildProgramRule(FirewallRuleModel rule)
    {
        if (string.Equals(GetSelectedTag(ProgramScopeBox), "This", StringComparison.Ordinal))
        {
            rule.Program = ProgramPathTextBox.Text.Trim();
        }

        rule.Protocol = FirewallRuleProtocol.Any;
        rule.ProtocolNumber = 0;
        rule.LocalPortOption = FirewallPortOption.AllPorts;
        rule.RemotePortOption = FirewallPortOption.AllPorts;
    }

    private void BuildPortRule(FirewallRuleModel rule)
    {
        string protocolTag = GetSelectedTag(PortProtocolTypeBox);
        rule.Protocol = string.Equals(protocolTag, "UDP", StringComparison.Ordinal)
            ? FirewallRuleProtocol.UDP
            : FirewallRuleProtocol.TCP;
        rule.ProtocolNumber = rule.Protocol == FirewallRuleProtocol.UDP ? 17 : 6;

        if (string.Equals(GetSelectedTag(PortLocalPortBox), "SpecificPorts", StringComparison.Ordinal))
        {
            rule.LocalPortOption = FirewallPortOption.SpecificPorts;
            rule.LocalPort = PortInputTextBox.Text.Trim();
        }
        else
        {
            rule.LocalPortOption = FirewallPortOption.AllPorts;
            rule.LocalPort = string.Empty;
        }

        rule.RemotePortOption = FirewallPortOption.AllPorts;
        rule.RemotePort = string.Empty;
    }

    private void BuildCustomRule(FirewallRuleModel rule)
    {
        if (string.Equals(GetSelectedTag(CustomProgramScopeBox), "This", StringComparison.Ordinal))
        {
            rule.Program = CustomProgramPathTextBox.Text.Trim();
        }

        rule.Services = _servicesExpression;

        string protocolTag = GetSelectedTag(CustomProtocolTypeBox);
        rule.Protocol = ParseProtocol(protocolTag);
        rule.ProtocolNumber = ResolveProtocolNumber(rule.Protocol, (int)CustomProtocolNumberBox.Value);

        bool tcpOrUdp = rule.Protocol is FirewallRuleProtocol.TCP or FirewallRuleProtocol.UDP;

        if (tcpOrUdp && string.Equals(GetSelectedTag(CustomLocalPortBox), "SpecificPorts", StringComparison.Ordinal))
        {
            rule.LocalPortOption = FirewallPortOption.SpecificPorts;
            rule.LocalPort = CustomLocalPortInput.Text.Trim();
        }
        else
        {
            rule.LocalPortOption = FirewallPortOption.AllPorts;
            rule.LocalPort = string.Empty;
        }

        if (tcpOrUdp && string.Equals(GetSelectedTag(CustomRemotePortBox), "SpecificPorts", StringComparison.Ordinal))
        {
            rule.RemotePortOption = FirewallPortOption.SpecificPorts;
            rule.RemotePort = CustomRemotePortInput.Text.Trim();
        }
        else
        {
            rule.RemotePortOption = FirewallPortOption.AllPorts;
            rule.RemotePort = string.Empty;
        }

        rule.LocalAddress = _scopeLocalAddress;
        rule.RemoteAddress = _scopeRemoteAddress;
        rule.IcmpTypesAndCodes = rule.Protocol is FirewallRuleProtocol.ICMPv4 or FirewallRuleProtocol.ICMPv6
            ? _customIcmpTypesAndCodes
            : string.Empty;
    }

    private FirewallConnectionAction GetConnectionAction()
    {
        return GetSelectedTag(ActionBox) switch
        {
            "AllowIfSecure" => FirewallConnectionAction.AllowIfSecure,
            "Block" => FirewallConnectionAction.Block,
            _ => FirewallConnectionAction.Allow
        };
    }

    private string BuildProfileDisplay()
    {
        var profiles = new List<string>();
        if (DomainCheckBox.IsChecked == true) profiles.Add(LocalizedStrings.WF_Profile_Domain);
        if (PrivateCheckBox.IsChecked == true) profiles.Add(LocalizedStrings.WF_Profile_Private);
        if (PublicCheckBox.IsChecked == true) profiles.Add(LocalizedStrings.WF_Profile_Public);
        return profiles.Count == 3 ? LocalizedStrings.WF_Common_All : string.Join(", ", profiles);
    }

    private static FirewallRuleProtocol ParseProtocol(string protocolTag)
    {
        return protocolTag switch
        {
            "TCP" => FirewallRuleProtocol.TCP,
            "UDP" => FirewallRuleProtocol.UDP,
            "Custom" => FirewallRuleProtocol.Custom,
            "ICMPv4" => FirewallRuleProtocol.ICMPv4,
            "ICMPv6" => FirewallRuleProtocol.ICMPv6,
            _ => FirewallRuleProtocol.Any
        };
    }

    private static int ResolveProtocolNumber(FirewallRuleProtocol protocol, int customNumber)
    {
        return protocol switch
        {
            FirewallRuleProtocol.TCP => 6,
            FirewallRuleProtocol.UDP => 17,
            FirewallRuleProtocol.ICMPv4 => 1,
            FirewallRuleProtocol.ICMPv6 => 58,
            FirewallRuleProtocol.Custom => customNumber,
            _ => 0
        };
    }

    private static string GetSelectedTag(ComboBox comboBox)
    {
        return (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;
    }

    private async void BrowseProgramButton_Click(object sender, RoutedEventArgs e)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        string? path = await App.GetRequiredService<OneMMC.Core.Abstractions.Services.IFileDialogService>().OpenFileAsync(
            hwnd,
            filter: GetExecutableFilesFilter(),
            title: LocalizedStrings.WF_FileDialog_SelectProgramTitle);

        if (path is not null)
        {
            ProgramPathTextBox.Text = path;
        }
    }

    private async void BrowseCustomProgramButton_Click(object sender, RoutedEventArgs e)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        string? path = await App.GetRequiredService<OneMMC.Core.Abstractions.Services.IFileDialogService>().OpenFileAsync(
            hwnd,
            filter: GetExecutableFilesFilter(),
            title: LocalizedStrings.WF_FileDialog_SelectProgramTitle);

        if (path is not null)
        {
            CustomProgramPathTextBox.Text = path;
        }
    }

    private async void CustomizeAllowIfSecureButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CustomizeAllowIfSecureDialog();
        dialog.ConfigureEncryptionNegotiationOption(_direction == FirewallRuleDirection.Inbound);
        dialog.ApplySelection(_allowIfSecureSecureFlags, _allowIfSecureOverrideBlockRules);

        if (await dialog.ShowDialogAsync(XamlRoot) == WindowDialogResult.Primary)
        {
            _allowIfSecureSecureFlags = dialog.SelectedSecureFlags;
            _allowIfSecureOverrideBlockRules = dialog.OverrideBlockRules;
        }
    }

    private async void SpecifyServicesButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ServicesDialog();
        WindowDialogResult result = await dialog.ShowDialogAsync(XamlRoot);
        if (result != WindowDialogResult.Primary)
        {
            return;
        }

        if (dialog.ApplyToAllServices)
        {
            _servicesExpression = string.Empty;
            return;
        }

        if (dialog.ApplyOnlyToServices)
        {
            _servicesExpression = "*";
            return;
        }

        if (dialog.ApplyToSpecificService)
        {
            _servicesExpression = dialog.SelectedService?.ShortName ?? string.Empty;
            return;
        }

        _servicesExpression = dialog.SelectedServiceShortName.Trim();
    }

    private async void ConfigureScopeButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ScopeIPAddressDialog();
        dialog.ApplyAddressExpressions(_scopeLocalAddress, _scopeRemoteAddress);
        WindowDialogResult result = await dialog.ShowDialogAsync(XamlRoot);
        if (result == WindowDialogResult.Primary)
        {
            _scopeLocalAddress = dialog.LocalAddressExpression;
            _scopeRemoteAddress = dialog.RemoteAddressExpression;
        }
    }

    private async void IcmpCustomizeButton_Click(object sender, RoutedEventArgs e)
    {
        var content = new ICMPSettingsDialog();
        content.ApplyIcmpTypesAndCodes(_customIcmpTypesAndCodes);

        var dialog = new ModalDialogWindow(new ModalDialogOptions
        {
            Title = LocalizedStrings.WF_CustomizeIcmpSettings_Title,
            Content = content,
            OwnerXamlRoot = XamlRoot,
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
            _customIcmpTypesAndCodes = content.IcmpTypesAndCodesExpression;
        }
    }

    private void RuleTypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        UpdateSectionVisibility();
        ClearValidationError();
    }

    private void ProgramScopeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        UpdateProgramPathVisibility();
    }

    private void PortLocalPortBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        UpdatePortInputVisibility();
    }

    private void PredefinedGroupBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (PredefinedGroupBox.SelectedItem is PredefinedFirewallRuleGroup selectedGroup)
        {
            PredefinedRulesListView.ItemsSource = selectedGroup.Rules;
        }
    }

    private void CustomProgramScopeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        UpdateCustomProgramPathVisibility();
    }

    private void CustomProtocolTypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        UpdateCustomProtocolState();
    }

    private void CustomLocalPortBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        UpdateCustomPortInputVisibility();
    }

    private void CustomRemotePortBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        UpdateCustomPortInputVisibility();
    }

    private void ActionBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        UpdateActionCustomizationVisibility();
    }

    private void UpdateSectionVisibility()
    {
        string typeTag = GetSelectedTag(RuleTypeBox);

        ProgramSection.Visibility = string.Equals(typeTag, "Program", StringComparison.Ordinal)
            ? Visibility.Visible : Visibility.Collapsed;
        PortSection.Visibility = string.Equals(typeTag, "Port", StringComparison.Ordinal)
            ? Visibility.Visible : Visibility.Collapsed;
        PredefinedSection.Visibility = string.Equals(typeTag, "Predefined", StringComparison.Ordinal)
            ? Visibility.Visible : Visibility.Collapsed;
        CustomSection.Visibility = string.Equals(typeTag, "Custom", StringComparison.Ordinal)
            ? Visibility.Visible : Visibility.Collapsed;

        bool showShared = !string.Equals(typeTag, "Predefined", StringComparison.Ordinal);
        ProfileSection.Visibility = showShared ? Visibility.Visible : Visibility.Collapsed;
        NameSection.Visibility = showShared ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateProgramPathVisibility()
    {
        ProgramPathGrid.Visibility = string.Equals(GetSelectedTag(ProgramScopeBox), "This", StringComparison.Ordinal)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateCustomProgramPathVisibility()
    {
        CustomProgramPathGrid.Visibility = string.Equals(GetSelectedTag(CustomProgramScopeBox), "This", StringComparison.Ordinal)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdatePortInputVisibility()
    {
        bool showInput = string.Equals(GetSelectedTag(PortLocalPortBox), "SpecificPorts", StringComparison.Ordinal);
        PortInputLabel.Visibility = showInput ? Visibility.Visible : Visibility.Collapsed;
        PortInputTextBox.Visibility = showInput ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateCustomPortInputVisibility()
    {
        bool showLocalInput = string.Equals(GetSelectedTag(CustomLocalPortBox), "SpecificPorts", StringComparison.Ordinal);
        CustomLocalInputLabel.Visibility = showLocalInput ? Visibility.Visible : Visibility.Collapsed;
        CustomLocalPortInput.Visibility = showLocalInput ? Visibility.Visible : Visibility.Collapsed;

        bool showRemoteInput = string.Equals(GetSelectedTag(CustomRemotePortBox), "SpecificPorts", StringComparison.Ordinal);
        CustomRemoteInputLabel.Visibility = showRemoteInput ? Visibility.Visible : Visibility.Collapsed;
        CustomRemotePortInput.Visibility = showRemoteInput ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateCustomProtocolState()
    {
        string protocolTag = GetSelectedTag(CustomProtocolTypeBox);
        bool isCustomProtocol = string.Equals(protocolTag, "Custom", StringComparison.Ordinal);
        bool isIcmp = string.Equals(protocolTag, "ICMPv4", StringComparison.Ordinal) ||
                      string.Equals(protocolTag, "ICMPv6", StringComparison.Ordinal);
        bool tcpOrUdp = string.Equals(protocolTag, "TCP", StringComparison.Ordinal) ||
                        string.Equals(protocolTag, "UDP", StringComparison.Ordinal);

        CustomProtocolNumberBox.IsEnabled = isCustomProtocol;
        IcmpCustomizeButton.IsEnabled = isIcmp;
        CustomLocalPortBox.IsEnabled = tcpOrUdp;
        CustomRemotePortBox.IsEnabled = tcpOrUdp;

        if (!tcpOrUdp)
        {
            CustomLocalPortBox.SelectedIndex = 0;
            CustomRemotePortBox.SelectedIndex = 0;
        }

        UpdateCustomPortInputVisibility();
    }

    private void UpdateActionCustomizationVisibility()
    {
        CustomizeAllowIfSecureGrid.Visibility = string.Equals(GetSelectedTag(ActionBox), "AllowIfSecure", StringComparison.Ordinal)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowValidationError(string message)
    {
        ValidationErrorTextBlock.Text = message;
        ValidationErrorTextBlock.Visibility = Visibility.Visible;
    }

    private void ClearValidationError()
    {
        ValidationErrorTextBlock.Text = string.Empty;
        ValidationErrorTextBlock.Visibility = Visibility.Collapsed;
    }

    private string GetExecutableFilesFilter()
        => $"{LocalizedStrings.WF_FileDialog_ExecutableFiles}\0*.exe\0{LocalizedStrings.WF_FileDialog_AllFiles}\0*.*\0";

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
}

