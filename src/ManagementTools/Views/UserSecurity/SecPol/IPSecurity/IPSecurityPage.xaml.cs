using System.Globalization;
using ManagementTools.Core.Features.UserSecurity.Models.SecPol.IPSecurity;
using ManagementTools.Core.Features.UserSecurity.ViewModels.SecPol.IPSecurity;
using ManagementTools.Helpers;
using ManagementTools.Localization;
using ManagementTools.Views.UserSecurity.SecPol.IPSecurity.Editors;
using ManagementTools.Views.UserSecurity.SecPol.IPSecurity.Manage;
using ManagementTools.Views.UserSecurity.SecPol.IPSecurity.Rules;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace ManagementTools.Views;

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

    private Task<WindowDialogResult> ShowEditorAsync(
        string title,
        UserControl editor,
        Func<bool> validate)
    {
        var modal = new ModalDialogWindow(new ModalDialogOptions
        {
            Title = title,
            Content = editor,
            OwnerXamlRoot = XamlRoot,
            RequestedTheme = App.CurrentTheme,
            PrimaryButtonText = LocalizedStrings.Common_SaveButton,
            CloseButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = WindowDialogResult.Primary,
            IsPrimaryButtonLeading = true,
            Width = EditorDialogWidth,
            Height = EditorDialogHeight,
            OnPrimaryButtonClick = validate
        });

        return modal.ShowDialogAsync();
    }

    private async Task<bool> ShowDeleteConfirmationAsync(string message)
    {
        var modal = new ModalDialogWindow(new ModalDialogOptions
        {
            Title = LocalizedStrings.IPSec_DeleteConfirm_Title,
            Content = message,
            OwnerXamlRoot = XamlRoot,
            RequestedTheme = App.CurrentTheme,
            PrimaryButtonText = LocalizedStrings.Common_DeleteButton,
            CloseButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = WindowDialogResult.None,
            Width = 560,
            Height = 320
        });

        return await modal.ShowDialogAsync() == WindowDialogResult.Primary;
    }

    private static string Format(string format, string value)
    {
        return string.Format(CultureInfo.CurrentCulture, format, value);
    }
}
