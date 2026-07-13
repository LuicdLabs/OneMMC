using OneMMC.Core.Features.PolicyManagement.ViewModels.RSoP;
using Microsoft.UI.Xaml.Controls;
using OneMMC.Localization;
using Microsoft.UI.Xaml;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System;
using OneMMC.Core.Localization;

namespace OneMMC.Views;

public sealed partial class ResultantSetOfPolicyPage : Page
{
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;
    private readonly ILogger<ResultantSetOfPolicyPage> _logger;
    public ResultantSetOfPolicyViewModel ViewModel { get; }

    private string _lastSearchText = string.Empty;

    public ResultantSetOfPolicyPage()
    {
        _logger = App.GetRequiredService<ILogger<ResultantSetOfPolicyPage>>();
        ViewModel = App.GetRequiredService<ResultantSetOfPolicyViewModel>();
        this.InitializeComponent();
        DataContext = ViewModel;
        ViewModel.RootNodes.CollectionChanged += RootNodes_CollectionChanged;

        this.RequestedTheme = App.CurrentTheme;
        App.ThemeChanged += OnThemeChanged;

        this.Loaded += OnLoaded;
        this.Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();
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
        App.ThemeChanged -= OnThemeChanged;
        ViewModel.RootNodes.CollectionChanged -= RootNodes_CollectionChanged;
        ViewModel.Dispose();
        PolicyTree.RootNodes.Clear();
        DataContext = null;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
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

    private TreeViewNode CreateTreeNode(RSoPTreeItem item)
    {
        var node = new TreeViewNode() { Content = item };
        foreach (var child in item.Children)
        {
            node.Children.Add(CreateTreeNode(child));
        }
        return node;
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
