using System.Collections.ObjectModel;
using System.Globalization;
using ManagementTools.Core.Features.UserSecurity.Models.SecPol.IPSecurity;
using ManagementTools.Helpers;
using ManagementTools.Localization;
using ManagementTools.Views.UserSecurity.SecPol.IPSecurity.Editors;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace ManagementTools.Views.UserSecurity.SecPol.IPSecurity.Rules;

/// <summary>
/// Displays and edits the rules owned by one legacy static IPsec policy.
/// </summary>
public sealed partial class IPSecurityRulesManagerControl : UserControl
{
    private const int ManagerDialogWidth = 1040;
    private const int ManagerDialogHeight = 720;
    private const int EditorDialogWidth = 860;
    private const int EditorDialogHeight = 760;
    private const int ConfirmationDialogWidth = 520;
    private const int ConfirmationDialogHeight = 300;

    private readonly IPSecurityPolicyDefinition _policy;
    private readonly IReadOnlyList<string> _filterListNames;
    private readonly IReadOnlyList<string> _filterActionNames;
    private readonly Func<IPSecurityRuleCommandOptions, Task<bool>> _addRuleAsync;
    private readonly Func<IPSecurityRuleCommandOptions, Task<bool>> _setRuleAsync;
    private readonly Func<string, string, Task<bool>> _deleteRuleAsync;
    private bool _isBusy;

    /// <summary>Gets localized strings used by the control.</summary>
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    /// <summary>Gets the mutable in-memory rule list displayed by the manager.</summary>
    private ObservableCollection<IPSecurityRuleListItem> RuleItems { get; } = [];

    /// <summary>
    /// Initializes a rule manager for one IPsec policy.
    /// </summary>
    /// <param name="policy">The policy whose rules are displayed.</param>
    /// <param name="filterListNames">Available filter-list names.</param>
    /// <param name="filterActionNames">Available filter-action names.</param>
    /// <param name="addRuleAsync">Callback that creates a validated rule.</param>
    /// <param name="setRuleAsync">Callback that updates a validated rule.</param>
    /// <param name="deleteRuleAsync">Callback that deletes a rule by policy and rule name.</param>
    public IPSecurityRulesManagerControl(
        IPSecurityPolicyDefinition policy,
        IEnumerable<string> filterListNames,
        IEnumerable<string> filterActionNames,
        Func<IPSecurityRuleCommandOptions, Task<bool>> addRuleAsync,
        Func<IPSecurityRuleCommandOptions, Task<bool>> setRuleAsync,
        Func<string, string, Task<bool>> deleteRuleAsync)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(filterListNames);
        ArgumentNullException.ThrowIfNull(filterActionNames);
        ArgumentNullException.ThrowIfNull(addRuleAsync);
        ArgumentNullException.ThrowIfNull(setRuleAsync);
        ArgumentNullException.ThrowIfNull(deleteRuleAsync);

        _policy = policy;
        _filterListNames = NormalizeNames(filterListNames);
        _filterActionNames = NormalizeNames(filterActionNames);
        _addRuleAsync = addRuleAsync;
        _setRuleAsync = setRuleAsync;
        _deleteRuleAsync = deleteRuleAsync;

        InitializeComponent();
        RulesListView.ItemsSource = RuleItems;
        foreach (IPSecurityRuleDefinition rule in policy.Rules)
        {
            RuleItems.Add(CreateRuleListItem(rule));
        }

        UpdateCommandState();
    }

    /// <summary>
    /// Shows this manager in a modal window owned by the supplied XAML root.
    /// </summary>
    /// <param name="ownerXamlRoot">The owning XAML root.</param>
    /// <returns>The result produced when the manager window closes.</returns>
    public Task<WindowDialogResult> ShowDialogAsync(XamlRoot ownerXamlRoot)
    {
        ArgumentNullException.ThrowIfNull(ownerXamlRoot);

        string title = string.Format(
            CultureInfo.CurrentCulture,
            LocalizedStrings.IPSec_Dialog_EditPolicy_TitleFormat,
            _policy.Name);
        var modalWindow = new ModalDialogWindow(new ModalDialogOptions
        {
            Title = title,
            Content = this,
            OwnerXamlRoot = ownerXamlRoot,
            RequestedTheme = App.CurrentTheme,
            CloseButtonText = LocalizedStrings.Common_CloseButton,
            DefaultButton = WindowDialogResult.None,
            Width = ManagerDialogWidth,
            Height = ManagerDialogHeight
        });

        return modalWindow.ShowDialogAsync();
    }

    private async void AddRuleButton_Click(object sender, RoutedEventArgs e)
    {
        var editor = new IPSecurityRuleEditorControl(
            IPSecurityEditorMode.Create,
            _policy.Name,
            _filterListNames,
            _filterActionNames);
        IPSecurityRuleCommandOptions? options = await ShowRuleEditorAsync(
            editor,
            string.Format(
                CultureInfo.CurrentCulture,
                LocalizedStrings.IPSec_Dialog_CreateRule_TitleFormat,
                _policy.Name),
            LocalizedStrings.Common_CreateButton);
        if (options is null || !await RunMutationAsync(() => _addRuleAsync(options)))
        {
            return;
        }

        IPSecurityRuleDefinition rule = CreateRuleDefinition(options);
        IPSecurityRuleListItem item = CreateRuleListItem(rule);
        RuleItems.Add(item);
        RulesListView.SelectedItem = item;
    }

    private async void EditRuleButton_Click(object sender, RoutedEventArgs e)
    {
        await EditSelectedRuleAsync();
    }

    private async void DeleteRuleButton_Click(object sender, RoutedEventArgs e)
    {
        if (RulesListView.SelectedItem is not IPSecurityRuleListItem selected)
        {
            return;
        }

        var confirmationWindow = new ModalDialogWindow(new ModalDialogOptions
        {
            Title = LocalizedStrings.IPSec_DeleteConfirm_Title,
            Content = string.Format(
                CultureInfo.CurrentCulture,
                LocalizedStrings.IPSec_DeleteRule_MessageFormat,
                selected.Definition.Name),
            OwnerXamlRoot = XamlRoot,
            RequestedTheme = App.CurrentTheme,
            PrimaryButtonText = LocalizedStrings.Common_DeleteButton,
            CloseButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = WindowDialogResult.None,
            Width = ConfirmationDialogWidth,
            Height = ConfirmationDialogHeight
        });
        if (await confirmationWindow.ShowDialogAsync() != WindowDialogResult.Primary
            || !await RunMutationAsync(() => _deleteRuleAsync(_policy.Name, selected.Definition.Name)))
        {
            return;
        }

        RuleItems.Remove(selected);
    }

    private void RulesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateCommandState();
    }

    private async void RulesListView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        await EditSelectedRuleAsync();
    }

    private async Task EditSelectedRuleAsync()
    {
        if (RulesListView.SelectedItem is not IPSecurityRuleListItem selected)
        {
            return;
        }

        var editor = new IPSecurityRuleEditorControl(
            IPSecurityEditorMode.Edit,
            _policy.Name,
            _filterListNames,
            _filterActionNames,
            selected.Definition);
        IPSecurityRuleCommandOptions? options = await ShowRuleEditorAsync(
            editor,
            string.Format(
                CultureInfo.CurrentCulture,
                LocalizedStrings.IPSec_Dialog_EditRule_TitleFormat,
                selected.Definition.Name),
            LocalizedStrings.Common_SaveButton);
        if (options is null || !await RunMutationAsync(() => _setRuleAsync(options)))
        {
            return;
        }

        int index = RuleItems.IndexOf(selected);
        IPSecurityRuleDefinition updated = CreateRuleDefinition(options, selected.Definition);
        RuleItems[index] = CreateRuleListItem(updated);
        RulesListView.SelectedIndex = index;
    }

    private async Task<IPSecurityRuleCommandOptions?> ShowRuleEditorAsync(
        IPSecurityRuleEditorControl editor,
        string title,
        string primaryButtonText)
    {
        IPSecurityRuleCommandOptions? result = null;
        var modalWindow = new ModalDialogWindow(new ModalDialogOptions
        {
            Title = title,
            Content = editor,
            OwnerXamlRoot = XamlRoot,
            RequestedTheme = App.CurrentTheme,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = WindowDialogResult.Primary,
            IsPrimaryButtonLeading = true,
            Width = EditorDialogWidth,
            Height = EditorDialogHeight,
            OnPrimaryButtonClick = () => editor.TryBuildResult(out result)
        });

        return await modalWindow.ShowDialogAsync() == WindowDialogResult.Primary ? result : null;
    }

    private async Task<bool> RunMutationAsync(Func<Task<bool>> mutation)
    {
        SetBusyState(true);
        ErrorInfoBar.IsOpen = false;
        try
        {
            bool succeeded = await mutation();
            ErrorInfoBar.IsOpen = !succeeded;
            return succeeded;
        }
        catch (Exception)
        {
            ErrorInfoBar.IsOpen = true;
            return false;
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private void SetBusyState(bool isBusy)
    {
        _isBusy = isBusy;
        BusyProgressRing.IsActive = isBusy;
        BusyProgressRing.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        RulesListView.IsEnabled = !isBusy;
        UpdateCommandState();
    }

    private void UpdateCommandState()
    {
        bool hasSelection = RulesListView?.SelectedItem is IPSecurityRuleListItem;
        if (AddRuleButton is not null)
        {
            AddRuleButton.IsEnabled = !_isBusy;
            EditRuleButton.IsEnabled = !_isBusy && hasSelection;
            DeleteRuleButton.IsEnabled = !_isBusy && hasSelection;
        }
    }

    private static IPSecurityRuleDefinition CreateRuleDefinition(
        IPSecurityRuleCommandOptions options,
        IPSecurityRuleDefinition? existing = null)
    {
        return new IPSecurityRuleDefinition
        {
            Name = options.NewName ?? options.Name,
            PolicyName = options.PolicyName,
            Description = options.Description ?? existing?.Description ?? string.Empty,
            FilterListName = options.FilterListName ?? existing?.FilterListName ?? string.Empty,
            FilterActionName = options.FilterActionName ?? existing?.FilterActionName ?? string.Empty,
            TunnelEndpoint = ResolveTunnelEndpoint(options.TunnelEndpoint, existing?.TunnelEndpoint),
            ConnectionType = options.ConnectionType?.ToString() ?? existing?.ConnectionType ?? string.Empty,
            IsActive = options.IsActive ?? existing?.IsActive ?? true,
            AuthenticationMethods = options.AuthenticationMethods is null
                ? existing?.AuthenticationMethods ?? []
                : CreateAuthenticationDefinitions(options.AuthenticationMethods)
        };
    }

    private IPSecurityRuleListItem CreateRuleListItem(IPSecurityRuleDefinition rule)
    {
        return new IPSecurityRuleListItem
        {
            Definition = rule,
            Name = rule.Name,
            Description = rule.Description,
            FilterListName = rule.FilterListName,
            FilterActionName = rule.FilterActionName,
            ActiveDisplay = rule.IsActive
                ? LocalizedStrings.IPSec_Value_Yes
                : LocalizedStrings.IPSec_Value_No,
            ConnectionTypeDisplay = ConvertConnectionType(rule.ConnectionType)
        };
    }

    private string ConvertConnectionType(string connectionType)
    {
        if (string.IsNullOrWhiteSpace(connectionType))
        {
            return string.Empty;
        }

        return connectionType.ToLowerInvariant() switch
        {
            "all" => LocalizedStrings.IPSec_Editor_ConnectionAll,
            "lan" => LocalizedStrings.IPSec_Editor_ConnectionLan,
            "dialup" => LocalizedStrings.IPSec_Editor_ConnectionDialUp,
            _ => connectionType
        };
    }

    private static string ResolveTunnelEndpoint(string? endpoint, string? existingEndpoint)
    {
        if (endpoint is null)
        {
            return existingEndpoint ?? string.Empty;
        }

        return string.Equals(endpoint, "no", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : endpoint;
    }

    private static IReadOnlyList<IPSecurityAuthenticationMethodDefinition> CreateAuthenticationDefinitions(
        IEnumerable<IPSecurityRuleAuthenticationCommand> methods)
    {
        return methods.Select(static method => method switch
        {
            IPSecurityCertificateAuthenticationCommand certificate =>
                new IPSecurityAuthenticationMethodDefinition
                {
                    Kind = IPSecurityAuthenticationMethodKind.CertificateAuthority,
                    Detail = certificate.CertificateAuthorityName,
                    EnableCertificateToAccountMapping = certificate.EnableCertificateToAccountMapping,
                    ExcludeCertificateAuthorityName = certificate.ExcludeCertificateAuthorityName
                },
            IPSecurityPreSharedKeyAuthenticationCommand =>
                new IPSecurityAuthenticationMethodDefinition
                {
                    Kind = IPSecurityAuthenticationMethodKind.PreSharedKey
                },
            _ => new IPSecurityAuthenticationMethodDefinition
            {
                Kind = IPSecurityAuthenticationMethodKind.Kerberos
            }
        }).ToArray();
    }

    private static IReadOnlyList<string> NormalizeNames(IEnumerable<string> names)
    {
        return names
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(static name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }
}

internal sealed class IPSecurityRuleListItem
{
    internal IPSecurityRuleDefinition Definition { get; set; } = new();

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string FilterListName { get; set; } = string.Empty;

    public string FilterActionName { get; set; } = string.Empty;

    public string ActiveDisplay { get; set; } = string.Empty;

    public string ConnectionTypeDisplay { get; set; } = string.Empty;
}
