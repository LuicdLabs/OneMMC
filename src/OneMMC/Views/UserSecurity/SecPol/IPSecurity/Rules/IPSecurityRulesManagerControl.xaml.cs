using System.Collections.ObjectModel;
using System.Globalization;
using OneMMC.Core.Features.UserSecurity.Models.SecPol.IPSecurity;
using OneMMC.Helpers;
using OneMMC.Localization;
using OneMMC.Views.UserSecurity.SecPol.IPSecurity.Editors;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
namespace OneMMC.Views.UserSecurity.SecPol.IPSecurity.Rules;

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
        RuleItems.CollectionChanged += (_, _) => UpdateEmptyState();
        UpdateEmptyState();
    }

    /// <summary>Shows or hides the list's empty state.</summary>
    private void UpdateEmptyState()
    {
        EmptyRulesText.Visibility = RuleItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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

        var confirmation = new ContentDialog
        {
            Title = LocalizedStrings.IPSec_DeleteConfirm_Title,
            Content = string.Format(
                CultureInfo.CurrentCulture,
                LocalizedStrings.IPSec_DeleteRule_MessageFormat,
                selected.Definition.Name),
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            XamlRoot = this.XamlRoot,
            RequestedTheme = App.CurrentTheme,
            PrimaryButtonText = LocalizedStrings.Common_DeleteButton,
            CloseButtonText = LocalizedStrings.Common_CancelButton
        };
        confirmation.Resources["ContentDialogMaxWidth"] = (double)ConfirmationDialogWidth;
        confirmation.Resources["ContentDialogMaxHeight"] = (double)ConfirmationDialogHeight;
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary
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
                selected.Name),
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
        // The rule editor is a leaf: everything it needs is inline, so a ContentDialog on this
        // manager's own XAML root is the right host.
        IPSecurityRuleCommandOptions? result = null;
        var dialog = new ContentDialog
        {
            Title = title,
            Content = editor,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            XamlRoot = this.XamlRoot,
            RequestedTheme = App.CurrentTheme,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = ContentDialogButton.Primary
        };
        dialog.Resources["ContentDialogMaxWidth"] = (double)EditorDialogWidth;
        dialog.Resources["ContentDialogMaxHeight"] = (double)EditorDialogHeight;
        dialog.PrimaryButtonClick += (_, args) => args.Cancel = !editor.TryBuildResult(out result);

        return await dialog.ShowAsync() == ContentDialogResult.Primary ? result : null;
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
        bool canDelete = RulesListView?.SelectedItem is IPSecurityRuleListItem selected
            && !selected.Definition.IsDefaultResponseRule;
        if (AddRuleButton is not null)
        {
            AddRuleButton.IsEnabled = !_isBusy;
            EditRuleButton.IsEnabled = !_isBusy && hasSelection;
            DeleteRuleButton.IsEnabled = !_isBusy && canDelete;
        }
    }

    private static IPSecurityRuleDefinition CreateRuleDefinition(
        IPSecurityRuleCommandOptions options,
        IPSecurityRuleDefinition? existing = null)
    {
        return new IPSecurityRuleDefinition
        {
            Identifier = existing?.Identifier ?? Guid.Empty,
            IsDefaultResponseRule = existing?.IsDefaultResponseRule ?? false,
            Name = options.NewName ?? options.Name,
            PolicyName = options.PolicyName,
            Description = options.Description ?? existing?.Description ?? string.Empty,
            FilterListName = options.FilterListName ?? existing?.FilterListName ?? string.Empty,
            FilterActionName = options.FilterActionName ?? existing?.FilterActionName ?? string.Empty,
            FilterAction = existing?.FilterAction is { } action
                ? new IPSecurityFilterActionDefinition
                {
                    Name = action.Name,
                    Description = action.Description,
                    Action = action.Action,
                    UseQuickModePerfectForwardSecrecy =
                        options.UseQuickModePerfectForwardSecrecy
                        ?? action.UseQuickModePerfectForwardSecrecy,
                    AcceptUnsecuredInbound = action.AcceptUnsecuredInbound,
                    AllowUnsecuredFallback = action.AllowUnsecuredFallback,
                    QuickModeSecurityMethods =
                        options.QuickModeSecurityMethods ?? action.QuickModeSecurityMethods
                }
                : null,
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
            Name = rule.IsDefaultResponseRule
                ? LocalizedStrings.IPSec_Rule_Dynamic
                : rule.Name,
            Description = rule.Description,
            FilterListName = rule.IsDefaultResponseRule
                ? LocalizedStrings.IPSec_Rule_Dynamic
                : rule.FilterListName,
            FilterActionName = rule.IsDefaultResponseRule
                ? LocalizedStrings.IPSec_Rule_DefaultResponse
                : rule.FilterActionName,
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

    public string ConnectionTypeDisplay { get; set; } = string.Empty;
}
