using System;
using OneMMC.Core.Abstractions.Services;
using OneMMC.Core.Features.UserSecurity.Models.SecPol.SystemAudit;
using OneMMC.Core.Features.UserSecurity.ViewModels.SecPol.SystemAudit;
using OneMMC.Helpers;
using OneMMC.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace OneMMC.Views;

public sealed partial class SystemAuditPage : Page
{
    private readonly LocalizedStrings LocalizedStrings = LocalizedStrings.Instance;
    private readonly ILogger<SystemAuditPage> _logger;
    public SystemAuditViewModel ViewModel { get; }

    public SystemAuditPage()
    {
        _logger = App.GetRequiredService<ILogger<SystemAuditPage>>();
        ViewModel = App.GetRequiredService<SystemAuditViewModel>();

        _logger.LogDebug("[SystemAuditPage] Initializing");
        InitializeComponent();
        DataContext = ViewModel;
        Loaded += OnPageLoaded;

        // Subscribe to admin permission required event
        ViewModel.AdminPermissionRequired += OnAdminPermissionRequired;

        this.Unloaded += (_, _) =>
        {
            ViewModel.StopLoading();
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
        _logger.LogDebug("[SystemAuditPage] Page loaded");

        // Select the first category in the tree
        if (CategoryTree.RootNodes.Count > 0)
        {
            CategoryTree.SelectedNode = CategoryTree.RootNodes[0];
        }
    }

    private void CategoryTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs e)
    {
        if (e.InvokedItem is AuditCategoryItem category)
        {
            _logger.LogDebug("[SystemAuditPage] Category selected: {CategoryDisplayName}", category.DisplayName);
            ViewModel.SelectedCategory = category;
        }
    }

    private async void EditButton_Click(object sender, RoutedEventArgs e)
    {
        _logger.LogDebug("[SystemAuditPage] Edit clicked");
        await OpenPropertiesDialogAsync();
    }

    private async void SubcategoriesList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        _logger.LogDebug("[SystemAuditPage] Subcategory double-tapped");
        await OpenPropertiesDialogAsync();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        _logger.LogDebug("[SystemAuditPage] Refresh clicked");
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
        _logger.LogDebug("[SystemAuditPage] Export clicked");
        if (ViewModel.CurrentSubcategories.Count == 0) return;

        await SecurityPolicyExport.ExportAuditSubcategoriesAsync(
            ViewModel.SelectedCategory?.DisplayName,
            ViewModel.CurrentSubcategories,
            _logger);
    }

    private async System.Threading.Tasks.Task OpenPropertiesDialogAsync()
    {
        if (ViewModel.SelectedSubcategory is null)
            return;

        // Refuse before the dialog opens so a non-elevated user cannot make edits that are then discarded.
        var adminService = App.GetRequiredService<IAdminService>();
        if (!adminService.IsRunningAsAdmin)
        {
            await AdminDialogHelper.ShowAdminRequiredDialogAsync(XamlRoot);
            return;
        }

        var dialog = new SystemAuditPropertiesDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = App.CurrentTheme,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style
        };
        dialog.SetSubcategory(ViewModel.SelectedCategory, ViewModel.SelectedSubcategory);

        await dialog.ShowAsync();

        if (!dialog.WasAccepted || dialog.EditedSubcategory is null)
            return;

        _logger.LogDebug(
            "[SystemAuditPage] Saving audit subcategory: {SubcategoryDisplayName}",
            dialog.EditedSubcategory.DisplayName);

        await ViewModel.SaveSubcategoryAsync(dialog.EditedSubcategory);
    }
}
