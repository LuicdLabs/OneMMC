using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OneMMC.Core.Features.SystemManagement.Models.ComExp;
using OneMMC.Core.Features.SystemManagement.ViewModels.ComExp;

using OneMMC.Localization;

namespace OneMMC.Views.ComExp;

public sealed partial class RunningProcessesPage : Page
{
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;
    public RunningProcessesViewModel ViewModel { get; }

    // Guards the programmatic root-node selection (in RebuildTree) so it does not re-enter the
    // SelectionChanged handler and trigger redundant work.
    private bool _suppressTreeSelection;

    public RunningProcessesPage()
    {
        ViewModel = App.GetRequiredService<RunningProcessesViewModel>();
        InitializeComponent();
        ViewModel.FilteredProcesses.CollectionChanged += OnFilteredProcessesChanged;
        Loaded += RunningProcessesPage_Loaded;
        Unloaded += RunningProcessesPage_Unloaded;
    }

    private async void RunningProcessesPage_Loaded(object sender, RoutedEventArgs e)
    {
        OneMMC.Services.Logging.UiLogger.LogDebug("[RunningProcessesPage] Loaded.");
        await ViewModel.LoadProcessesAsync();
        RebuildTree();
    }

    private void RunningProcessesPage_Unloaded(object sender, RoutedEventArgs e)
    {
        Loaded -= RunningProcessesPage_Loaded;
        Unloaded -= RunningProcessesPage_Unloaded;
        ViewModel.FilteredProcesses.CollectionChanged -= OnFilteredProcessesChanged;
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        OneMMC.Services.Logging.UiLogger.LogDebug("[RunningProcessesPage] Refresh requested.");
        await ViewModel.LoadProcessesAsync();
        RebuildTree();
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            ViewModel.ApplyFilter(sender.Text);
        }
    }

    // The view model owns the process data; whenever it refilters (load, search),
    // mirror it into the TreeView's explicit node hierarchy.
    private void OnFilteredProcessesChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildTree();

    /// <summary>Rebuilds the explicit TreeView node hierarchy from the filtered processes.</summary>
    private void RebuildTree()
    {
        ProcessesTreeView.RootNodes.Clear();

        var rootItem = new ComPlusTreeItem
        {
            Kind = ComPlusTreeNodeKind.Root,
            Title = LocalizedStrings.ComExp_RunningProcesses
        };
        var rootNode = new TreeViewNode { Content = rootItem, IsExpanded = true };
        foreach (var process in ViewModel.FilteredProcesses)
        {
            rootNode.Children.Add(BuildProcessNode(process));
        }

        ProcessesTreeView.RootNodes.Add(rootNode);

        // Show the root summary by default without re-entering SelectionChanged.
        _suppressTreeSelection = true;
        ProcessesTreeView.SelectedNode = rootNode;
        ViewModel.SelectedItem = rootItem;
        _suppressTreeSelection = false;
    }

    private static TreeViewNode BuildProcessNode(ComPlusRunningProcess process)
    {
        return new TreeViewNode
        {
            Content = new ComPlusTreeItem
            {
                Kind = ComPlusTreeNodeKind.Process,
                Title = process.DisplayTitle,
                Process = process
            },
            // The application child is realized on expand; see doc/MemoryManagement.md.
            HasUnrealizedChildren = true
        };
    }

    private static TreeViewNode BuildApplicationNode(ComPlusApplicationInstance instance)
    {
        return new TreeViewNode
        {
            Content = new ComPlusTreeItem
            {
                Kind = ComPlusTreeNodeKind.Application,
                Title = instance.ApplicationName,
                Instance = instance
            },
            HasUnrealizedChildren = instance.Components.Count > 0
        };
    }

    private static TreeViewNode BuildComponentNode(ComPlusComponentInfo component)
    {
        return new TreeViewNode
        {
            Content = new ComPlusTreeItem
            {
                Kind = ComPlusTreeNodeKind.Component,
                Title = component.DisplayName,
                Component = component
            }
        };
    }

    private void ProcessesTreeView_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        if (args.Node.Children.Count > 0 || args.Node.Content is not ComPlusTreeItem item)
        {
            return;
        }

        if (item.Kind == ComPlusTreeNodeKind.Root)
        {
            foreach (var process in ViewModel.FilteredProcesses)
            {
                args.Node.Children.Add(BuildProcessNode(process));
            }
        }
        else if (item.Kind == ComPlusTreeNodeKind.Process && item.Process is not null)
        {
            args.Node.Children.Add(BuildApplicationNode(item.Process.Instance));
        }
        else if (item.Kind == ComPlusTreeNodeKind.Application && item.Instance is not null)
        {
            foreach (var component in item.Instance.Components)
            {
                args.Node.Children.Add(BuildComponentNode(component));
            }
        }

        args.Node.HasUnrealizedChildren = false;
    }

    private void ProcessesTreeView_Collapsed(TreeView sender, TreeViewCollapsedEventArgs args)
    {
        if (args.Node.Content is not ComPlusTreeItem item)
        {
            return;
        }

        bool canExpand = item.Kind switch
        {
            ComPlusTreeNodeKind.Root => args.Node.Children.Count > 0,
            ComPlusTreeNodeKind.Process => true,
            ComPlusTreeNodeKind.Application => item.Instance is not null && item.Instance.Components.Count > 0,
            _ => false
        };

        if (!canExpand)
        {
            return;
        }

        args.Node.Children.Clear();
        args.Node.HasUnrealizedChildren = true;
    }

    private void ProcessesTreeView_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        if (_suppressTreeSelection || args.AddedItems.Count == 0)
        {
            return;
        }

        if (ResolveItem(args.AddedItems[0]) is { } item)
        {
            ViewModel.SelectedItem = item;
        }
    }

    private void ProcessesTreeView_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (ResolveItem(args.InvokedItem) is { } item)
        {
            ViewModel.SelectedItem = item;
        }
    }

    // Selection (AddedItems) and invocation (InvokedItem) yield a TreeViewNode for explicit-node
    // trees; tolerate a bare data item as well so the handler is robust to either binding mode.
    private static ComPlusTreeItem? ResolveItem(object? item) => item switch
    {
        TreeViewNode { Content: ComPlusTreeItem treeItem } => treeItem,
        ComPlusTreeItem treeItem => treeItem,
        _ => null,
    };
}
