using System;
using System.Threading.Tasks;
using OneMMC.Core.Features.PrintManagement.Models.PrintManagement;
using OneMMC.Core.Features.PrintManagement.Services.PrintManagement;
using OneMMC.Helpers;
using OneMMC.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OneMMC.Views;

/// <summary>
/// Dialog for deploying a printer connection via Group Policy.
/// </summary>
public sealed partial class DeployPrinterDialog : ContentDialog
{
    private readonly GpoPrinterDeploymentService _gpoDeploymentService;
    private readonly IAdminService _adminService;
    private readonly ILogger<DeployPrinterDialog> _logger;
    private readonly DeployPrinterDialogViewModel _viewModel;

    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    public DeployPrinterDialog(string printerName, string connectionPath, XamlRoot xamlRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionPath);

        InitializeComponent();

        _gpoDeploymentService = App.GetRequiredService<GpoPrinterDeploymentService>();
        _adminService = App.GetRequiredService<IAdminService>();
        _logger = App.GetRequiredService<ILogger<DeployPrinterDialog>>();
        _viewModel = new DeployPrinterDialogViewModel(printerName, connectionPath);

        DataContext = _viewModel;
        XamlRoot = xamlRoot;
        RequestedTheme = App.CurrentTheme;
        Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style;

        Title = string.Format(LocalizedStrings.PrintMgmt_DeployDialogTitleFormat, printerName);
        PrimaryButtonText = LocalizedStrings.Common_OKButton;
        SecondaryButtonText = LocalizedStrings.PrintMgmt_DeployDialogApplyButton;
        CloseButtonText = LocalizedStrings.Common_CancelButton;
        DefaultButton = ContentDialogButton.Primary;
    }

    public async Task LoadAsync()
    {
        try
        {
            var deployments = await _gpoDeploymentService.GetDeploymentsAsync(_viewModel.ConnectionPath);
            _viewModel.Initialize(deployments);
            await LoadGpoListAsync();
        }
        catch (Exception ex)
        {
            await HandleActionExceptionAsync(ex);
        }
    }

    private void GpoListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is GroupPolicyObjectInfo gpo)
        {
            _viewModel.SelectedGpo = gpo;
            GpoFlyout.Hide();
        }
    }

    private void AddDeployment_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.AddSelectedDeployments();
    }

    private void RemoveDeployment_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.RemoveSelectedDeployment();
    }

    private void RemoveAllDeployments_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.RemoveAllDeployments();
    }

    private async void PrimaryButton_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        bool success = await ApplyChangesAsync();
        if (success)
        {
            Hide();
        }
    }

    private async void SecondaryButton_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        await ApplyChangesAsync();
    }

    private async Task<bool> ApplyChangesAsync()
    {
        if (!_viewModel.HasPendingChanges)
        {
            return true;
        }

        try
        {
            foreach (var deployment in _viewModel.RemovedDeployments)
            {
                await _gpoDeploymentService.RemoveDeploymentAsync(deployment.DistinguishedName);
            }

            foreach (var deployment in _viewModel.AddedDeployments)
            {
                await _gpoDeploymentService.AddDeploymentAsync(
                    deployment.GpoGuid,
                    deployment.ConnectionPath,
                    deployment.ConnectionType);
            }

            await LoadAsync();
            return true;
        }
        catch (Exception ex)
        {
            await HandleActionExceptionAsync(ex);
            return false;
        }
    }

    private async Task LoadGpoListAsync()
    {
        _viewModel.IsGpoLoading = true;
        _viewModel.GpoLoadError = null;
        _viewModel.AvailableGpos.Clear();

        try
        {
            var gpos = await _gpoDeploymentService.GetGroupPolicyObjectsAsync();
            foreach (var gpo in gpos)
            {
                _viewModel.AvailableGpos.Add(gpo);
            }

            if (_viewModel.AvailableGpos.Count == 0)
            {
                _viewModel.GpoLoadError = LocalizedStrings.PrintMgmt_GpoBrowseDialogEmpty;
            }
        }
        catch (InvalidOperationException)
        {
            _viewModel.GpoLoadError = LocalizedStrings.PrintMgmt_DeployDialogDomainRequired;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load GPO list.");
            _viewModel.GpoLoadError = string.Format(LocalizedStrings.PrintMgmt_ActionFailedFormat, ex.Message);
        }
        finally
        {
            _viewModel.IsGpoLoading = false;
        }
    }

    private async Task HandleActionExceptionAsync(Exception ex)
    {
        if (_adminService.IsPermissionError(ex))
        {
            await AdminDialogHelper.ShowAdminRequiredDialogAsync(XamlRoot);
            return;
        }

        if (ex is InvalidOperationException)
        {
            await ShowErrorDialogAsync(LocalizedStrings.PrintMgmt_DeployDialogDomainRequired);
            return;
        }

        _logger.LogError(ex, "Failed to update printer GPO deployment.");
        await ShowErrorDialogAsync(string.Format(LocalizedStrings.PrintMgmt_ActionFailedFormat, ex.Message));
    }

    private async Task ShowErrorDialogAsync(string message)
    {
        var dialog = new ContentDialog
        {
            Title = LocalizedStrings.Common_ErrorTitle,
            Content = message,
            CloseButtonText = LocalizedStrings.Common_CloseButton,
            XamlRoot = XamlRoot,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            RequestedTheme = App.CurrentTheme
        };

        await dialog.ShowAsync();
    }
}

