using OneMMC.Core.Features.PCManagement.Models.FsMgmt;
using OneMMC.Core.Features.PCManagement.ViewModels.FsMgmt;
using OneMMC.Helpers;
using OneMMC.Localization;
using OneMMC.Views.FsMgmt;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace OneMMC.Views;

/// <summary>
/// Displays the local Shared Folders management surface.
/// </summary>
public sealed partial class SharedFoldersPage : Page
{
    /// <summary>
    /// Gets localized strings for XAML binding.
    /// </summary>
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    /// <summary>
    /// Gets the Shared Folders view model.
    /// </summary>
    public SharedFoldersViewModel ViewModel { get; }

    public SharedFoldersPage()
    {
        ViewModel = App.GetRequiredService<SharedFoldersViewModel>();
        InitializeComponent();

        Loaded += SharedFoldersPage_Loaded;
        Unloaded += SharedFoldersPage_Unloaded;
        ViewModel.AdminPermissionRequired += ViewModel_AdminPermissionRequired;
    }

    private async void SharedFoldersPage_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync();
    }

    private void SharedFoldersPage_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.AdminPermissionRequired -= ViewModel_AdminPermissionRequired;
        ViewModel.ClearCachedData();
    }

    private async void ViewModel_AdminPermissionRequired(object? sender, EventArgs e)
    {
        await AdminDialogHelper.ShowAdminRequiredDialogAsync(XamlRoot);
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        ViewModel.FilterText = sender.Text;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.RefreshAsync();
    }

    private async void AddShare_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ShareEditDialog();
        if (await dialog.ShowDialogAsync(XamlRoot) == ContentDialogResult.Primary)
        {
            await ViewModel.CreateShareAsync(dialog.Definition);
        }
    }

    private async void OpenShare_Click(object sender, RoutedEventArgs e)
    {
        if (GetTag<SharedFolderShare>(sender) is not { } share || string.IsNullOrWhiteSpace(share.Path))
        {
            return;
        }

        await Launcher.LaunchFolderPathAsync(share.Path);
    }

    private async void StopSharing_Click(object sender, RoutedEventArgs e)
    {
        if (GetTag<SharedFolderShare>(sender) is not { } share || share.IsAdministrative)
        {
            return;
        }

        bool confirmed = await ConfirmAsync(
            LocalizedStrings.FsMgmt_ConfirmStopSharing_Title,
            string.Format(LocalizedStrings.FsMgmt_ConfirmStopSharing_MessageFormat, share.Name),
            LocalizedStrings.FsMgmt_StopSharing);

        if (confirmed)
        {
            await ViewModel.DeleteShareAsync(share.Name);
        }
    }

    private async void ShareProperties_Click(object sender, RoutedEventArgs e)
    {
        if (GetTag<SharedFolderShare>(sender) is not { } share)
        {
            return;
        }

        var dialog = new ShareEditDialog(share);
        if (await dialog.ShowDialogAsync(XamlRoot) == ContentDialogResult.Primary && !dialog.IsReadOnly)
        {
            await ViewModel.UpdateShareAsync(share.Name, dialog.Definition);
        }
    }

    private async void DisconnectSession_Click(object sender, RoutedEventArgs e)
    {
        if (GetTag<SharedFolderSession>(sender) is not { } session)
        {
            return;
        }

        string client = string.IsNullOrWhiteSpace(session.ClientName)
            ? session.UserName
            : session.ClientName;
        bool confirmed = await ConfirmAsync(
            LocalizedStrings.FsMgmt_ConfirmDisconnectSession_Title,
            string.Format(LocalizedStrings.FsMgmt_ConfirmDisconnectSession_MessageFormat, client),
            LocalizedStrings.FsMgmt_DisconnectSelected);

        if (confirmed)
        {
            await ViewModel.DisconnectSessionAsync(session);
        }
    }

    private async void DisconnectAllSessions_Click(object sender, RoutedEventArgs e)
    {
        bool confirmed = await ConfirmAsync(
            LocalizedStrings.FsMgmt_ConfirmDisconnectAllSessions_Title,
            LocalizedStrings.FsMgmt_ConfirmDisconnectAllSessions_Message,
            LocalizedStrings.FsMgmt_DisconnectAllSessions);

        if (confirmed)
        {
            await ViewModel.DisconnectAllSessionsAsync();
        }
    }

    private async void CloseOpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (GetTag<SharedFolderOpenFile>(sender) is not { } openFile)
        {
            return;
        }

        bool confirmed = await ConfirmAsync(
            LocalizedStrings.FsMgmt_ConfirmCloseOpenFile_Title,
            string.Format(LocalizedStrings.FsMgmt_ConfirmCloseOpenFile_MessageFormat, openFile.Path),
            LocalizedStrings.FsMgmt_CloseSelected);

        if (confirmed)
        {
            await ViewModel.CloseOpenFileAsync(openFile);
        }
    }

    private async void CloseAllOpenFiles_Click(object sender, RoutedEventArgs e)
    {
        bool confirmed = await ConfirmAsync(
            LocalizedStrings.FsMgmt_ConfirmCloseAllOpenFiles_Title,
            LocalizedStrings.FsMgmt_ConfirmCloseAllOpenFiles_Message,
            LocalizedStrings.FsMgmt_CloseAllOpenFiles);

        if (confirmed)
        {
            await ViewModel.CloseAllOpenFilesAsync();
        }
    }

    private async Task<bool> ConfirmAsync(string title, string message, string primaryButtonText)
    {
        var modalWindow = new ModalDialogWindow(new ModalDialogOptions
        {
            Title = title,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap
            },
            OwnerXamlRoot = XamlRoot,
            RequestedTheme = App.CurrentTheme,
            ThemeChangeSubscribe = handler => App.ThemeChanged += handler,
            ThemeChangeUnsubscribe = handler => App.ThemeChanged -= handler,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = WindowDialogResult.None,
            Width = 500,
            Height = 220
        });

        return await modalWindow.ShowDialogAsync() == WindowDialogResult.Primary;
    }

    private static T? GetTag<T>(object sender) where T : class
    {
        return sender is FrameworkElement { Tag: T value } ? value : null;
    }
}
