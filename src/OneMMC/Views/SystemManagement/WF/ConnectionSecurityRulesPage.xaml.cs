using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using OneMMC.Services;
using OneMMC.Views.Dialogs.NewRule;
using CommunityToolkit.WinUI.Controls;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.System;

namespace OneMMC.Views;

public sealed partial class ConnectionSecurityRulesPage : Page
{
    private readonly ConnectionSecurityService _connectionSecurityService;
    private readonly IAdminService _adminService;
    private readonly ILogger<ConnectionSecurityRulesPage> _logger;
    private readonly HashSet<ToggleSwitch> _trackedRuleToggles = [];
    private readonly HashSet<ToggleSwitch> _userInitiatedRuleToggles = [];
    private IDisposable? _firewallChangeSubscription;
    private bool _isLoadingRules;

    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;
    public ConnectionSecurityRulesViewModel ViewModel { get; }

    private string _lastSearchText = string.Empty;

    public ConnectionSecurityRulesPage()
    {
        _connectionSecurityService = App.GetRequiredService<ConnectionSecurityService>();
        _adminService = App.GetRequiredService<IAdminService>();
        _logger = App.GetRequiredService<ILogger<ConnectionSecurityRulesPage>>();
        ViewModel = new ConnectionSecurityRulesViewModel();

        InitializeComponent();
        RequestedTheme = App.CurrentTheme;
        App.ThemeChanged += OnThemeChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        _firewallChangeSubscription = App.GetRequiredService<WindowsFirewallRuleChangeService>()
            .Subscribe(OnFirewallRulesChanged);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await LoadRulesAsync(showLoading: true, showError: true);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        App.ThemeChanged -= OnThemeChanged;
        _firewallChangeSubscription?.Dispose();
        _firewallChangeSubscription = null;
        _trackedRuleToggles.Clear();
        _userInitiatedRuleToggles.Clear();
        Unloaded -= OnUnloaded;
    }

    private void OnThemeChanged(ElementTheme theme)
    {
        RequestedTheme = theme;
    }

    private async Task LoadRulesAsync(bool showLoading, bool showError)
    {
        if (_isLoadingRules)
        {
            return;
        }

        _isLoadingRules = true;
        if (showLoading)
        {
            ViewModel.IsLoading = true;
        }

        try
        {
            IReadOnlyList<ConnectionSecurityRuleModel> rules = await Task.Run(() => _connectionSecurityService.GetRules());
            ViewModel.Rules = new ObservableCollection<ConnectionSecurityRuleItem>(
                rules.Select(ConnectionSecurityRuleItem.FromModel));
            ViewModel.FilterRules(_lastSearchText);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load connection security rules.");
            if (showError)
            {
                await ShowErrorDialogAsync(LocalizedStrings.Common_ErrorTitle, LocalizedStrings.WF_Error_LoadConnectionSecurityRules);
            }
        }
        finally
        {
            if (showLoading)
            {
                ViewModel.IsLoading = false;
            }

            _isLoadingRules = false;
        }
    }

    private async void AddRuleButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_adminService.IsRunningAsAdmin)
        {
            await AdminDialogHelper.ShowAdminRequiredDialogAsync(XamlRoot);
            return;
        }

        var dialog = new NewConnectionSecurityRuleDialog(XamlRoot);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.Result is null)
        {
            return;
        }

        try
        {
            ConnectionSecurityRuleModel model = MapDialogResult(dialog.Result);
            _connectionSecurityService.AddRule(model);
            ConnectionSecurityRuleModel createdRule = _connectionSecurityService.GetRule(model.Name) ?? model;
            ViewModel.Rules.Insert(0, ConnectionSecurityRuleItem.FromModel(createdRule));
            ViewModel.FilterRules(_lastSearchText);
        }
        catch (Exception ex)
        {
            await ShowErrorDialogAsync(LocalizedStrings.WF_Error_CreateConnectionSecurityRule, ex.Message);
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadRulesAsync(showLoading: true, showError: true);
    }

    private void OnFirewallRulesChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            await LoadRulesAsync(showLoading: false, showError: false);
        });
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        string text = sender.Text ?? string.Empty;
        if (string.Equals(_lastSearchText, text, StringComparison.Ordinal))
        {
            return;
        }

        _lastSearchText = text;
        ViewModel.FilterRules(text);
    }

    private void RuleCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not SettingsCard card || card.Tag is not ConnectionSecurityRuleItem item)
        {
            return;
        }

        ViewModel.SelectedRule = item;
        BreadcrumbNavigationService.AddBreadcrumb(item.Name, typeof(FirewallRuleInfoPage), item.Model);
        Frame.Navigate(
            typeof(FirewallRuleInfoPage),
            item.Model,
            new SlideNavigationTransitionInfo
            {
                Effect = SlideNavigationTransitionEffect.FromRight
            });
    }

    private void RuleToggleSwitch_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggleSwitch || !_trackedRuleToggles.Add(toggleSwitch))
        {
            return;
        }

        toggleSwitch.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(RuleToggleSwitch_PointerPressed), true);
        toggleSwitch.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(RuleToggleSwitch_KeyDown), true);
    }

    private void RuleToggleSwitch_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggleSwitch || !_trackedRuleToggles.Remove(toggleSwitch))
        {
            return;
        }

        toggleSwitch.RemoveHandler(UIElement.PointerPressedEvent, new PointerEventHandler(RuleToggleSwitch_PointerPressed));
        toggleSwitch.RemoveHandler(UIElement.KeyDownEvent, new KeyEventHandler(RuleToggleSwitch_KeyDown));
        _userInitiatedRuleToggles.Remove(toggleSwitch);
    }

    private void RuleToggleSwitch_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggleSwitch)
        {
            MarkUserInitiatedToggle(toggleSwitch);
        }
    }

    private void RuleToggleSwitch_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggleSwitch && e.Key is VirtualKey.Space or VirtualKey.Enter)
        {
            MarkUserInitiatedToggle(toggleSwitch);
        }
    }

    private void MarkUserInitiatedToggle(ToggleSwitch toggleSwitch)
    {
        _userInitiatedRuleToggles.Add(toggleSwitch);

        var expirationTimer = DispatcherQueue.CreateTimer();
        expirationTimer.Interval = TimeSpan.FromSeconds(3);
        expirationTimer.Tick += (_, _) =>
        {
            _userInitiatedRuleToggles.Remove(toggleSwitch);
            expirationTimer.Stop();
        };
        expirationTimer.Start();
    }

    private async void RuleToggleSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggleSwitch || toggleSwitch.Tag is not ConnectionSecurityRuleItem item)
        {
            return;
        }

        if (!_userInitiatedRuleToggles.Remove(toggleSwitch))
        {
            return;
        }

        if (item.IsEnabled == toggleSwitch.IsOn)
        {
            return;
        }

        if (!_adminService.IsRunningAsAdmin)
        {
            ResetToggleSwitch(toggleSwitch, item.Model.Enabled);
            await AdminDialogHelper.ShowAdminRequiredDialogAsync(XamlRoot);
            return;
        }

        bool previousEnabled = item.Model.Enabled;
        try
        {
            await Task.Run(() => _connectionSecurityService.SetRuleEnabled(GetRuleLookupName(item.Model), toggleSwitch.IsOn));
            item.IsEnabled = toggleSwitch.IsOn;
            item.Model.Enabled = toggleSwitch.IsOn;
        }
        catch (Exception ex)
        {
            item.IsEnabled = previousEnabled;
            item.Model.Enabled = previousEnabled;
            ResetToggleSwitch(toggleSwitch, previousEnabled);
            await ShowErrorDialogAsync(LocalizedStrings.WF_Error_UpdateConnectionSecurityRule, ex.Message);
        }
    }

    private void ResetToggleSwitch(ToggleSwitch toggleSwitch, bool enabled)
    {
        toggleSwitch.Toggled -= RuleToggleSwitch_Toggled;
        toggleSwitch.IsOn = enabled;
        toggleSwitch.Toggled += RuleToggleSwitch_Toggled;
    }

    private static ConnectionSecurityRuleModel MapDialogResult(NewConnectionSecurityRuleDialogResult result)
    {
        var model = new ConnectionSecurityRuleModel
        {
            Name = result.Name,
            OriginalName = result.Name,
            Description = result.Description,
            Kind = result.Type switch
            {
                "Isolation" => ConnectionSecurityRuleKind.Isolation,
                "AuthenticationExemption" => ConnectionSecurityRuleKind.AuthenticationExemption,
                "ServerToServer" => ConnectionSecurityRuleKind.ServerToServer,
                "Tunnel" => ConnectionSecurityRuleKind.Tunnel,
                _ => ConnectionSecurityRuleKind.Custom
            },
            Enabled = true,
            ProfileDomain = result.ProfileDomain,
            ProfilePrivate = result.ProfilePrivate,
            ProfilePublic = result.ProfilePublic,
            InterfaceTypes = result.InterfaceTypes,
            InboundSecurity = result.RequirementsTag switch
            {
                "RequestInboundOutbound" => ConnectionSecurityRequirement.Request,
                "RequireInboundRequestOutbound" => ConnectionSecurityRequirement.Require,
                "RequireInboundOutbound" => ConnectionSecurityRequirement.Require,
                "RequireInboundClearOutbound" => ConnectionSecurityRequirement.Require,
                _ => ConnectionSecurityRequirement.None
            },
            OutboundSecurity = result.RequirementsTag switch
            {
                "RequestInboundOutbound" => ConnectionSecurityRequirement.Request,
                "RequireInboundRequestOutbound" => ConnectionSecurityRequirement.Request,
                "RequireInboundOutbound" => ConnectionSecurityRequirement.Require,
                "RequireInboundClearOutbound" => ConnectionSecurityRequirement.None,
                _ => ConnectionSecurityRequirement.None
            },
            Mode = result.Type == "Tunnel" ? ConnectionSecurityMode.Tunnel : ConnectionSecurityMode.Transport,
            Endpoint1Expression = string.IsNullOrWhiteSpace(result.Endpoint1Expression) ? "Any" : result.Endpoint1Expression,
            Endpoint2Expression = string.IsNullOrWhiteSpace(result.Endpoint2Expression) ? "Any" : result.Endpoint2Expression,
            Protocol = result.ProtocolTag switch
            {
                "TCP" => "TCP",
                "UDP" => "UDP",
                "Custom" => result.ProtocolNumber.ToString(),
                "" or "Any" => "Any",
                _ => result.ProtocolTag
            },
            LocalPort = result.Endpoint1PortModeTag == "SpecificPorts" ? result.Endpoint1Ports : "Any",
            RemotePort = result.Endpoint2PortModeTag == "SpecificPorts" ? result.Endpoint2Ports : "Any",
            Phase1AuthSet = string.Empty,
            Phase2AuthSet = string.Empty,
            MainModeCryptoSet = string.Empty,
            QuickModeCryptoSet = string.Empty,
            KeyModule = result.TunnelKeyModule,
            BypassTunnelIfEncrypted = result.ExemptIpsecConnectionsFromTunnel,
            RequireAuthorization = result.ApplyTunnelAuthorization,
            LocalTunnelEndpoint = CombineAddressExpressions(result.TunnelLocalIpv4, result.TunnelLocalIpv6),
            RemoteTunnelEndpoint = CombineAddressExpressions(result.TunnelRemoteIpv4, result.TunnelRemoteIpv6),
            RemoteTunnelEndpointDnsName = string.Empty,
            TunnelType = result.TunnelType switch
            {
                "ClientToGateway" => "ClientToGateway",
                "GatewayToClient" => "GatewayToClient",
                _ => "GatewayToGateway"
            },
            Summary = result.Summary
        };

        ApplyAuthenticationMethodSelection(model, result);

        model.ProfilesMask = 0;
        if (model.ProfileDomain)
        {
            model.ProfilesMask |= 1;
        }

        if (model.ProfilePrivate)
        {
            model.ProfilesMask |= 2;
        }

        if (model.ProfilePublic)
        {
            model.ProfilesMask |= 4;
        }

        model.ProfileDisplay = model.ProfilesMask == 7
            ? LocalizedStrings.Instance.WF_Common_All
            : string.Join(", ", new[]
            {
                model.ProfileDomain ? LocalizedStrings.Instance.WF_Profile_Domain : null,
                model.ProfilePrivate ? LocalizedStrings.Instance.WF_Profile_Private : null,
                model.ProfilePublic ? LocalizedStrings.Instance.WF_Profile_Public : null
            }.Where(value => value is not null));

        return model;
    }

    private static void ApplyAuthenticationMethodSelection(
        ConnectionSecurityRuleModel model,
        NewConnectionSecurityRuleDialogResult result)
    {
        switch (result.AuthenticationMethodTag)
        {
            case "Advanced":
                CopyAuthMethods(model.FirstAuthMethods, result.FirstAuthMethods);
                CopyAuthMethods(model.SecondAuthMethods, result.SecondAuthMethods);
                model.IsFirstAuthOptional = result.IsFirstAuthOptional;
                model.IsSecondAuthOptional = result.IsSecondAuthOptional;
                return;

            case "ComputerAndUser":
                AddAuthMethod(model.FirstAuthMethods, CreateComputerKerberosAuthMethod());
                AddAuthMethod(model.SecondAuthMethods, CreateUserKerberosAuthMethod());
                return;

            case "Computer":
                AddAuthMethod(model.FirstAuthMethods, CreateComputerKerberosAuthMethod());
                return;

            case "User":
                AddAuthMethod(model.SecondAuthMethods, CreateUserKerberosAuthMethod());
                return;

            case "ComputerCertificate":
                AddAuthMethod(model.FirstAuthMethods, CreateComputerCertificateAuthMethod(result));
                return;

            default:
                return;
        }
    }

    private static void CopyAuthMethods(
        ICollection<AuthMethodListItem> target,
        IEnumerable<AuthMethodDialogResult> source)
    {
        foreach (AuthMethodDialogResult method in source)
        {
            AddAuthMethod(target, method);
        }
    }

    private static void AddAuthMethod(
        ICollection<AuthMethodListItem> target,
        AuthMethodDialogResult method)
    {
        target.Add(new AuthMethodListItem
        {
            Method = method.Method,
            Details = method.Details,
            Result = method
        });
    }

    private static AuthMethodDialogResult CreateComputerKerberosAuthMethod() =>
        new()
        {
            Kind = "ComputerKerberos",
            Method = LocalizedStrings.Instance.WF_AuthMethod_ComputerKerberos,
            Details = LocalizedStrings.Instance.WF_AuthDetails_KerberosAuthentication
        };

    private static AuthMethodDialogResult CreateUserKerberosAuthMethod() =>
        new()
        {
            Kind = "UserKerberos",
            Method = LocalizedStrings.Instance.WF_AuthMethod_UserKerberos,
            Details = LocalizedStrings.Instance.WF_AuthDetails_KerberosAuthentication
        };

    private static AuthMethodDialogResult CreateComputerCertificateAuthMethod(NewConnectionSecurityRuleDialogResult result)
    {
        string certificateStoreType = AuthMethodValueMapper.NormalizeCertificateStoreTypeTag(result.CertificateStoreTypeTag);
        string signingAlgorithm = AuthMethodValueMapper.NormalizeSigningAlgorithmTag(result.SigningAlgorithmTag);
        string method = result.AcceptHealthCertificatesOnly
            ? LocalizedStrings.Instance.WF_AuthMethod_ComputerHealthCertificateFromCA
            : LocalizedStrings.Instance.WF_AuthMethod_ComputerCertificateFromCA;

        return new AuthMethodDialogResult
        {
            Kind = result.AcceptHealthCertificatesOnly ? "ComputerHealthCertificate" : "ComputerCertificate",
            Method = method,
            Details = AuthMethodValueMapper.BuildCertificateDetails(
                result.CertificateAuthorityName,
                signingAlgorithm,
                certificateStoreType,
                result.AcceptHealthCertificatesOnly,
                certificateMappingEnabled: false,
                advancedCriteria: null),
            SigningAlgorithm = signingAlgorithm,
            CertificateStoreType = certificateStoreType,
            CaDistinguishedName = result.CertificateAuthorityName,
            HealthCertificateOnly = result.AcceptHealthCertificatesOnly
        };
    }

    private static string CombineAddressExpressions(params string[] values)
    {
        string[] normalizedValues = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        return normalizedValues.Length == 0
            ? string.Empty
            : string.Join(",", normalizedValues);
    }

    private async System.Threading.Tasks.Task ShowErrorDialogAsync(string title, string message)
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

    private static string GetRuleLookupName(ConnectionSecurityRuleModel rule)
        => string.IsNullOrWhiteSpace(rule.OriginalName) ? rule.Name : rule.OriginalName;

    public class ConnectionSecurityRulesViewModel : BindableBase
    {
        private ObservableCollection<ConnectionSecurityRuleItem> _rules = new();
        private ObservableCollection<ConnectionSecurityRuleItem> _filteredRules = new();
        private ConnectionSecurityRuleItem? _selectedRule;
        private bool _isLoading;

        public ObservableCollection<ConnectionSecurityRuleItem> Rules
        {
            get => _rules;
            set => SetProperty(ref _rules, value);
        }

        public ObservableCollection<ConnectionSecurityRuleItem> FilteredRules
        {
            get => _filteredRules;
            set => SetProperty(ref _filteredRules, value);
        }

        public ConnectionSecurityRuleItem? SelectedRule
        {
            get => _selectedRule;
            set => SetProperty(ref _selectedRule, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public void FilterRules(string searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText))
            {
                FilteredRules = new ObservableCollection<ConnectionSecurityRuleItem>(Rules);
                return;
            }

            var filtered = Rules.Where(rule =>
                rule.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                rule.Summary.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                rule.Description.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                .ToList();

            FilteredRules = new ObservableCollection<ConnectionSecurityRuleItem>(filtered);
        }
    }

    public class ConnectionSecurityRuleItem : BindableBase
    {
        private string _name = string.Empty;
        private string _description = string.Empty;
        private string _summary = string.Empty;
        private bool _isEnabled;

        public ConnectionSecurityRuleModel Model { get; init; } = new();

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
        }

        public string Summary
        {
            get => _summary;
            set => SetProperty(ref _summary, value);
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        public static ConnectionSecurityRuleItem FromModel(ConnectionSecurityRuleModel model)
        {
            return new ConnectionSecurityRuleItem
            {
                Model = model,
                Name = model.Name,
                Description = model.Description,
                Summary = string.IsNullOrWhiteSpace(model.Summary) ? model.ProfileDisplay : model.Summary,
                IsEnabled = model.Enabled
            };
        }
    }

    public class BindableBase : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        protected bool SetProperty<T>(ref T storage, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(storage, value))
            {
                return false;
            }

            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }
}
