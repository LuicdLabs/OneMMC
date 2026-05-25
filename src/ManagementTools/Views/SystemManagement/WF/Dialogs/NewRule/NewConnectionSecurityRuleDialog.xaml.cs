using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Authentication;
using ManagementTools.Core.Features.SystemManagement.Models.WF.ConnectionSecurity;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Monitoring;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Profiles;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Rules;
using ManagementTools.Views.Dialogs.Authentication;
using ManagementTools.Helpers;
using ManagementTools.Localization;
using ManagementTools.Views.Dialogs.ConnectionSecurity;
using ManagementTools.Views.Dialogs.Network;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ManagementTools.Views.Dialogs.NewRule;

public sealed partial class NewConnectionSecurityRuleDialog : ContentDialog
{
    private readonly ObservableCollection<string> _exemptComputers = [];

    private string _endpoint1Expression = "Any";
    private string _endpoint2Expression = "Any";
    private bool _endpoint1UsesAnyAddress = true;
    private bool _endpoint2UsesAnyAddress = true;
    private string _interfaceTypes = "Any";
    private string _tunnelLocalIpv4 = string.Empty;
    private string _tunnelLocalIpv6 = string.Empty;
    private string _tunnelRemoteIpv4 = string.Empty;
    private string _tunnelRemoteIpv6 = string.Empty;
    private bool _applyTunnelAuthorization;
    private IReadOnlyList<AuthMethodDialogResult> _firstAuthMethods = [];
    private IReadOnlyList<AuthMethodDialogResult> _secondAuthMethods = [];
    private bool _isFirstAuthOptional;
    private bool _isSecondAuthOptional;
    private bool _isInitializing = true;

    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    public NewConnectionSecurityRuleDialogResult? Result { get; private set; }

    public NewConnectionSecurityRuleDialog(XamlRoot xamlRoot)
    {
        InitializeComponent();
        XamlRoot = xamlRoot;
        RequestedTheme = App.CurrentTheme;
        Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style;

        PrimaryButtonClick += OnPrimaryButtonClick;

        ExemptComputersListView.ItemsSource = _exemptComputers;

        UpdateTunnelTypeDescription();
        UpdateTypeUI();
        UpdateInterfaceTypesSummary();
        UpdateEndpointPortInputVisibility();
        UpdateProtocolState();

        _isInitializing = false;
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            ClearValidationError();
            bool success = TryBuildResult();
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

    private bool TryBuildResult()
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

        string typeTag = GetSelectedTag(RuleTypeBox);
        if (string.Equals(typeTag, "AuthenticationExemption", StringComparison.Ordinal) && _exemptComputers.Count == 0)
        {
            ShowValidationError(LocalizedStrings.WF_Validation_ExemptComputerRequired);
            return false;
        }

        if (string.Equals(typeTag, "ServerToServer", StringComparison.Ordinal) &&
            ((!_endpoint1UsesAnyAddress && string.IsNullOrWhiteSpace(_endpoint1Expression)) ||
             (!_endpoint2UsesAnyAddress && string.IsNullOrWhiteSpace(_endpoint2Expression))))
        {
            ShowValidationError(LocalizedStrings.WF_Validation_EndpointsRequired);
            return false;
        }

        if (ProtocolsAndPortsSection.Visibility == Visibility.Visible)
        {
            if (string.Equals(GetSelectedTag(Endpoint1PortBox), "SpecificPorts", StringComparison.Ordinal) &&
                string.IsNullOrWhiteSpace(Endpoint1PortInputTextBox.Text))
            {
                ShowValidationError(LocalizedStrings.WF_Validation_Endpoint1PortsRequired);
                return false;
            }

            if (string.Equals(GetSelectedTag(Endpoint2PortBox), "SpecificPorts", StringComparison.Ordinal) &&
                string.IsNullOrWhiteSpace(Endpoint2PortInputTextBox.Text))
            {
                ShowValidationError(LocalizedStrings.WF_Validation_Endpoint2PortsRequired);
                return false;
            }
        }

        string requirementsTag = RequirementsSection.Visibility == Visibility.Visible
            ? GetSelectedTag(RequirementsBox)
            : string.Empty;
        string authenticationMethodTag = AuthenticationMethodSection.Visibility == Visibility.Visible
            ? GetSelectedTag(AuthenticationMethodBox)
            : string.Empty;
        bool useAdvancedAuthentication = string.Equals(authenticationMethodTag, "Advanced", StringComparison.Ordinal);

        if (string.Equals(authenticationMethodTag, "ComputerCertificate", StringComparison.Ordinal) &&
            string.IsNullOrWhiteSpace(CertificateAuthorityNameTextBox.Text))
        {
            ShowValidationError(LocalizedStrings.WF_Validation_CertificateAuthorityPathRequired);
            return false;
        }

        if (useAdvancedAuthentication && _firstAuthMethods.Count == 0 && _secondAuthMethods.Count == 0)
        {
            ShowValidationError(LocalizedStrings.WF_Validation_CustomAuthenticationRequired);
            return false;
        }

        if (string.Equals(typeTag, "Tunnel", StringComparison.Ordinal) &&
            !ValidateTunnelSelection(authenticationMethodTag))
        {
            return false;
        }

        bool hasSpecificInterfaceTypes = !string.Equals(_interfaceTypes, "Any", StringComparison.OrdinalIgnoreCase) &&
                                         !string.Equals(_interfaceTypes, "All", StringComparison.OrdinalIgnoreCase);
        if (hasSpecificInterfaceTypes &&
            (!_endpoint1UsesAnyAddress || !_endpoint2UsesAnyAddress))
        {
            ShowValidationError(LocalizedStrings.WF_Validation_AddressInterfaceConflict);
            return false;
        }

        Result = new NewConnectionSecurityRuleDialogResult
        {
            Type = typeTag,
            Name = RuleNameTextBox.Text.Trim(),
            Description = RuleDescriptionTextBox.Text.Trim(),
            ProfileDomain = DomainCheckBox.IsChecked == true,
            ProfilePrivate = PrivateCheckBox.IsChecked == true,
            ProfilePublic = PublicCheckBox.IsChecked == true,
            InterfaceTypes = _interfaceTypes,
            RequirementsTag = requirementsTag,
            Requirements = RequirementsSection.Visibility == Visibility.Visible
                ? GetSelectedContent(RequirementsBox)
                : string.Empty,
            AuthenticationMethodTag = authenticationMethodTag,
            AuthenticationMethod = AuthenticationMethodSection.Visibility == Visibility.Visible
                ? GetSelectedContent(AuthenticationMethodBox)
                : string.Empty,
            Endpoint1Expression = _endpoint1Expression,
            Endpoint2Expression = _endpoint2Expression,
            ProtocolTag = GetSelectedTag(ProtocolTypeBox),
            ProtocolNumber = (int)ProtocolNumberBox.Value,
            Endpoint1PortModeTag = GetSelectedTag(Endpoint1PortBox),
            Endpoint1Ports = Endpoint1PortInputTextBox.Text.Trim(),
            Endpoint2PortModeTag = GetSelectedTag(Endpoint2PortBox),
            Endpoint2Ports = Endpoint2PortInputTextBox.Text.Trim(),
            TunnelType = GetSelectedTag(TunnelTypeBox),
            TunnelKeyModule = GetTunnelKeyModule(),
            ExemptIpsecConnectionsFromTunnel = TunnelExemptYesRadio.IsChecked == true,
            ExemptComputers = _exemptComputers.ToList(),
            TunnelLocalIpv4 = _tunnelLocalIpv4,
            TunnelLocalIpv6 = _tunnelLocalIpv6,
            TunnelRemoteIpv4 = _tunnelRemoteIpv4,
            TunnelRemoteIpv6 = _tunnelRemoteIpv6,
            ApplyTunnelAuthorization = _applyTunnelAuthorization,
            SigningAlgorithmTag = GetSelectedTag(SigningAlgorithmBox),
            CertificateStoreTypeTag = GetSelectedTag(CertificateStoreTypeBox),
            CertificateAuthorityName = CertificateAuthorityNameTextBox.Text.Trim(),
            AcceptHealthCertificatesOnly = AcceptHealthCertificatesCheckBox.IsChecked == true,
            FirstAuthMethods = useAdvancedAuthentication ? _firstAuthMethods : [],
            SecondAuthMethods = useAdvancedAuthentication ? _secondAuthMethods : [],
            IsFirstAuthOptional = useAdvancedAuthentication && _isFirstAuthOptional,
            IsSecondAuthOptional = useAdvancedAuthentication && _isSecondAuthOptional,
            Summary = BuildSummary(typeTag)
        };

        return true;
    }

    private string BuildSummary(string typeTag)
    {
        return typeTag switch
        {
            "Isolation" => string.Format(
                CultureInfo.CurrentCulture,
                LocalizedStrings.WF_Summary_RequirementsAuthenticationFormat,
                GetSelectedContent(RequirementsBox),
                GetSelectedContent(AuthenticationMethodBox)),
            "AuthenticationExemption" => _exemptComputers.Count == 0
                ? LocalizedStrings.WF_Summary_NoExemptComputers
                : string.Format(CultureInfo.CurrentCulture, LocalizedStrings.WF_Summary_ExemptComputersFormat, string.Join(", ", _exemptComputers)),
            "ServerToServer" => string.Format(CultureInfo.CurrentCulture, LocalizedStrings.WF_Summary_EndpointPairFormat, _endpoint1Expression, _endpoint2Expression),
            "Tunnel" => string.Format(CultureInfo.CurrentCulture, LocalizedStrings.WF_CSR_Summary_TunnelFormat, GetSelectedContent(TunnelTypeBox), _endpoint1Expression, _endpoint2Expression),
            _ => string.Format(CultureInfo.CurrentCulture, LocalizedStrings.WF_Summary_EndpointPairFormat, _endpoint1Expression, _endpoint2Expression)
        };
    }

    private void RuleTypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        UpdateTypeUI();
        ClearValidationError();
    }

    private void TunnelTypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        UpdateTunnelTypeUI();
        ClearValidationError();
    }

    private void RequirementsBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        UpdateAuthenticationSectionVisibility();
        UpdateProtocolPortsVisibility();
    }

    private void AuthenticationMethodBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        UpdateAuthenticationMethodUI();
    }

    private void ProtocolTypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        UpdateProtocolState();
    }

    private void Endpoint1PortBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        UpdateEndpointPortInputVisibility();
    }

    private void Endpoint2PortBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        UpdateEndpointPortInputVisibility();
    }

    private async void ExemptAddButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new IPAddressEntryDialog { ShowPredefined = true };
        WindowDialogResult result = await ShowIpAddressDialogAsync(dialog);
        if (result == WindowDialogResult.Primary && !string.IsNullOrWhiteSpace(dialog.ResultValue))
        {
            _exemptComputers.Add(dialog.ResultValue);
        }
    }

    private async void ExemptEditButton_Click(object sender, RoutedEventArgs e)
    {
        if (ExemptComputersListView.SelectedItem is not string selected) return;

        var dialog = new IPAddressEntryDialog { ShowPredefined = true };
        dialog.SetExistingValue(selected);
        WindowDialogResult result = await ShowIpAddressDialogAsync(dialog);
        if (result == WindowDialogResult.Primary && !string.IsNullOrWhiteSpace(dialog.ResultValue))
        {
            int index = _exemptComputers.IndexOf(selected);
            if (index >= 0)
            {
                _exemptComputers[index] = dialog.ResultValue;
            }
        }
    }

    private void ExemptRemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (ExemptComputersListView.SelectedItem is string selected)
        {
            _exemptComputers.Remove(selected);
        }
    }

    private async void ConfigureEndpointsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new RemoteEndpointsDialog();
        dialog.ApplyAddressExpressions(_endpoint1Expression, _endpoint2Expression);
        WindowDialogResult result = await dialog.ShowDialogAsync(XamlRoot);
        if (result == WindowDialogResult.Primary)
        {
            _endpoint1Expression = dialog.Endpoint1AddressExpression;
            _endpoint2Expression = dialog.Endpoint2AddressExpression;
            _endpoint1UsesAnyAddress = IsAnyAddressExpression(_endpoint1Expression);
            _endpoint2UsesAnyAddress = IsAnyAddressExpression(_endpoint2Expression);
        }
    }

    private async void ConfigureTunnelEndpointsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ConfigureTunnelEndpointsDialog();
        dialog.ApplyState(
            ParseAddressExpression(_endpoint1Expression),
            ParseAddressExpression(_endpoint2Expression),
            _tunnelLocalIpv4,
            _tunnelLocalIpv6,
            _tunnelRemoteIpv4,
            _tunnelRemoteIpv6,
            _applyTunnelAuthorization,
            GetSelectedTag(TunnelTypeBox),
            GetSelectedTag(RequirementsBox));

        WindowDialogResult result = await dialog.ShowDialogAsync(XamlRoot);
        if (result == WindowDialogResult.Primary)
        {
            ApplyTunnelEndpointDialogResult(dialog);
        }
    }

    private void ApplyTunnelEndpointDialogResult(ConfigureTunnelEndpointsDialog dialog)
    {
        string tunnelType = GetSelectedTag(TunnelTypeBox);
        bool doNotAuthenticate = string.Equals(GetSelectedTag(RequirementsBox), "DoNotAuthenticate", StringComparison.Ordinal);

        switch (tunnelType)
        {
            case "ClientToGateway":
                _endpoint1Expression = "Any";
                _endpoint2Expression = BuildAddressExpression(dialog.Endpoint2Computers);
                _tunnelLocalIpv4 = string.Empty;
                _tunnelLocalIpv6 = string.Empty;
                _tunnelRemoteIpv4 = doNotAuthenticate ? string.Empty : dialog.RemoteTunnelIpv4;
                _tunnelRemoteIpv6 = doNotAuthenticate ? string.Empty : dialog.RemoteTunnelIpv6;
                _applyTunnelAuthorization = false;
                break;

            case "GatewayToClient":
                _endpoint1Expression = BuildAddressExpression(dialog.Endpoint1Computers);
                _endpoint2Expression = "Any";
                _tunnelLocalIpv4 = dialog.LocalTunnelIpv4;
                _tunnelLocalIpv6 = dialog.LocalTunnelIpv6;
                _tunnelRemoteIpv4 = string.Empty;
                _tunnelRemoteIpv6 = string.Empty;
                _applyTunnelAuthorization = dialog.ApplyIpsecAuthorization;
                break;

            default:
                _endpoint1Expression = BuildAddressExpression(dialog.Endpoint1Computers);
                _endpoint2Expression = BuildAddressExpression(dialog.Endpoint2Computers);
                _tunnelLocalIpv4 = dialog.LocalTunnelIpv4;
                _tunnelLocalIpv6 = dialog.LocalTunnelIpv6;
                _tunnelRemoteIpv4 = dialog.RemoteTunnelIpv4;
                _tunnelRemoteIpv6 = dialog.RemoteTunnelIpv6;
                _applyTunnelAuthorization = doNotAuthenticate ? false : dialog.ApplyIpsecAuthorization;
                break;
        }

        _endpoint1UsesAnyAddress = IsAnyAddressExpression(_endpoint1Expression);
        _endpoint2UsesAnyAddress = IsAnyAddressExpression(_endpoint2Expression);
    }

    private async void CustomizeMethodsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CustomizeAuthMethodsDialog();
        dialog.ApplySelections(_firstAuthMethods, _secondAuthMethods, _isFirstAuthOptional, _isSecondAuthOptional);

        if (await dialog.ShowDialogAsync(XamlRoot) == WindowDialogResult.Primary)
        {
            _firstAuthMethods = dialog.FirstMethods.ToList();
            _secondAuthMethods = dialog.SecondMethods.ToList();
            _isFirstAuthOptional = dialog.IsFirstAuthOptional;
            _isSecondAuthOptional = dialog.IsSecondAuthOptional;
        }
    }

    private void BrowseCertificateAuthorityButton_Click(object sender, RoutedEventArgs e)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        var pickerService = App.GetRequiredService<CertificateAuthorityPickerService>();
        string? distinguishedName = pickerService.PickCaDistinguishedName(
            hwnd,
            System.Security.Cryptography.X509Certificates.StoreLocation.LocalMachine,
            GetSelectedCertificateAuthorityStoreName(CertificateStoreTypeBox));
        if (!string.IsNullOrWhiteSpace(distinguishedName))
        {
            CertificateAuthorityNameTextBox.Text = distinguishedName;
        }
    }

    private void UpdateTypeUI()
    {
        string typeTag = GetSelectedTag(RuleTypeBox);
        bool isAuthenticationExemption = string.Equals(typeTag, "AuthenticationExemption", StringComparison.Ordinal);
        bool isServerToServer = string.Equals(typeTag, "ServerToServer", StringComparison.Ordinal);
        bool isTunnel = string.Equals(typeTag, "Tunnel", StringComparison.Ordinal);
        bool isCustom = string.Equals(typeTag, "Custom", StringComparison.Ordinal);
        bool isIsolation = string.Equals(typeTag, "Isolation", StringComparison.Ordinal);

        TunnelTypeSection.Visibility = isTunnel ? Visibility.Visible : Visibility.Collapsed;
        ExemptComputersSection.Visibility = isAuthenticationExemption ? Visibility.Visible : Visibility.Collapsed;
        EndpointsSection.Visibility = (isServerToServer || isCustom) ? Visibility.Visible : Visibility.Collapsed;
        RequirementsSection.Visibility = isAuthenticationExemption ? Visibility.Collapsed : Visibility.Visible;
        TunnelEndpointsSection.Visibility = isTunnel ? Visibility.Visible : Visibility.Collapsed;
        UpdateProtocolPortsVisibility();

        ConfigureRequirementsModeItems(typeTag);
        ConfigureAuthenticationModeItems(typeTag);
        UpdateTunnelTypeUI();

        if (isServerToServer)
        {
            SelectAuthenticationMethod("ComputerCertificate");
            SelectPreferredRequirement();
        }
        else if (isTunnel)
        {
            SelectAuthenticationMethod("ComputerCertificate");
        }
        else if (isCustom)
        {
            SelectAuthenticationMethod("Advanced");
        }
        else if (isIsolation)
        {
            SelectAuthenticationMethod("Default");
            SelectPreferredRequirement();
        }

        if (isTunnel)
        {
            SelectPreferredRequirement();
        }

        UpdateAuthenticationSectionVisibility();
    }

    private void ConfigureRequirementsModeItems(string typeTag)
    {
        if (string.Equals(typeTag, "Tunnel", StringComparison.Ordinal))
        {
            UpdateTunnelRequirements();
            return;
        }

        RestoreAllRequirementsItems();
        if (string.Equals(typeTag, "Custom", StringComparison.Ordinal))
        {
            foreach (var item in RequirementsBox.Items)
            {
                if (item is not ComboBoxItem comboBoxItem)
                {
                    continue;
                }

                string tag = comboBoxItem.Tag?.ToString() ?? string.Empty;
                comboBoxItem.Visibility = tag is "DoNotAuthenticate" or "RequestInboundOutbound" or "RequireInboundRequestOutbound" or "RequireInboundOutbound"
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            if (RequirementsBox.SelectedItem is ComboBoxItem selected && selected.Visibility != Visibility.Visible)
            {
                SelectPreferredRequirement();
            }

            return;
        }

        if (!string.Equals(typeTag, "Isolation", StringComparison.Ordinal) &&
            !string.Equals(typeTag, "ServerToServer", StringComparison.Ordinal))
        {
            return;
        }

        foreach (var item in RequirementsBox.Items)
        {
            if (item is not ComboBoxItem comboBoxItem)
            {
                continue;
            }

            string tag = comboBoxItem.Tag?.ToString() ?? string.Empty;
            comboBoxItem.Visibility = tag is "RequestInboundOutbound" or "RequireInboundRequestOutbound" or "RequireInboundOutbound"
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private async void CustomizeInterfaceTypesButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new InterfaceTypesDialog();
        dialog.ApplyInterfaceTypes(_interfaceTypes);
        if (await dialog.ShowDialogAsync(XamlRoot) == WindowDialogResult.Primary)
        {
            _interfaceTypes = NormalizeConnectionSecurityInterfaceTypes(dialog.SelectedInterfaceTypes);
            UpdateInterfaceTypesSummary();
        }
    }

    private void ConfigureAuthenticationModeItems(string typeTag)
    {
        bool isServerToServer = string.Equals(typeTag, "ServerToServer", StringComparison.Ordinal);
        bool isTunnel = string.Equals(typeTag, "Tunnel", StringComparison.Ordinal);
        bool isIsolation = string.Equals(typeTag, "Isolation", StringComparison.Ordinal);

        foreach (var item in AuthenticationMethodBox.Items)
        {
            if (item is not ComboBoxItem comboBoxItem) continue;

            string tag = comboBoxItem.Tag?.ToString() ?? string.Empty;
            comboBoxItem.Visibility = tag switch
            {
                "Advanced" => Visibility.Visible,
                "ComputerCertificate" => isServerToServer || isTunnel ? Visibility.Visible : Visibility.Collapsed,
                "User" => isIsolation || isServerToServer || isTunnel ? Visibility.Collapsed : Visibility.Visible,
                _ => isServerToServer || isTunnel ? Visibility.Collapsed : Visibility.Visible
            };
        }
    }

    private void UpdateAuthenticationSectionVisibility()
    {
        bool doNotAuthenticate = string.Equals(GetSelectedTag(RequirementsBox), "DoNotAuthenticate", StringComparison.Ordinal);
        bool isAuthenticationExemption = string.Equals(GetSelectedTag(RuleTypeBox), "AuthenticationExemption", StringComparison.Ordinal);

        AuthenticationMethodSection.Visibility = (isAuthenticationExemption || doNotAuthenticate)
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (AuthenticationMethodSection.Visibility == Visibility.Visible)
        {
            UpdateAuthenticationMethodUI();
        }

        UpdateProtocolPortsVisibility();
    }

    private void UpdateAuthenticationMethodUI()
    {
        string authTag = GetSelectedTag(AuthenticationMethodBox);
        string typeTag = GetSelectedTag(RuleTypeBox);

        bool serverOrTunnel = string.Equals(typeTag, "ServerToServer", StringComparison.Ordinal) ||
                              string.Equals(typeTag, "Tunnel", StringComparison.Ordinal);
        bool isAdvanced = string.Equals(authTag, "Advanced", StringComparison.Ordinal);
        bool isComputerCertificate = string.Equals(authTag, "ComputerCertificate", StringComparison.Ordinal);

        ComputerCertificateSection.Visibility = isComputerCertificate ? Visibility.Visible : Visibility.Collapsed;
        CustomizeMethodsGrid.Visibility = (serverOrTunnel || isAdvanced) ? Visibility.Visible : Visibility.Collapsed;
        CustomizeMethodsButton.IsEnabled = isAdvanced;
    }

    private void UpdateTunnelTypeDescription()
    {
        string tag = GetSelectedTag(TunnelTypeBox);
        TunnelTypeDescriptionTextBlock.Text = tag switch
        {
            "ClientToGateway" => LocalizedStrings.WF_CSR_TunnelType_ClientToGatewayDescription,
            "GatewayToClient" => LocalizedStrings.WF_CSR_TunnelType_GatewayToClientDescription,
            _ => LocalizedStrings.WF_CSR_TunnelType_DefaultDescription
        };
    }

    private void UpdateTunnelTypeUI()
    {
        bool isTunnel = string.Equals(GetSelectedTag(RuleTypeBox), "Tunnel", StringComparison.Ordinal);
        if (!isTunnel)
        {
            TunnelKeyModuleGrid.Visibility = Visibility.Collapsed;
            return;
        }

        UpdateTunnelTypeDescription();
        UpdateTunnelRequirements();
        UpdateTunnelKeyModuleVisibility();
        UpdateAuthenticationSectionVisibility();
        UpdateProtocolPortsVisibility();
    }

    private void UpdateTunnelKeyModuleVisibility()
    {
        bool showKeyModule = string.Equals(GetSelectedTag(TunnelTypeBox), "ClientToGateway", StringComparison.Ordinal);
        TunnelKeyModuleGrid.Visibility = showKeyModule ? Visibility.Visible : Visibility.Collapsed;

        if (!showKeyModule)
        {
            TunnelKeyModuleBox.SelectedIndex = 0;
        }
    }

    private void UpdateProtocolPortsVisibility()
    {
        string typeTag = GetSelectedTag(RuleTypeBox);
        bool isCustomRule = string.Equals(typeTag, "Custom", StringComparison.Ordinal);
        bool isTunnelWithoutAuthentication =
            string.Equals(typeTag, "Tunnel", StringComparison.Ordinal) &&
            string.Equals(GetSelectedTag(RequirementsBox), "DoNotAuthenticate", StringComparison.Ordinal);

        ProtocolsAndPortsSection.Visibility = isCustomRule || isTunnelWithoutAuthentication
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void UpdateInterfaceTypesSummary()
    {
        InterfaceTypesValueTextBlock.Text = string.Equals(_interfaceTypes, "Any", StringComparison.OrdinalIgnoreCase)
            ? LocalizedStrings.WF_Common_AllInterfaceTypes
            : GetInterfaceTypesDisplay(_interfaceTypes);
    }

    private void UpdateTunnelRequirements()
    {
        string typeTag = GetSelectedTag(RuleTypeBox);
        if (!string.Equals(typeTag, "Tunnel", StringComparison.Ordinal)) return;

        string tunnelTag = GetSelectedTag(TunnelTypeBox);
        foreach (var item in RequirementsBox.Items)
        {
            if (item is not ComboBoxItem comboBoxItem) continue;
            string tag = comboBoxItem.Tag?.ToString() ?? string.Empty;

            comboBoxItem.Visibility = tunnelTag switch
            {
                "ClientToGateway" => tag is "RequireInboundOutbound" or "DoNotAuthenticate"
                    ? Visibility.Visible : Visibility.Collapsed,
                "GatewayToClient" => tag is "RequireInboundOutbound" or "RequireInboundClearOutbound"
                    ? Visibility.Visible : Visibility.Collapsed,
                // CustomConfiguration
                _ => tag is "RequireInboundOutbound" or "RequireInboundClearOutbound" or "DoNotAuthenticate"
                    ? Visibility.Visible : Visibility.Collapsed,
            };
        }

        // Ensure a visible item is selected
        if (RequirementsBox.SelectedItem is ComboBoxItem selected && selected.Visibility != Visibility.Visible)
        {
            SelectPreferredRequirement();
        }
    }

    private void RestoreAllRequirementsItems()
    {
        foreach (var item in RequirementsBox.Items)
        {
            if (item is ComboBoxItem comboBoxItem)
            {
                comboBoxItem.Visibility = Visibility.Visible;
            }
        }
    }

    private void SelectPreferredRequirement()
    {
        string[] preferredTags =
        {
            "RequestInboundOutbound",
            "RequireInboundRequestOutbound",
            "RequireInboundOutbound",
            "RequireInboundClearOutbound",
            "DoNotAuthenticate"
        };

        foreach (string preferredTag in preferredTags)
        {
            for (int i = 0; i < RequirementsBox.Items.Count; i++)
            {
                if (RequirementsBox.Items[i] is ComboBoxItem ci &&
                    ci.Visibility == Visibility.Visible &&
                    string.Equals(ci.Tag?.ToString(), preferredTag, StringComparison.Ordinal))
                {
                    RequirementsBox.SelectedIndex = i;
                    return;
                }
            }
        }

        for (int i = 0; i < RequirementsBox.Items.Count; i++)
        {
            if (RequirementsBox.Items[i] is ComboBoxItem ci && ci.Visibility == Visibility.Visible)
            {
                RequirementsBox.SelectedIndex = i;
                return;
            }
        }
    }

    private void UpdateProtocolState()
    {
        string protocolTag = GetSelectedTag(ProtocolTypeBox);
        bool customProtocol = string.Equals(protocolTag, "Custom", StringComparison.Ordinal);
        bool tcpOrUdp = string.Equals(protocolTag, "TCP", StringComparison.Ordinal) ||
                        string.Equals(protocolTag, "UDP", StringComparison.Ordinal);

        ProtocolNumberBox.IsEnabled = customProtocol;
        Endpoint1PortBox.IsEnabled = tcpOrUdp;
        Endpoint2PortBox.IsEnabled = tcpOrUdp;

        if (!customProtocol)
        {
            ProtocolNumberBox.Value = protocolTag switch
            {
                "TCP" => 6,
                "UDP" => 17,
                _ => 0
            };
        }

        if (!tcpOrUdp)
        {
            Endpoint1PortBox.SelectedIndex = 0;
            Endpoint2PortBox.SelectedIndex = 0;
        }

        UpdateEndpointPortInputVisibility();
    }

    private void UpdateEndpointPortInputVisibility()
    {
        bool showEndpoint1 = string.Equals(GetSelectedTag(Endpoint1PortBox), "SpecificPorts", StringComparison.Ordinal) && Endpoint1PortBox.IsEnabled;
        Endpoint1PortInputLabel.Visibility = showEndpoint1 ? Visibility.Visible : Visibility.Collapsed;
        Endpoint1PortInputTextBox.Visibility = showEndpoint1 ? Visibility.Visible : Visibility.Collapsed;

        bool showEndpoint2 = string.Equals(GetSelectedTag(Endpoint2PortBox), "SpecificPorts", StringComparison.Ordinal) && Endpoint2PortBox.IsEnabled;
        Endpoint2PortInputLabel.Visibility = showEndpoint2 ? Visibility.Visible : Visibility.Collapsed;
        Endpoint2PortInputTextBox.Visibility = showEndpoint2 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SelectAuthenticationMethod(string methodTag)
    {
        for (int i = 0; i < AuthenticationMethodBox.Items.Count; i++)
        {
            if (AuthenticationMethodBox.Items[i] is ComboBoxItem comboBoxItem &&
                string.Equals(comboBoxItem.Tag?.ToString(), methodTag, StringComparison.Ordinal) &&
                comboBoxItem.Visibility == Visibility.Visible)
            {
                AuthenticationMethodBox.SelectedIndex = i;
                return;
            }
        }
    }

    private bool ValidateTunnelSelection(string authenticationMethodTag)
    {
        if (!ValidateTunnelEndpointAddressFamilies())
        {
            ShowValidationError(LocalizedStrings.WF_Validation_TunnelEndpointFamiliesRequired);
            return false;
        }

        if (!string.Equals(GetTunnelKeyModule(), "IKEv2", StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.Equals(authenticationMethodTag, "Advanced", StringComparison.Ordinal))
        {
            return true;
        }

        if (_secondAuthMethods.Count > 0)
        {
            ShowValidationError(LocalizedStrings.WF_Validation_Ikev2FirstAuthOnly);
            return false;
        }

        if (_firstAuthMethods.Any(method => IsIkeV2UnsupportedAuthKind(method.Kind)))
        {
            ShowValidationError(LocalizedStrings.WF_Validation_Ikev2UnsupportedAuth);
            return false;
        }

        return true;
    }

    private bool ValidateTunnelEndpointAddressFamilies()
    {
        bool localHasIpv4 = !string.IsNullOrWhiteSpace(_tunnelLocalIpv4);
        bool localHasIpv6 = !string.IsNullOrWhiteSpace(_tunnelLocalIpv6);
        bool remoteHasIpv4 = !string.IsNullOrWhiteSpace(_tunnelRemoteIpv4);
        bool remoteHasIpv6 = !string.IsNullOrWhiteSpace(_tunnelRemoteIpv6);
        bool localDynamic = !localHasIpv4 && !localHasIpv6;
        bool remoteDynamic = !remoteHasIpv4 && !remoteHasIpv6;

        return localDynamic ||
               remoteDynamic ||
               (localHasIpv4 == remoteHasIpv4 && localHasIpv6 == remoteHasIpv6);
    }

    private string GetTunnelKeyModule()
    {
        bool useKeyModule =
            string.Equals(GetSelectedTag(RuleTypeBox), "Tunnel", StringComparison.Ordinal) &&
            string.Equals(GetSelectedTag(TunnelTypeBox), "ClientToGateway", StringComparison.Ordinal) &&
            !string.Equals(GetSelectedTag(RequirementsBox), "DoNotAuthenticate", StringComparison.Ordinal);

        return useKeyModule ? GetSelectedTag(TunnelKeyModuleBox) : "Default";
    }

    private static bool IsIkeV2UnsupportedAuthKind(string kind)
        => kind is "ComputerKerberos" or "ComputerNtlm" or "UserKerberos" or "UserNtlm" or "PresharedKey";

    private System.Threading.Tasks.Task<WindowDialogResult> ShowIpAddressDialogAsync(IPAddressEntryDialog dialog)
    {
        var modalWindow = new ModalDialogWindow(new ModalDialogOptions
        {
            Title = LocalizedStrings.WF_IPAddress_Title,
            Content = dialog,
            OwnerXamlRoot = XamlRoot,
            RequestedTheme = App.CurrentTheme,
            ThemeChangeSubscribe = h => App.ThemeChanged += h,
            ThemeChangeUnsubscribe = h => App.ThemeChanged -= h,
            PrimaryButtonText = LocalizedStrings.Common_OKButton,
            SecondaryButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = WindowDialogResult.Primary,
            Width = 620,
            Height = 560,
            OnPrimaryButtonClick = () =>
            {
                dialog.CommitResult();
                return true;
            }
        });

        return modalWindow.ShowDialogAsync();
    }

    private static string GetSelectedTag(ComboBox comboBox)
        => (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;

    private static string GetSelectedContent(ComboBox comboBox)
        => (comboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;

    private static System.Security.Cryptography.X509Certificates.StoreName GetSelectedCertificateAuthorityStoreName(ComboBox comboBox)
    {
        return AuthMethodValueMapper.NormalizeCertificateStoreTypeTag(GetSelectedTag(comboBox)) switch
        {
            "IntermediateCA" => System.Security.Cryptography.X509Certificates.StoreName.CertificateAuthority,
            _ => System.Security.Cryptography.X509Certificates.StoreName.Root
        };
    }

    private static IReadOnlyList<string> ParseAddressExpression(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression) ||
            string.Equals(expression, "Any", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(expression, "*", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        return expression
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
    }

    private static string BuildAddressExpression(IReadOnlyList<string> addresses)
        => addresses.Count == 0 ? "Any" : string.Join(",", addresses);

    private static bool IsAnyAddressExpression(string expression)
        => string.IsNullOrWhiteSpace(expression) ||
           string.Equals(expression, "Any", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(expression, "*", StringComparison.OrdinalIgnoreCase);

    private string GetInterfaceTypesDisplay(string interfaceTypes)
    {
        string[] values = interfaceTypes
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => value switch
            {
                "Lan" or "LAN" or "Wired" => LocalizedStrings.WF_Common_LocalAreaNetworkLAN,
                "Wireless" => LocalizedStrings.WF_Common_Wireless,
                "RemoteAccess" => LocalizedStrings.WF_Common_RemoteAccess,
                _ => value
            })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        return values.Length == 0 ? LocalizedStrings.WF_Common_AllInterfaceTypes : string.Join(", ", values);
    }

    private static string NormalizeConnectionSecurityInterfaceTypes(string interfaceTypes)
    {
        string[] values = interfaceTypes
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => value switch
            {
                "All" => "Any",
                "Lan" => "Wired",
                _ => value
            })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return values.Length == 0 || values.Any(value => string.Equals(value, "Any", StringComparison.OrdinalIgnoreCase))
            ? "Any"
            : string.Join(",", values);
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
}

public sealed class NewConnectionSecurityRuleDialogResult
{
    public string Type { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;

    public bool ProfileDomain { get; init; }
    public bool ProfilePrivate { get; init; }
    public bool ProfilePublic { get; init; }
    public string InterfaceTypes { get; init; } = "Any";

    public string Requirements { get; init; } = string.Empty;
    public string RequirementsTag { get; init; } = string.Empty;
    public string AuthenticationMethod { get; init; } = string.Empty;
    public string AuthenticationMethodTag { get; init; } = string.Empty;

    public string Endpoint1Expression { get; init; } = string.Empty;
    public string Endpoint2Expression { get; init; } = string.Empty;
    public string ProtocolTag { get; init; } = string.Empty;
    public int ProtocolNumber { get; init; }
    public string Endpoint1PortModeTag { get; init; } = string.Empty;
    public string Endpoint1Ports { get; init; } = string.Empty;
    public string Endpoint2PortModeTag { get; init; } = string.Empty;
    public string Endpoint2Ports { get; init; } = string.Empty;

    public string TunnelType { get; init; } = string.Empty;
    public string TunnelKeyModule { get; init; } = "Default";
    public bool ExemptIpsecConnectionsFromTunnel { get; init; }

    public IReadOnlyList<string> ExemptComputers { get; init; } = [];

    public string TunnelLocalIpv4 { get; init; } = string.Empty;
    public string TunnelLocalIpv6 { get; init; } = string.Empty;
    public string TunnelRemoteIpv4 { get; init; } = string.Empty;
    public string TunnelRemoteIpv6 { get; init; } = string.Empty;
    public bool ApplyTunnelAuthorization { get; init; }
    public string SigningAlgorithmTag { get; init; } = string.Empty;
    public string CertificateStoreTypeTag { get; init; } = string.Empty;
    public string CertificateAuthorityName { get; init; } = string.Empty;
    public bool AcceptHealthCertificatesOnly { get; init; }

    public IReadOnlyList<AuthMethodDialogResult> FirstAuthMethods { get; init; } = [];
    public IReadOnlyList<AuthMethodDialogResult> SecondAuthMethods { get; init; } = [];
    public bool IsFirstAuthOptional { get; init; }
    public bool IsSecondAuthOptional { get; init; }
}
