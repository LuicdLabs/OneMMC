using OneMMC.Core.Features.PolicyManagement.ViewModels.RSoP;
using Microsoft.UI.Xaml.Controls;
using OneMMC.Localization;
using Microsoft.UI.Xaml;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.Threading;
using System;
using OneMMC.Core.Localization;

namespace OneMMC.Views;

public sealed partial class ResultantSetOfPolicyPage : Page
{
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;
    private readonly ILogger<ResultantSetOfPolicyPage> _logger;

    /// <summary>
    /// Owns the transient view model and its disposable policy services. The ADMX bundle is borrowed
    /// from the process-wide provider, while the per-page policy-service handles are released with this
    /// scope. See doc/MemoryManagement.md.
    /// </summary>
    private readonly PageServiceScope _serviceScope = new();
    private readonly CancellationTokenSource _lifetimeCts = new();

    public ResultantSetOfPolicyViewModel ViewModel { get; }

    private string _lastSearchText = string.Empty;

    public ResultantSetOfPolicyPage()
    {
        _logger = App.GetRequiredService<ILogger<ResultantSetOfPolicyPage>>();
        ViewModel = _serviceScope.GetRequiredService<ResultantSetOfPolicyViewModel>();
        this.InitializeComponent();
        DataContext = ViewModel;
        ViewModel.RootNodes.CollectionChanged += RootNodes_CollectionChanged;

        this.RequestedTheme = App.CurrentTheme;
        App.ThemeChanged += OnThemeChanged;

        this.Loaded += OnLoaded;
        this.Unloaded += OnUnloaded;
        _serviceScope.Attach(this);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _ = ViewModel.InitializeAsync(_lifetimeCts.Token);
    }

    /// <summary>
    /// Builds the stats text showing how many configured policies are in the current view.
    /// </summary>
    public string GetStatsText(int configured)
    {
        var format = LocalizationProvider.Current.GetString(
            ResourceFileNames.Policy, RSoPKeys.StatsFormat);
        return string.Format(format, configured);
    }

    private void PolicySearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            var text = sender.Text ?? string.Empty;
            if (!string.Equals(_lastSearchText, text, StringComparison.Ordinal))
            {
                _lastSearchText = text;
                ViewModel.FilterPoliciesByName(text);
            }
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _lifetimeCts.Cancel();
        App.ThemeChanged -= OnThemeChanged;
        ViewModel.RootNodes.CollectionChanged -= RootNodes_CollectionChanged;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;

        _lifetimeCts.Dispose();
    }

    private void OnThemeChanged(ElementTheme theme)
    {
        this.RequestedTheme = theme;
    }

    private void RootNodes_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (sender is ObservableCollection<RSoPTreeItem> rootNodes)
        {
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
            {
                foreach (var item in e.NewItems)
                {
                    if (item is RSoPTreeItem treeItem)
                    {
                        var node = CreateTreeNode(treeItem);
                        PolicyTree.RootNodes.Add(node);
                    }
                }
            }
            else if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                PolicyTree.RootNodes.Clear();
                foreach (var item in rootNodes)
                {
                    var node = CreateTreeNode(item);
                    PolicyTree.RootNodes.Add(node);
                }
            }
        }
    }

    /// <summary>
    /// Creates a single tree node without realizing its subtree; <see cref="PolicyTree_Expanding"/> fills
    /// the children on demand. See doc/MemoryManagement.md.
    /// </summary>
    private static TreeViewNode CreateTreeNode(RSoPTreeItem item)
    {
        return new TreeViewNode
        {
            Content = item,
            HasUnrealizedChildren = item.Children.Count > 0,
        };
    }

    private void PolicyTree_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        if (!args.Node.HasUnrealizedChildren || args.Node.Content is not RSoPTreeItem item)
        {
            return;
        }

        foreach (RSoPTreeItem child in item.Children)
        {
            args.Node.Children.Add(CreateTreeNode(child));
        }

        args.Node.HasUnrealizedChildren = false;
    }

    private void PolicyTree_Collapsed(TreeView sender, TreeViewCollapsedEventArgs args)
    {
        if (args.Node.Content is not RSoPTreeItem item || item.Children.Count == 0)
        {
            return;
        }

        args.Node.Children.Clear();
        args.Node.HasUnrealizedChildren = true;
    }

    private void PolicyTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is TreeViewNode node &&
            node.Content is RSoPTreeItem treeItem)
        {
            ViewModel.SelectedNode = treeItem;
        }
    }

    private async Task ShowPolicyDetailsAsync()
    {
        if (ViewModel.SelectedPolicy is null) return;

        var policy = ViewModel.SelectedPolicy;
        var options = ViewModel.GetSelectedPolicyOptions();

        var dialog = new PolicyDetailsDialog
        {
            XamlRoot = this.XamlRoot,
            RequestedTheme = App.CurrentTheme
        };

        dialog.Initialize(policy, options);

        await dialog.ShowAsync().AsTask();
    }

    private async void ViewDetailsButton_Click(object sender, RoutedEventArgs e)
    {
        await ShowPolicyDetailsAsync();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Refresh();
    }

    private async void PoliciesListView_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        await ShowPolicyDetailsAsync();
    }
}
