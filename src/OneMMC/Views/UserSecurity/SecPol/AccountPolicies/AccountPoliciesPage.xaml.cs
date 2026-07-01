using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using OneMMC.Core.Features.UserSecurity.Models.SecPol;
using OneMMC.Core.Features.UserSecurity.ViewModels.SecPol;
using OneMMC.Helpers;
using OneMMC.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace OneMMC.Views;

public sealed partial class AccountPoliciesPage : Page
{
    private readonly LocalizedStrings LocalizedStrings = LocalizedStrings.Instance;
    private readonly ILogger<AccountPoliciesPage> _logger;
    public AccountPoliciesViewModel ViewModel { get; }

    public AccountPoliciesPage()
    {
        _logger = App.GetRequiredService<ILogger<AccountPoliciesPage>>();
        ViewModel = App.GetRequiredService<AccountPoliciesViewModel>();

        _logger.LogDebug("[AccountPoliciesPage] Initializing");
        InitializeComponent();
        DataContext = ViewModel;
        Loaded += OnPageLoaded;

        // Subscribe to admin permission required event
        ViewModel.AdminPermissionRequired += OnAdminPermissionRequired;

        this.Unloaded += (_, _) =>
        {
            ViewModel.AdminPermissionRequired -= OnAdminPermissionRequired;
            DataContext = null;
            Loaded -= OnPageLoaded;
        };
    }

    private async void OnAdminPermissionRequired(object? sender, EventArgs e)
    {
        await AdminDialogHelper.ShowAdminRequiredDialogAsync(this.XamlRoot);
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _logger.LogDebug("[AccountPoliciesPage] Page loaded");

        // Select the first category in the tree
        if (PolicyTree.ItemsSource is System.Collections.IList items && items.Count > 0)
        {
            var firstNode = PolicyTree.RootNodes.Count > 0 ? PolicyTree.RootNodes[0] : null;
            if (firstNode != null)
            {
                PolicyTree.SelectedNode = firstNode;
            }
        }
    }

    private void PolicyTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs e)
    {
        if (e.InvokedItem is PolicyCategoryItem category)
        {
            _logger.LogDebug("[AccountPoliciesPage] Category selected: {CategoryDisplayName}", category.DisplayName);
            ViewModel.SelectedCategory = category;
        }
    }

    private async void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedPolicy == null) return;

        _logger.LogDebug("[AccountPoliciesPage] Edit clicked: {PolicyKey}", ViewModel.SelectedPolicy.Definition.Key);
        await OpenEditorDialogAsync(ViewModel.SelectedPolicy);
    }

    private async void PoliciesList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (ViewModel.SelectedPolicy == null) return;

        _logger.LogDebug("[AccountPoliciesPage] Policy double-tapped: {PolicyKey}", ViewModel.SelectedPolicy.Definition.Key);
        await OpenEditorDialogAsync(ViewModel.SelectedPolicy);
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        _logger.LogDebug("[AccountPoliciesPage] Refresh clicked");
        ViewModel.RefreshCommand.Execute(null);
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            ViewModel.FilterText = sender.Text;
        }
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        _logger.LogDebug("[AccountPoliciesPage] Export clicked");
        if (ViewModel.CurrentPolicies.Count == 0) return;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        var path = await App.GetRequiredService<OneMMC.Core.Abstractions.Services.IFileDialogService>().SaveFileAsync(
            hwnd,
            $"{LocalizedStrings.SecPol_ExportFilter}\0*.txt\0",
            LocalizedStrings.SecPol_ExportButton,
            null,
            "txt",
            ViewModel.SelectedCategory?.DisplayName);

        if (string.IsNullOrEmpty(path)) return;

        var sb = new StringBuilder();
        sb.AppendLine($"{LocalizedStrings.SecPol_Header_Policy}\t{LocalizedStrings.SecPol_Header_SecuritySetting}");
        sb.AppendLine(new string('-', 80));
        foreach (var policy in ViewModel.CurrentPolicies)
        {
            sb.AppendLine($"{policy.Definition.DisplayName}\t{policy.DisplaySetting}");
        }
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        _logger.LogInformation("[AccountPoliciesPage] Exported {PolicyCount} policies to {ExportPath}", ViewModel.CurrentPolicies.Count, path);
    }

    private async System.Threading.Tasks.Task OpenEditorDialogAsync(SecurityPolicyValue policy)
    {
        _logger.LogDebug("[AccountPoliciesPage] Opening editor for: {PolicyKey}", policy.Definition.Key);

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

        if (result == ContentDialogResult.Primary && dialog.EditedValue != null)
        {
            _logger.LogDebug("[AccountPoliciesPage] Saving edited policy: {PolicyKey}", dialog.EditedValue.Definition.Key);
            await ViewModel.SavePolicyAsync(dialog.EditedValue);
        }
    }
}

