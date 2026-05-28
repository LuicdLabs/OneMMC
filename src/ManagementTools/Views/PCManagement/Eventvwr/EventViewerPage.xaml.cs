using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using ManagementTools.Core.Features.PCManagement.Models.EventViewer;
using ManagementTools.Core.Features.PCManagement.ViewModels.EventViewer;
using ManagementTools.Helpers;
using ManagementTools.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace ManagementTools.Views;

public sealed partial class EventViewerPage : Page
{
    public EventViewerViewModel ViewModel { get; } = App.GetRequiredService<EventViewerViewModel>();
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    private int _previousDetailTabIndex = 0;
    private ScrollViewer? _eventsScrollViewer;

    public EventViewerPage()
    {
        InitializeComponent();

        PopulateLevelFilterCombo();

        ViewModel.AdminPermissionRequired += OnAdminPermissionRequired;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        Loaded += EventViewerPage_Loaded;
        Unloaded += EventViewerPage_Unloaded;
    }

    // ========================================================================
    // Lifecycle
    // ========================================================================

    private async void EventViewerPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel.RootNodes.Count == 0)
        {
            await ViewModel.InitializeAsync();
        }
    }

    private void EventViewerPage_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_eventsScrollViewer is not null)
        {
            _eventsScrollViewer.ViewChanged -= ScrollViewer_ViewChanged;
            _eventsScrollViewer = null;
        }

        DetailContentFrame.BackStack.Clear();
        DetailContentFrame.ForwardStack.Clear();
        DetailContentFrame.Content = null;
        EventLogTreeView.RootNodes.Clear();
        EventsListView.ItemsSource = null;
        DataContext = null;
        ViewModel.AdminPermissionRequired -= OnAdminPermissionRequired;
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        ViewModel.Dispose();
        Loaded -= EventViewerPage_Loaded;
        Unloaded -= EventViewerPage_Unloaded;
    }

    // ========================================================================
    // Tree View
    // ========================================================================

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.RootNodes))
        {
            PopulateTreeView();
        }
        else if (e.PropertyName == nameof(ViewModel.SelectedEvent))
        {
            NavigateDetailFrame(useSlide: false);
        }
    }

    private void PopulateTreeView()
    {
        EventLogTreeView.RootNodes.Clear();
        foreach (var node in ViewModel.RootNodes)
        {
            EventLogTreeView.RootNodes.Add(CreateTreeNode(node));
        }
        // Auto-expand Windows Logs
        if (EventLogTreeView.RootNodes.Count > 0)
        {
            EventLogTreeView.RootNodes[0].IsExpanded = true;
        }
    }

    private static TreeViewNode CreateTreeNode(EventLogTreeNode node)
    {
        var tvn = new TreeViewNode { Content = node, IsExpanded = false };
        foreach (var child in node.Children)
        {
            tvn.Children.Add(CreateTreeNode(child));
        }
        return tvn;
    }

    private async void EventLogTreeView_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is TreeViewNode tvn && tvn.Content is EventLogTreeNode node)
        {
            await ViewModel.SelectLogAsync(node);
        }
    }

    // ========================================================================
    // Details Tab Switching
    // ========================================================================

    private void DetailsSelectorBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        int currentIndex = sender.Items.IndexOf(sender.SelectedItem);
        var effect = currentIndex > _previousDetailTabIndex
            ? SlideNavigationTransitionEffect.FromRight
            : SlideNavigationTransitionEffect.FromLeft;
        _previousDetailTabIndex = currentIndex;
        NavigateDetailFrame(useSlide: true, slideEffect: effect);
    }

    private void NavigateDetailFrame(bool useSlide, SlideNavigationTransitionEffect slideEffect = SlideNavigationTransitionEffect.FromRight)
    {
        if (ViewModel.SelectedEvent is null) return;

        bool isXmlTab = DetailsSelectorBar.SelectedItem == DetailsSelectorBar.Items[1];
        NavigationTransitionInfo transition = useSlide
            ? new SlideNavigationTransitionInfo { Effect = slideEffect }
            : new SuppressNavigationTransitionInfo();

        if (isXmlTab)
        {
            DetailContentFrame.Navigate(typeof(EventDetailsPage), null, transition);
            DetailContentFrame.BackStack.Clear();
            DetailContentFrame.ForwardStack.Clear();
            if (DetailContentFrame.Content is EventDetailsPage detailsPage)
            {
                detailsPage.SelectedEvent = ViewModel.SelectedEvent;
                detailsPage.Refresh();
            }
        }
        else
        {
            DetailContentFrame.Navigate(typeof(EventGeneralPage), null, transition);
            DetailContentFrame.BackStack.Clear();
            DetailContentFrame.ForwardStack.Clear();
            if (DetailContentFrame.Content is EventGeneralPage generalPage)
            {
                generalPage.SelectedEvent = ViewModel.SelectedEvent;
                generalPage.Refresh();
            }
        }
    }

    // ========================================================================
    // Level Filter
    // ========================================================================

    private void PopulateLevelFilterCombo()
    {
        LevelFilterCombo.Items.Add(new ComboBoxItem { Content = LocalizedStrings.EventViewer_Filter_All, Tag = (byte?)null });
        LevelFilterCombo.Items.Add(new ComboBoxItem { Content = LocalizedStrings.EventViewer_Level_Critical, Tag = (byte?)1 });
        LevelFilterCombo.Items.Add(new ComboBoxItem { Content = LocalizedStrings.EventViewer_Level_Error, Tag = (byte?)2 });
        LevelFilterCombo.Items.Add(new ComboBoxItem { Content = LocalizedStrings.EventViewer_Level_Warning, Tag = (byte?)3 });
        LevelFilterCombo.Items.Add(new ComboBoxItem { Content = LocalizedStrings.EventViewer_Level_Information, Tag = (byte?)4 });
        LevelFilterCombo.SelectedIndex = 0;
    }

    private void LevelFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LevelFilterCombo.SelectedItem is ComboBoxItem item)
        {
            ViewModel.SelectedLevelFilter = item.Tag as byte?;
        }
    }

    // ========================================================================
    // Command Bar Actions
    // ========================================================================

    private async void ClearLogButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = LocalizedStrings.EventViewer_ClearLog_ConfirmTitle,
            Content = LocalizedStrings.EventViewer_ClearLog_ConfirmMessage,
            PrimaryButtonText = LocalizedStrings.EventViewer_ClearLog_SaveFirst,
            SecondaryButtonText = LocalizedStrings.EventViewer_ClearLog,
            CloseButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = App.CurrentTheme
        };

        if (Application.Current.Resources.TryGetValue("DefaultContentDialogStyle", out var style))
        {
            dialog.Style = style as Style;
        }

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            // Save first, then clear
            var path = await ShowSaveEvtxDialogAsync();
            if (!string.IsNullOrEmpty(path))
            {
                await ViewModel.ClearLogAsync(path);
            }
        }
        else if (result == ContentDialogResult.Secondary)
        {
            // Clear without saving
            await ViewModel.ClearLogAsync();
        }
    }

    private async void ExportLogButton_Click(object sender, RoutedEventArgs e)
    {
        var path = await ShowSaveEvtxDialogAsync();
        if (!string.IsNullOrEmpty(path))
        {
            await ViewModel.ExportLogAsync(path);
        }
    }

    private async void LogPropertiesButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadLogPropertiesAsync();
        if (ViewModel.CurrentLogInfo is null) return;

        var info = ViewModel.CurrentLogInfo;
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(MakePropertyRow(LocalizedStrings.EventViewer_LogProp_FullName, info.LogName));
        content.Children.Add(MakePropertyRow(LocalizedStrings.EventViewer_LogProp_LogPath, info.LogFilePath));
        content.Children.Add(MakePropertyRow(LocalizedStrings.EventViewer_LogProp_LogSize, FormatSize(info.LogFileSize)));
        content.Children.Add(MakePropertyRow(LocalizedStrings.EventViewer_LogProp_MaxSize, FormatSize(info.MaxLogFileSize)));
        content.Children.Add(MakePropertyRow(LocalizedStrings.EventViewer_LogProp_Enabled, info.IsEnabled.ToString()));
        content.Children.Add(MakePropertyRow(LocalizedStrings.EventViewer_LogProp_LogMode, info.LogMode));

        var dialog = new ContentDialog
        {
            Title = LocalizedStrings.EventViewer_LogProp_Title,
            Content = content,
            CloseButtonText = LocalizedStrings.Common_OKButton,
            XamlRoot = XamlRoot,
            RequestedTheme = App.CurrentTheme
        };

        if (Application.Current.Resources.TryGetValue("DefaultContentDialogStyle", out var style))
        {
            dialog.Style = style as Style;
        }

        await dialog.ShowAsync();
    }

    private void OpenLegacyEventViewer_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("eventvwr.msc") { UseShellExecute = true });
    }

    // ========================================================================
    // Helpers
    // ========================================================================

    private async Task<string?> ShowSaveEvtxDialogAsync()
    {
        if (App.MainWindowInstance is null) return null;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        return await App.GetRequiredService<ManagementTools.Core.Abstractions.Services.IFileDialogService>().SaveFileAsync(
            hwnd,
            "Event Log Files (*.evtx)\0*.evtx\0All Files\0*.*\0",
            initialDirectory: null,
            defaultExtension: "evtx",
            suggestedFileName: ViewModel.SelectedLogName ?? "EventLog");
    }

    private StackPanel MakePropertyRow(string label, string value)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            row.Children.Add(new TextBlock
            {
                Text = label,
                Width = 140,
                Style = (Style)Resources["SecondaryTextBlockStyle"]
            });
            row.Children.Add(new TextBlock { Text = value, IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap });
            return row;
        }


    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1048576) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / 1048576.0:F1} MB";
    }

    private async void OnAdminPermissionRequired(object? sender, EventArgs e)
    {
        await AdminDialogHelper.ShowAdminRequiredDialogAsync(XamlRoot);
    }

    private void EventsListView_Loaded(object sender, RoutedEventArgs e)
    {
        var scrollViewer = GetScrollViewer(EventsListView);
        if (scrollViewer != null)
        {
            if (!ReferenceEquals(_eventsScrollViewer, scrollViewer))
            {
                if (_eventsScrollViewer is not null)
                {
                    _eventsScrollViewer.ViewChanged -= ScrollViewer_ViewChanged;
                }

                _eventsScrollViewer = scrollViewer;
                _eventsScrollViewer.ViewChanged += ScrollViewer_ViewChanged;
            }
        }
    }

    private ScrollViewer? GetScrollViewer(DependencyObject depObj)
    {
        if (depObj is ScrollViewer viewer) return viewer;

        for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(depObj); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(depObj, i);
            var result = GetScrollViewer(child);
            if (result != null) return result;
        }
        return null;
    }

    private void ScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer)
        {
            // Trigger load when scrolled near the bottom (50 pixels threshold)
            if (scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - 50)
            {
                if (ViewModel.CanLoadMore && !ViewModel.IsLoading)
                {
                    _ = ViewModel.LoadMoreEventsAsync();
                }
            }
        }
    }
}


