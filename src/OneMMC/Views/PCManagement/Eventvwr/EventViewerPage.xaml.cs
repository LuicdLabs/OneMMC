using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using OneMMC.Core.Features.PCManagement.Models.EventViewer;
using OneMMC.Core.Features.PCManagement.ViewModels.EventViewer;
using OneMMC.Helpers;
using OneMMC.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace OneMMC.Views;

public sealed partial class EventViewerPage : Page
{
    // Owns the view model's lifetime: EventViewerViewModel is a transient IDisposable, so resolving it
    // from the root provider would leave the container holding it (and its loaded events) until the
    // process exits. See doc/MemoryManagement.md.
    private readonly PageServiceScope _serviceScope = new();

    public EventViewerViewModel ViewModel { get; }
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    private int _previousDetailTabIndex = 0;

    public EventViewerPage()
    {
        ViewModel = _serviceScope.GetRequiredService<EventViewerViewModel>();

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
        // The detail frame's journal is deliberately not cleared here. ShowDetail sets
        // IsNavigationStackEnabled = false before its first Navigate, so the stacks never hold anything,
        // and the frame is discarded with this page in any case. The clear that used to run here was
        // therefore dead work that also threw: NavigationHistory::ValidateCanChangePageStack rejects any
        // journal edit while that frame has a navigation pending (E_INVALID_OPERATION / 0x800710DD),
        // which is exactly the state a detail navigation leaves behind when the user switches pages.
        DetailContentFrame.Content = null;
        EventLogTreeView.RootNodes.Clear();
        EventsListView.ItemsSource = null;
        DataContext = null;
        ViewModel.AdminPermissionRequired -= OnAdminPermissionRequired;
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        Loaded -= EventViewerPage_Loaded;
        Unloaded -= EventViewerPage_Unloaded;

        // Disposes the view model and releases the container's reference to it.
        _serviceScope.Dispose();
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
            TreeViewNode windowsLogs = EventLogTreeView.RootNodes[0];
            RealizeChildren(windowsLogs);
            windowsLogs.IsExpanded = true;
        }
    }

    /// <summary>
    /// Creates a single tree node without realizing its subtree. A machine can expose several hundred
    /// event channels, so mirroring the whole hierarchy up front built a node per channel before the user
    /// had opened anything. <c>HasUnrealizedChildren</c> keeps the expand chevron visible.
    /// </summary>
    private static TreeViewNode CreateTreeNode(EventLogTreeNode node)
    {
        return new TreeViewNode
        {
            Content = node,
            IsExpanded = false,
            HasUnrealizedChildren = node.Children.Count > 0,
        };
    }

    private static void RealizeChildren(TreeViewNode treeNode)
    {
        if (!treeNode.HasUnrealizedChildren || treeNode.Content is not EventLogTreeNode node)
        {
            return;
        }

        foreach (EventLogTreeNode child in node.Children)
        {
            treeNode.Children.Add(CreateTreeNode(child));
        }

        treeNode.HasUnrealizedChildren = false;
    }

    private void EventLogTreeView_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        RealizeChildren(args.Node);
    }

    private void EventLogTreeView_Collapsed(TreeView sender, TreeViewCollapsedEventArgs args)
    {
        if (args.Node.Content is not EventLogTreeNode node || node.Children.Count == 0)
        {
            return;
        }

        // Release the closed branch; it is rebuilt from the data tree if reopened.
        args.Node.Children.Clear();
        args.Node.HasUnrealizedChildren = true;
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
        if (ViewModel.SelectedEvent is null)
        {
            DetailContentFrame.Content = null;
            return;
        }

        bool isXmlTab = DetailsSelectorBar.SelectedItem == DetailsSelectorBar.Items[1];
        NavigationTransitionInfo transition = useSlide
            ? new SlideNavigationTransitionInfo { Effect = slideEffect }
            : new SuppressNavigationTransitionInfo();

        // Disable navigation stack so entries never accumulate — avoids
        // COMException 0x800710DD when clearing stacks during navigation.
        DetailContentFrame.IsNavigationStackEnabled = false;

        if (isXmlTab)
        {
            DetailContentFrame.Navigate(typeof(EventDetailsPage), null, transition);
            if (DetailContentFrame.Content is EventDetailsPage detailsPage)
            {
                detailsPage.SelectedEvent = ViewModel.SelectedEvent;
                detailsPage.Refresh();
            }
        }
        else
        {
            DetailContentFrame.Navigate(typeof(EventGeneralPage), null, transition);
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
        return await App.GetRequiredService<OneMMC.Core.Abstractions.Services.IFileDialogService>().SaveFileAsync(
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
}


