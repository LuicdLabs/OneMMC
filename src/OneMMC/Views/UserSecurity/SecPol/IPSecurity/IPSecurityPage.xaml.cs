using System.Globalization;
using OneMMC.Core.Features.UserSecurity.Models.SecPol.IPSecurity;
using OneMMC.Core.Features.UserSecurity.ViewModels.SecPol.IPSecurity;
using OneMMC.Helpers;
using OneMMC.Localization;
using OneMMC.Views.UserSecurity.SecPol.IPSecurity.Editors;
using OneMMC.Views.UserSecurity.SecPol.IPSecurity.Manage;
using OneMMC.Views.UserSecurity.SecPol.IPSecurity.Rules;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace OneMMC.Views;

public sealed partial class IPSecurityPage : Page
{
    private const int EditorDialogWidth = 820;
    private const int EditorDialogHeight = 720;

    private readonly ILogger<IPSecurityPage> _logger;
    private bool _hasLoaded;

    internal readonly LocalizedStrings LocalizedStrings = LocalizedStrings.Instance;

    public IPSecurityPoliciesViewModel ViewModel { get; }

    public IPSecurityPage()
    {
        _logger = App.GetRequiredService<ILogger<IPSecurityPage>>();
        ViewModel = App.GetRequiredService<IPSecurityPoliciesViewModel>();

        InitializeComponent();
        DataContext = ViewModel;
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
        ViewModel.AdminPermissionRequired += OnAdminPermissionRequired;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        if (_hasLoaded)
        {
            return;
        }

        _logger.LogDebug("[IPSecurityPage] Page loaded.");
        _hasLoaded = true;
        await ViewModel.LoadAsync();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.AdminPermissionRequired -= OnAdminPermissionRequired;
        DataContext = null;
        Loaded -= OnPageLoaded;
        Unloaded -= OnPageUnloaded;
    }

    private async void OnAdminPermissionRequired(object? sender, EventArgs e)
    {
        await AdminDialogHelper.ShowAdminRequiredDialogAsync(XamlRoot);
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.RefreshAsync();
    }

    private async void ViewDetailsButton_Click(object sender, RoutedEventArgs e)
    {
        await ShowPolicyDetailsAsync(ViewModel.SelectedPolicy);
    }

    private async void NewItemButton_Click(object sender, RoutedEventArgs e)
    {
        await CreateCurrentSectionItemAsync();
    }

    private async void EditItemButton_Click(object sender, RoutedEventArgs e)
    {
        await EditSelectedItemAsync();
    }

    private async void DeleteItemButton_Click(object sender, RoutedEventArgs e)
    {
        await DeleteSelectedItemAsync();
    }

    private async void AssignPolicyButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedPolicy?.Policy is { } policy)
        {
            await ViewModel.AssignPolicyAsync(policy.Name, isAssigned: true);
        }
    }

    private async void UnassignPolicyButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedPolicy?.Policy is { } policy)
        {
            await ViewModel.AssignPolicyAsync(policy.Name, isAssigned: false);
        }
    }

    private async void ManageRulesButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedPolicy?.Policy is not { } policy)
        {
            return;
        }

        var manager = new IPSecurityRulesManagerControl(
            policy,
            ViewModel.FilterListNames,
            ViewModel.FilterActionNames,
            ViewModel.AddRuleAsync,
            ViewModel.SetRuleAsync,
            ViewModel.DeleteRuleAsync);
        _ = await manager.ShowDialogAsync(XamlRoot);
        await ViewModel.RefreshAsync();
    }

    private async void ManageFiltersActionsButton_Click(object sender, RoutedEventArgs e)
    {
        var manager = new IPSecurityManageListsActionsControl(
            ViewModel.FilterLists,
            ViewModel.FilterActions,
            ViewModel.AddFilterListWithFiltersAsync,
            ViewModel.SetFilterListWithFiltersAsync,
            ViewModel.DeleteFilterListAsync,
            ViewModel.AddFilterActionAsync,
            ViewModel.SetFilterActionAsync,
            ViewModel.DeleteFilterActionAsync);
        _ = await manager.ShowDialogAsync(XamlRoot);
        await ViewModel.RefreshAsync();
    }

    private async void PolicyListView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        await EditSelectedItemAsync();
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            ViewModel.FilterText = sender.Text;
        }
    }

    private async Task ShowPolicyDetailsAsync(IPSecurityPolicyRow? row)
    {
        if (row?.CanViewDetails != true)
        {
            return;
        }

        var modal = new IPSecurityDetailsModal(row, XamlRoot);
        _ = await modal.ShowAsync();
    }

    private Task CreateCurrentSectionItemAsync()
    {
        return ShowPolicyEditorAsync(IPSecurityEditorMode.Create);
    }

    private Task EditSelectedItemAsync()
    {
        return ViewModel.SelectedPolicy is { Policy: not null } row
            ? ShowPolicyEditorAsync(IPSecurityEditorMode.Edit, row.Policy)
            : Task.CompletedTask;
    }

    private async Task DeleteSelectedItemAsync()
    {
        IPSecurityPolicyRow? row = ViewModel.SelectedPolicy;
        if (row?.Policy is null)
        {
            return;
        }

        string message = Format(LocalizedStrings.IPSec_DeletePolicy_MessageFormat, row.Name);
        if (!await ShowDeleteConfirmationAsync(message))
        {
            return;
        }

        await ViewModel.DeletePolicyAsync(row.Name);
    }

    private async Task ShowPolicyEditorAsync(
        IPSecurityEditorMode mode,
        IPSecurityPolicyDefinition? policy = null)
    {
        var editor = new IPSecurityPolicyEditorControl(mode, policy);
        IPSecurityPolicyCommandOptions? result = null;
        string title = mode == IPSecurityEditorMode.Create
            ? LocalizedStrings.IPSec_Dialog_CreatePolicy_Title
            : Format(LocalizedStrings.IPSec_Dialog_EditPolicy_TitleFormat, policy!.Name);

        if (await ShowEditorAsync(title, editor, () => editor.TryBuildResult(out result))
            != WindowDialogResult.Primary
            || result is null)
        {
            return;
        }

        if (mode == IPSecurityEditorMode.Create)
        {
            await ViewModel.AddPolicyAsync(result);
        }
        else
        {
            await ViewModel.SetPolicyAsync(result);
        }
    }

    /// <remarks>
    /// The policy editor opens no further dialogs, so it is hosted in a <c>ContentDialog</c>. Only
    /// the two manager surfaces need a real window, because they open editors on top of themselves.
    /// </remarks>
    private Task<WindowDialogResult> ShowEditorAsync(
        string title,
        UserControl editor,
        Func<bool> validate)
    {
        return InlineDialogHost.ShowAsync(new InlineDialogOptions
        {
            Title = title,
            Content = editor,
            XamlRoot = XamlRoot,
            RequestedTheme = App.CurrentTheme,
            PrimaryButtonText = LocalizedStrings.Common_SaveButton,
            CloseButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = WindowDialogResult.Primary,
            MaxWidth = EditorDialogWidth,
            MaxHeight = EditorDialogHeight,
            OnPrimaryButtonClick = validate
        });
    }

    private async Task<bool> ShowDeleteConfirmationAsync(string message)
    {
        return await InlineDialogHost.ShowAsync(new InlineDialogOptions
        {
            Title = LocalizedStrings.IPSec_DeleteConfirm_Title,
            Content = message,
            XamlRoot = XamlRoot,
            RequestedTheme = App.CurrentTheme,
            PrimaryButtonText = LocalizedStrings.Common_DeleteButton,
            CloseButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = WindowDialogResult.None,
            MaxWidth = 480,
            MaxHeight = 320
        }) == WindowDialogResult.Primary;
    }

    private static string Format(string format, string value)
    {
        return string.Format(CultureInfo.CurrentCulture, format, value);
    }
}
