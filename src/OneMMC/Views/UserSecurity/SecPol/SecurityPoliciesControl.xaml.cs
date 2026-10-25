using System;
using OneMMC.Core.Abstractions.Services;
using OneMMC.Core.Features.UserSecurity.Models.SecPol;
using OneMMC.Core.Features.UserSecurity.ViewModels.SecPol;
using OneMMC.Helpers;
using OneMMC.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace OneMMC.Views;

/// <summary>
/// The category tree, policy list and commands shared by the Account Policies and Local Policies
/// pages. The page supplies its ViewModel through <see cref="FrameworkElement.DataContext"/>.
/// </summary>
public sealed partial class SecurityPoliciesControl : UserControl
{
    private readonly LocalizedStrings LocalizedStrings = LocalizedStrings.Instance;
    private readonly ILogger<SecurityPoliciesControl> _logger;

    public SecurityPoliciesControl()
    {
        _logger = App.GetRequiredService<ILogger<SecurityPoliciesControl>>();
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
    }

    /// <summary>Gets the policy list state supplied by the page.</summary>
    public SecurityPoliciesViewModelBase? ViewModel { get; private set; }

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (ViewModel is not null)
            ViewModel.AdminPermissionRequired -= OnAdminPermissionRequired;

        ViewModel = args.NewValue as SecurityPoliciesViewModelBase;
        if (ViewModel is not null)
            ViewModel.AdminPermissionRequired += OnAdminPermissionRequired;

        // ViewModel is set after the page's InitializeComponent creates this control, so the
        // compiled bindings have to be evaluated again for the assigned instance.
        Bindings.Update();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _logger.LogDebug("[SecurityPoliciesControl] Loaded");

        // Select the category the ViewModel already loaded.
        if (PolicyTree.RootNodes.Count > 0)
        {
            PolicyTree.SelectedNode = PolicyTree.RootNodes[0];
        }
    }

    private async void OnAdminPermissionRequired(object? sender, EventArgs e)
    {
        await AdminDialogHelper.ShowAdminRequiredDialogAsync(XamlRoot);
    }

    private void PolicyTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs e)
    {
        if (ViewModel is not null && e.InvokedItem is PolicyCategoryItem category)
        {
            _logger.LogDebug("[SecurityPoliciesControl] Category selected: {CategoryDisplayName}", category.DisplayName);
            ViewModel.SelectedCategory = category;
        }
    }

    private async void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedPolicy is null) return;

        _logger.LogDebug("[SecurityPoliciesControl] Edit clicked: {PolicyKey}", ViewModel.SelectedPolicy.Definition.Key);
        await OpenEditorDialogAsync(ViewModel.SelectedPolicy);
    }

    private async void PoliciesList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (ViewModel?.SelectedPolicy is null) return;

        _logger.LogDebug("[SecurityPoliciesControl] Policy double-tapped: {PolicyKey}", ViewModel.SelectedPolicy.Definition.Key);
        await OpenEditorDialogAsync(ViewModel.SelectedPolicy);
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        _logger.LogDebug("[SecurityPoliciesControl] Refresh clicked");
        ViewModel?.RefreshCommand.Execute(null);
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (ViewModel is not null && args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            ViewModel.FilterText = sender.Text;
        }
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        _logger.LogDebug("[SecurityPoliciesControl] Export clicked");
        if (ViewModel is null || ViewModel.CurrentPolicies.Count == 0) return;

        await SecurityPolicyExport.ExportPoliciesAsync(
            ViewModel.SelectedCategory?.DisplayName,
            ViewModel.CurrentPolicies,
            _logger);
    }

    private async System.Threading.Tasks.Task OpenEditorDialogAsync(SecurityPolicyValue policy)
    {
        _logger.LogDebug("[SecurityPoliciesControl] Opening editor for: {PolicyKey}", policy.Definition.Key);

        // Check admin privileges before allowing edits
        var adminService = App.GetRequiredService<IAdminService>();
        if (!adminService.IsRunningAsAdmin)
        {
            await AdminDialogHelper.ShowAdminRequiredDialogAsync(this.XamlRoot);
            return;
        }

        var dialog = new SecurityPolicyEditorDialog();
        dialog.XamlRoot = this.XamlRoot;
        dialog.Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style;
        dialog.RequestedTheme = App.CurrentTheme;
        dialog.SetPolicy(policy);

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary && dialog.EditedValue != null && ViewModel is not null)
        {
            _logger.LogDebug("[SecurityPoliciesControl] Saving edited policy: {PolicyKey}", dialog.EditedValue.Definition.Key);
            await ViewModel.SavePolicyAsync(dialog.EditedValue);
        }
    }
}
