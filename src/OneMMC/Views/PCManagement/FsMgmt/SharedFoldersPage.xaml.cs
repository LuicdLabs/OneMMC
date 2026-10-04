using OneMMC.Core.Features.PCManagement.Models.FsMgmt;
using OneMMC.Core.Features.PCManagement.ViewModels.FsMgmt;
using OneMMC.Helpers;
using OneMMC.Localization;
using OneMMC.Views.FsMgmt;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

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

    private DispatcherQueueTimer? _liveUpdateTimer;
    private bool _isAdminDialogOpen;
    private bool _adminRequiredSeen;

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
        _adminRequiredSeen = false;
        await ViewModel.LoadAsync();

        // Live updating is part of how this page refreshes, not something the user turns on. It stays off
        // when the initial load needed elevation, to avoid re-prompting every interval.
        if (!_adminRequiredSeen)
        {
            StartLiveUpdates();
        }
    }

    private void SharedFoldersPage_Unloaded(object sender, RoutedEventArgs e)
    {
        StopLiveUpdates();

        ViewModel.AdminPermissionRequired -= ViewModel_AdminPermissionRequired;
    }

    private async void ViewModel_AdminPermissionRequired(object? sender, EventArgs e)
    {
        _adminRequiredSeen = true;

        // Live updates would otherwise re-trigger this prompt on every interval, so stop after the first
        // permission failure; Refresh still lets the user retry once they have elevated.
        StopLiveUpdates();

        if (_isAdminDialogOpen)
        {
            return;
        }

        _isAdminDialogOpen = true;
        try
        {
            await AdminDialogHelper.ShowAdminRequiredDialogAsync(XamlRoot);
        }
        finally
        {
            _isAdminDialogOpen = false;
        }
    }

    private void StartLiveUpdates()
    {
        _liveUpdateTimer ??= CreateLiveUpdateTimer();
        _liveUpdateTimer.Start();
        LiveUpdateStatusText.Text = LocalizedStrings.FsMgmt_AutoRefresh_Active;
    }

    private void StopLiveUpdates()
    {
        LiveUpdateStatusText.Text = string.Empty;

        if (_liveUpdateTimer is null)
        {
            return;
        }

        _liveUpdateTimer.Stop();
        _liveUpdateTimer.Tick -= LiveUpdateTimer_Tick;
        _liveUpdateTimer = null;
    }

    /// <summary>
    /// Interval between reads. Every tick re-enumerates shares, sessions, and open files over WMI and
    /// resolves client names, so a one-second cadence produced continuous allocation churn for a view
    /// that changes far more slowly than that. Five seconds still reads as "live" to the user, and the
    /// view model applies only the differences, so a tick that reads unchanged state updates nothing.
    /// </summary>
    private static readonly TimeSpan LiveUpdateInterval = TimeSpan.FromSeconds(5);

    private DispatcherQueueTimer CreateLiveUpdateTimer()
    {
        DispatcherQueueTimer timer = DispatcherQueue.CreateTimer();
        timer.Interval = LiveUpdateInterval;
        timer.IsRepeating = true;
        timer.Tick += LiveUpdateTimer_Tick;
        return timer;
    }

    private async void LiveUpdateTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        await ViewModel.OnLiveUpdateTickAsync();
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

        string client = string.IsNullOrWhiteSpace(session.ClientDisplayName)
            ? session.UserName
            : session.ClientDisplayName;
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
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = App.CurrentTheme,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private static T? GetTag<T>(object sender) where T : class
    {
        return sender is FrameworkElement { Tag: T value } ? value : null;
    }
}
