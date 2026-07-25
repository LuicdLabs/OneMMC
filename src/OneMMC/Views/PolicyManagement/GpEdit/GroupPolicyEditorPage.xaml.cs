using Microsoft.UI.Xaml.Controls;
using OneMMC.Core.Features.PolicyManagement.ViewModels.GpEdit;
using OneMMC.Core.Features.PolicyManagement.Services.GpEdit;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using OneMMC.Helpers;
using OneMMC.Localization;
using Microsoft.Extensions.Logging;

namespace OneMMC.Views.PolicyManagement.GpEdit
{
    public sealed partial class GroupPolicyEditorPage : Page
    {
	public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;
        private readonly ILogger<GroupPolicyEditorPage> _logger;

        /// <summary>
        /// Owns the view model's lifetime. GroupPolicyEditorViewModel is a transient IDisposable holding
        /// a fully parsed ADMX bundle, so resolving it from the root provider would leave the container
        /// pinning one bundle per visit to this page. See doc/MemoryManagement.md.
        /// </summary>
        private readonly PageServiceScope _serviceScope = new();

        /// <summary>
        /// Cancels the policy load when the page is navigated away from, so leaving immediately after
        /// opening does not keep parsing on a background thread.
        /// </summary>
        private readonly CancellationTokenSource _loadCts = new();

        public GroupPolicyEditorViewModel ViewModel { get; }

        private string _lastSearchText = string.Empty;

        public GroupPolicyEditorPage()
        {
            _logger = App.GetRequiredService<ILogger<GroupPolicyEditorPage>>();
            ViewModel = _serviceScope.GetRequiredService<GroupPolicyEditorViewModel>();
            this.InitializeComponent();
            DataContext = ViewModel;
            ViewModel.RootNodes.CollectionChanged += RootNodes_CollectionChanged;

            // Subscribe to admin permission required event
            ViewModel.AdminPermissionRequired += OnAdminPermissionRequired;
            this.RequestedTheme = App.CurrentTheme;
            App.ThemeChanged += OnThemeChanged;

            this.Loaded += OnLoaded;
            this.Unloaded += OnUnloaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;

            try
            {
                await ViewModel.InitializeAsync(_loadCts.Token);
            }
            catch (OperationCanceledException)
            {
                // Page was navigated away from while the definitions were loading.
            }
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
            _loadCts.Cancel();

            App.ThemeChanged -= OnThemeChanged;
            ViewModel.AdminPermissionRequired -= OnAdminPermissionRequired;
            ViewModel.RootNodes.CollectionChanged -= RootNodes_CollectionChanged;
            PolicyTree.RootNodes.Clear();
            DataContext = null;
            Unloaded -= OnUnloaded;

            // Disposes the view model (releasing its policy tree) and drops the container's reference.
            _serviceScope.Dispose();
            _loadCts.Dispose();
        }

        private async void OnAdminPermissionRequired(object? sender, EventArgs e)
        {
            await AdminDialogHelper.ShowAdminRequiredDialogAsync(this.XamlRoot);
        }
        
        private void OnThemeChanged(ElementTheme theme)
        {
            this.RequestedTheme = theme;
        }

        private void RootNodes_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (sender is ObservableCollection<GroupPolicyTreeItem> rootNodes)
            {
                if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
                {
                    foreach (var item in e.NewItems)
                    {
                        if (item is GroupPolicyTreeItem treeItem)
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
        /// Creates a single tree node without realizing its subtree. <c>HasUnrealizedChildren</c> keeps the
        /// expand chevron visible; <see cref="PolicyTree_Expanding"/> fills the children on demand.
        /// </summary>
        private static TreeViewNode CreateTreeNode(GroupPolicyTreeItem item)
        {
            return new TreeViewNode
            {
                Content = item,
                HasUnrealizedChildren = item.HasChildren,
            };
        }

        private void PolicyTree_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
        {
            if (!args.Node.HasUnrealizedChildren || args.Node.Content is not GroupPolicyTreeItem item)
            {
                return;
            }

            item.EnsureChildrenPopulated();

            foreach (GroupPolicyTreeItem child in item.Children)
            {
                args.Node.Children.Add(CreateTreeNode(child));
            }

            args.Node.HasUnrealizedChildren = false;
        }

        private void PolicyTree_Collapsed(TreeView sender, TreeViewCollapsedEventArgs args)
        {
            if (args.Node.Content is not GroupPolicyTreeItem item || !item.HasChildren)
            {
                return;
            }

            // Release the closed branch so a session of browsing does not accumulate the whole tree.
            args.Node.Children.Clear();
            args.Node.HasUnrealizedChildren = true;
        }

        private void PolicyTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
        {
            if (args.InvokedItem is TreeViewNode node && 
                node.Content is GroupPolicyTreeItem treeItem)
            {
                ViewModel.SelectedNode = treeItem;
            }
        }

        private async void EditButton_Click(object sender, RoutedEventArgs e)
        {
            await EditSelectedPolicy();
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedNode?.Category != null)
            {
                ViewModel.UpdatePolicyList(ViewModel.SelectedNode.Category, ViewModel.SelectedNode.IsComputerConfiguration);
            }
        }

        private async void PoliciesListView_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            await EditSelectedPolicy();
        }

        private async Task EditSelectedPolicy()
        {
            if (ViewModel.SelectedPolicy != null && ViewModel.SelectedNode != null && ViewModel.SelectedPolicy.Policy != null)
            {
                // Pre-flight admin check (Pattern 1)
                var adminService = App.GetRequiredService<IAdminService>();
                if (!adminService.IsRunningAsAdmin)
                {
                    await AdminDialogHelper.ShowAdminRequiredDialogAsync(this.XamlRoot);
                    return;
                }

                var currentOptions = ViewModel.GetPolicyOptions(ViewModel.SelectedPolicy.Policy, ViewModel.SelectedNode.IsComputerConfiguration);
                var dialog = new PolicyEditorDialog(
                    ViewModel.SelectedPolicy.Policy,
                    ViewModel.SelectedPolicy.State,
                    currentOptions,
                    ViewModel.SelectedNode.IsComputerConfiguration
                );
                dialog.XamlRoot = this.XamlRoot;
                dialog.RequestedTheme = App.CurrentTheme;
                
                var result = await dialog.ShowAsync();
                _logger.LogDebug("Policy editor dialog result state: {ResultState}", dialog.ResultState);
                
                if (result == ContentDialogResult.Primary)
                {
                    try
                    {
                        // Save policy
                        ViewModel.SavePolicy(ViewModel.SelectedPolicy, dialog.ResultState, dialog.ResultOptions);
                        
                        // Save policy display name for later use
                        string policyDisplayName = ViewModel.SelectedPolicy.Policy.DisplayName;
                        
                        // Force refresh UI state
                        if (ViewModel.SelectedNode?.Category != null)
                        {
                            ViewModel.UpdatePolicyList(ViewModel.SelectedNode.Category, ViewModel.SelectedNode.IsComputerConfiguration);
                        }
                        
                        // Check for errors
                        if (!string.IsNullOrEmpty(ViewModel.LastErrorMessage))
                        {
                            var errorDialog = new ContentDialog
                            {
                                Title = LocalizedStrings.Policy_SaveFailed_Title,
                                Content = ViewModel.LastErrorMessage,
                                CloseButtonText = LocalizedStrings.Common_OKButton,
                                XamlRoot = this.XamlRoot,
                                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                                RequestedTheme = App.CurrentTheme
                            };
                            await errorDialog.ShowAsync();
                        }
                        else
                        {
                            // Show success message (use InfoBar or brief notification)
                            _logger.LogInformation("Policy saved successfully: {PolicyDisplayName}", policyDisplayName);
                        }
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException || adminService.IsPermissionError(ex))
                    {
                        // This should rarely happen due to pre-flight check, but kept as defense in depth
                        await AdminDialogHelper.ShowAdminRequiredDialogAsync(this.XamlRoot);
                    }
                    catch (Exception ex)
                    {
                        var errorDialog = new ContentDialog
                        {
                            Title = LocalizedStrings.Policy_SaveFailed_Title,
                            Content = string.Format(LocalizedStrings.Policy_SaveFailed_MessageFormat, ex.Message),
                            CloseButtonText = LocalizedStrings.Common_OKButton,
                            XamlRoot = this.XamlRoot,
                            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                            RequestedTheme = App.CurrentTheme
                        };
                        await errorDialog.ShowAsync();
                    }
                }
            }
        }
    }
}


