using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Authentication;
using ManagementTools.Core.Features.SystemManagement.Models.WF.ConnectionSecurity;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Monitoring;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Profiles;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Rules;
using ManagementTools.Core.Features.SystemManagement.Services.WF.ConnectionSecurity;
using ManagementTools.Core.Features.SystemManagement.Services.WF.Monitoring;
using ManagementTools.Core.Features.SystemManagement.Services.WF.Profiles;
using ManagementTools.Core.Features.SystemManagement.Services.WF.Rules;
using ManagementTools.Localization;
using ManagementTools.Views.Dialogs.ConnectionSecurity;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace ManagementTools.Views;

public sealed partial class FirewallMonitoringPage : Page
{
    private readonly FirewallMonitoringService _monitoringService;
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    private readonly ObservableCollection<MonitoringItem> _currentItems = [];
    private MonitoringType _currentType = MonitoringType.None;
    private string _lastSearchText = string.Empty;
    private CancellationTokenSource? _loadCancellationTokenSource;

    public FirewallMonitoringPage()
    {
        _monitoringService = App.GetRequiredService<FirewallMonitoringService>();

        InitializeComponent();
        InitializeTreeView();
        RequestedTheme = App.CurrentTheme;
        App.ThemeChanged += OnThemeChanged;
        Unloaded += OnUnloaded;
    }

    private void InitializeTreeView()
    {
        var monitoringRoot = new TreeViewNode
        {
            Content = new MonitoringTreeItem { Name = LocalizedStrings.WF_Monitoring_PageTitle },
            IsExpanded = true
        };

        monitoringRoot.Children.Add(new TreeViewNode
        {
            Content = new MonitoringTreeItem { Name = LocalizedStrings.WF_Monitoring_Node_Firewall, Type = MonitoringType.Firewall }
        });
        monitoringRoot.Children.Add(new TreeViewNode
        {
            Content = new MonitoringTreeItem { Name = LocalizedStrings.WF_ConnectionSecurityRules_PageTitle, Type = MonitoringType.ConnectionSecurityRules }
        });

        var securityAssociationsNode = new TreeViewNode
        {
            Content = new MonitoringTreeItem { Name = LocalizedStrings.WF_Monitoring_Node_SecurityAssociations, Type = MonitoringType.SecurityAssociations },
            IsExpanded = true
        };
        securityAssociationsNode.Children.Add(new TreeViewNode
        {
            Content = new MonitoringTreeItem { Name = LocalizedStrings.WF_Monitoring_Node_MainMode, Type = MonitoringType.MainMode }
        });
        securityAssociationsNode.Children.Add(new TreeViewNode
        {
            Content = new MonitoringTreeItem { Name = LocalizedStrings.WF_Monitoring_Node_QuickMode, Type = MonitoringType.QuickMode }
        });
        monitoringRoot.Children.Add(securityAssociationsNode);

        MonitoringTree.RootNodes.Add(monitoringRoot);

        if (monitoringRoot.Children[0] is TreeViewNode firewallNode)
        {
            MonitoringTree.SelectedNode = firewallNode;
            _ = LoadMonitoringDataAsync(MonitoringType.Firewall);
        }
    }

    private void OnThemeChanged(ElementTheme theme)
    {
        RequestedTheme = theme;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        App.ThemeChanged -= OnThemeChanged;
        _loadCancellationTokenSource?.Cancel();
        _loadCancellationTokenSource?.Dispose();
        _loadCancellationTokenSource = null;
        _currentItems.Clear();
        DetailsListView.ItemsSource = null;
    }

    private void MonitoringTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is TreeViewNode node && node.Content is MonitoringTreeItem item)
        {
            _ = LoadMonitoringDataAsync(item.Type);
        }
    }

    private async Task LoadMonitoringDataAsync(MonitoringType type)
    {
        _loadCancellationTokenSource?.Cancel();
        _loadCancellationTokenSource?.Dispose();
        var cancellationTokenSource = new CancellationTokenSource();
        _loadCancellationTokenSource = cancellationTokenSource;

        _currentType = type;
        LoadingRing.IsActive = true;

        try
        {
            await Task.Yield();
            List<MonitoringItem> items = await Task.Run(
                () => LoadMonitoringItems(type).ToList(),
                cancellationTokenSource.Token);

            cancellationTokenSource.Token.ThrowIfCancellationRequested();

            _currentItems.Clear();
            foreach (MonitoringItem item in items)
            {
                _currentItems.Add(item);
            }

            DetailsListView.ItemsSource = _currentItems;
            FilterItems(_lastSearchText);
        }
        catch (OperationCanceledException) when (cancellationTokenSource.IsCancellationRequested)
        {
        }
        catch
        {
            _currentItems.Clear();
            DetailsListView.ItemsSource = _currentItems;
        }
        finally
        {
            if (ReferenceEquals(_loadCancellationTokenSource, cancellationTokenSource))
            {
                _loadCancellationTokenSource = null;
                LoadingRing.IsActive = false;
            }

            cancellationTokenSource.Dispose();
        }
    }

    private IEnumerable<MonitoringItem> LoadMonitoringItems(MonitoringType type)
        => type switch
        {
            MonitoringType.Firewall => _monitoringService.GetActiveFirewallRules().Select(MonitoringItem.FromFirewallRule),
            MonitoringType.ConnectionSecurityRules => _monitoringService.GetConnectionSecurityRules().Select(MonitoringItem.FromConnectionSecurityRule),
            MonitoringType.MainMode => _monitoringService.GetMainModeSecurityAssociations().Select(MonitoringItem.FromMainModeAssociation),
            MonitoringType.QuickMode => _monitoringService.GetQuickModeSecurityAssociations().Select(MonitoringItem.FromQuickModeAssociation),
            _ => Enumerable.Empty<MonitoringItem>()
        };

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadMonitoringDataAsync(_currentType);
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        _lastSearchText = sender.Text ?? string.Empty;
        FilterItems(_lastSearchText);
    }

    private void FilterItems(string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            DetailsListView.ItemsSource = _currentItems;
            return;
        }

        DetailsListView.ItemsSource = _currentItems
            .Where(item =>
                item.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                item.Description.Contains(searchText, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private async void DetailsListView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (DetailsListView.SelectedItem is not MonitoringItem item)
        {
            return;
        }

        switch (_currentType)
        {
            case MonitoringType.Firewall when item.FirewallRule is not null:
                await ShowFirewallRulePropertiesDialogAsync(item.FirewallRule);
                break;
            case MonitoringType.ConnectionSecurityRules when item.ConnectionSecurityRule is not null:
                var csrDialog = new ConnectionSecurityRulePropertiesDialog
                {
                    XamlRoot = XamlRoot,
                    RequestedTheme = App.CurrentTheme,
                    Title = FormatPropertiesTitle(item.Name)
                };
                csrDialog.LoadRule(item.ConnectionSecurityRule);
                await csrDialog.ShowAsync();
                break;
            case MonitoringType.MainMode when item.MainModeSecurityAssociation is not null:
                await ShowPropertyDialogAsync(
                    FormatPropertiesTitle(item.Name),
                    new Dictionary<string, string>
                    {
                        [LocalizedStrings.WF_Monitoring_Label_LocalEndpoint] = item.MainModeSecurityAssociation.LocalEndpoint,
                        [LocalizedStrings.WF_Monitoring_Label_RemoteEndpoint] = item.MainModeSecurityAssociation.RemoteEndpoint,
                        [LocalizedStrings.WF_Monitoring_Label_Mode] = item.MainModeSecurityAssociation.MainMode,
                        [LocalizedStrings.WF_Monitoring_Label_FirstAuthentication] = item.MainModeSecurityAssociation.FirstAuthMethod,
                        [LocalizedStrings.WF_Monitoring_Label_SecondAuthentication] = item.MainModeSecurityAssociation.SecondAuthMethod,
                        [LocalizedStrings.WF_Monitoring_Label_Cipher] = item.MainModeSecurityAssociation.CipherAlgorithm,
                        [LocalizedStrings.WF_Monitoring_Label_Hash] = item.MainModeSecurityAssociation.HashAlgorithm,
                        [LocalizedStrings.WF_Monitoring_Label_KeyExchange] = item.MainModeSecurityAssociation.KeyExchange
                    });
                break;
            case MonitoringType.QuickMode when item.QuickModeSecurityAssociation is not null:
                await ShowPropertyDialogAsync(
                    FormatPropertiesTitle(item.Name),
                    new Dictionary<string, string>
                    {
                        [LocalizedStrings.WF_Monitoring_Label_LocalAddress] = item.QuickModeSecurityAssociation.LocalAddress,
                        [LocalizedStrings.WF_Field_LocalPort] = item.QuickModeSecurityAssociation.LocalPort,
                        [LocalizedStrings.WF_Monitoring_Label_RemoteAddress] = item.QuickModeSecurityAssociation.RemoteAddress,
                        [LocalizedStrings.WF_Field_RemotePort] = item.QuickModeSecurityAssociation.RemotePort,
                        [LocalizedStrings.WF_Field_Protocol] = item.QuickModeSecurityAssociation.Protocol,
                        [LocalizedStrings.WF_Monitoring_Label_AhIntegrity] = item.QuickModeSecurityAssociation.AhIntegrity,
                        [LocalizedStrings.WF_Monitoring_Label_EspIntegrity] = item.QuickModeSecurityAssociation.EspIntegrity,
                        [LocalizedStrings.WF_Monitoring_Label_EspEncryption] = item.QuickModeSecurityAssociation.EspEncryption
                    });
                break;
        }
    }

    private async System.Threading.Tasks.Task ShowFirewallRulePropertiesDialogAsync(FirewallRuleModel rule)
    {
        var generalTab = new SelectorBarItem { Text = LocalizedStrings.WF_Tab_General };
        var programsAndPortsTab = new SelectorBarItem { Text = LocalizedStrings.WF_Monitoring_ProgramsAndPortsTab };
        var advancedTab = new SelectorBarItem { Text = LocalizedStrings.WF_Tab_Advanced };

        var tabBar = new SelectorBar
        {
            Margin = new Thickness(0, 0, 0, 10)
        };
        tabBar.Items.Add(generalTab);
        tabBar.Items.Add(programsAndPortsTab);
        tabBar.Items.Add(advancedTab);

        FrameworkElement generalContent = BuildGeneralTabContent(rule);
        FrameworkElement programsAndPortsContent = BuildProgramsAndPortsTabContent(rule);
        FrameworkElement advancedContent = BuildAdvancedTabContent(rule);

        var contentHost = new Grid();
        contentHost.Children.Add(generalContent);
        contentHost.Children.Add(programsAndPortsContent);
        contentHost.Children.Add(advancedContent);

        void UpdateVisibleTab()
        {
            bool isGeneral = tabBar.SelectedItem == generalTab;
            bool isProgramsAndPorts = tabBar.SelectedItem == programsAndPortsTab;

            generalContent.Visibility = isGeneral ? Visibility.Visible : Visibility.Collapsed;
            programsAndPortsContent.Visibility = isProgramsAndPorts ? Visibility.Visible : Visibility.Collapsed;
            advancedContent.Visibility = !isGeneral && !isProgramsAndPorts ? Visibility.Visible : Visibility.Collapsed;
        }

        tabBar.SelectionChanged += (_, _) => UpdateVisibleTab();
        tabBar.SelectedItem = generalTab;
        UpdateVisibleTab();

        var dialog = new ContentDialog
        {
            Title = FormatPropertiesTitle(rule.DisplayName),
            Content = new StackPanel
            {
                MinWidth = 520,
                Spacing = 0,
                Children =
                {
                    tabBar,
                    new ScrollViewer
                    {
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                        Content = contentHost
                    }
                }
            },
            PrimaryButtonText = LocalizedStrings.Common_OKButton,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            RequestedTheme = App.CurrentTheme,
            XamlRoot = XamlRoot
        };

        await dialog.ShowAsync();
    }

    private FrameworkElement BuildGeneralTabContent(FirewallRuleModel rule)
    {
        var panel = new StackPanel { Spacing = 10 };

        panel.Children.Add(new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 120,
            Text = ToDisplayValue(rule.Description, rule.DisplayDescription)
        });

        panel.Children.Add(CreateSectionSeparator());
        panel.Children.Add(CreateLabeledValueRow($"{LocalizedStrings.WF_Monitoring_Label_LocalIPAddress}:", ToDisplayValue(rule.LocalAddress, LocalizedStrings.WF_Common_Any)));
        panel.Children.Add(CreateLabeledValueRow($"{LocalizedStrings.WF_Monitoring_Label_RemoteIPAddress}:", ToDisplayValue(rule.RemoteAddress, LocalizedStrings.WF_Common_Any)));
        panel.Children.Add(CreateLabeledValueRow($"{LocalizedStrings.WF_Monitoring_Label_Direction}:", GetDirectionDisplay(rule.Direction)));
        panel.Children.Add(CreateSectionSeparator());
        panel.Children.Add(CreateLabeledValueRow($"{LocalizedStrings.WF_Field_Profile}:", ToDisplayValue(rule.Profile, LocalizedStrings.WF_Common_All)));

        return panel;
    }

    private FrameworkElement BuildProgramsAndPortsTabContent(FirewallRuleModel rule)
    {
        var panel = new StackPanel { Spacing = 10 };

        panel.Children.Add(CreateLabeledValueRow($"{LocalizedStrings.WF_Field_Protocol}:", GetProtocolDisplay(rule)));
        panel.Children.Add(CreateLabeledValueRow($"{LocalizedStrings.WF_Field_LocalPort}:", ToDisplayValue(rule.LocalPort, LocalizedStrings.WF_Common_Any)));
        panel.Children.Add(CreateLabeledValueRow($"{LocalizedStrings.WF_Field_RemotePort}:", ToDisplayValue(rule.RemotePort, LocalizedStrings.WF_Common_Any)));
        panel.Children.Add(new TextBlock
        {
            Text = LocalizedStrings.WF_Monitoring_IcmpSettings,
            FontWeight = Microsoft.UI.Text.FontWeights.Normal
        });
        panel.Children.Add(new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 84,
            Text = ToDisplayValue(rule.IcmpTypesAndCodes, LocalizedStrings.WF_Common_None)
        });

        panel.Children.Add(CreateSectionSeparator());
        panel.Children.Add(CreateLabeledValueRow($"{LocalizedStrings.WF_Field_Program}:", ToDisplayValue(rule.Program, LocalizedStrings.WF_Common_Any)));
        panel.Children.Add(CreateLabeledValueRow($"{LocalizedStrings.WF_Monitoring_Label_Service}:", ToDisplayValue(rule.ServiceName, LocalizedStrings.WF_Common_Any)));
        panel.Children.Add(CreateLabeledValueRow($"{LocalizedStrings.WF_Monitoring_Label_AppPackage}:", ToDisplayValue(rule.LocalAppPackageId, LocalizedStrings.WF_Common_None)));

        return panel;
    }

    private FrameworkElement BuildAdvancedTabContent(FirewallRuleModel rule)
    {
        var panel = new StackPanel { Spacing = 10 };

        panel.Children.Add(new TextBlock
        {
            Text = LocalizedStrings.WF_Monitoring_AuthorizedUsersComputers,
            FontWeight = Microsoft.UI.Text.FontWeights.Normal
        });
        panel.Children.Add(new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 84,
            Text = GetAuthorizedIdentitiesDisplay(rule)
        });

        panel.Children.Add(new TextBlock
        {
            Text = LocalizedStrings.WF_Monitoring_ExceptedUsersComputers,
            FontWeight = Microsoft.UI.Text.FontWeights.Normal
        });
        panel.Children.Add(new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 84,
            Text = ToDisplayValue(rule.LocalUserAuthorizedList, LocalizedStrings.WF_Common_Any)
        });

        panel.Children.Add(CreateLabeledValueRow(
            $"{LocalizedStrings.WF_Advanced_InterfaceTypes}:",
            GetInterfaceTypesDisplay(rule.InterfaceTypes)));
        panel.Children.Add(CreateLabeledValueRow($"{LocalizedStrings.WF_Monitoring_Label_EdgeTraversal}:", GetEdgeTraversalDisplay(rule.EdgeTraversal)));

        return panel;
    }

    private static FrameworkElement CreateLabeledValueRow(string label, string value)
    {
        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var labelBlock = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Top
        };
        var valueBlock = new TextBlock
        {
            Text = value,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            VerticalAlignment = VerticalAlignment.Top
        };

        Grid.SetColumn(labelBlock, 0);
        Grid.SetColumn(valueBlock, 1);
        row.Children.Add(labelBlock);
        row.Children.Add(valueBlock);
        return row;
    }

    private static FrameworkElement CreateSectionSeparator()
    {
        return new Border
        {
            Height = 1,
            Margin = new Thickness(0, 4, 0, 4),
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"]
        };
    }

    private string GetProtocolDisplay(FirewallRuleModel rule)
    {
        if (rule.Protocol == FirewallRuleProtocol.Custom)
        {
            return $"{LocalizedStrings.WF_Common_Custom} ({rule.ProtocolNumber})";
        }

        return rule.Protocol == FirewallRuleProtocol.Any
            ? LocalizedStrings.WF_Common_Any
            : rule.Protocol.ToString();
    }

    private string GetDirectionDisplay(FirewallRuleDirection direction)
        => direction == FirewallRuleDirection.Inbound
            ? LocalizedStrings.WF_Direction_Inbound
            : LocalizedStrings.WF_Direction_Outbound;

    private string GetInterfaceTypesDisplay(string interfaceTypes)
    {
        if (string.IsNullOrWhiteSpace(interfaceTypes) ||
            string.Equals(interfaceTypes, "All", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(interfaceTypes, "Any", StringComparison.OrdinalIgnoreCase))
        {
            return LocalizedStrings.WF_Common_AllInterfaceTypes;
        }

        string[] values = interfaceTypes
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => value switch
            {
                "Lan" or "LAN" or "Wired" => LocalizedStrings.WF_Common_LocalAreaNetworkLAN,
                "Wireless" => LocalizedStrings.WF_Common_Wireless,
                "RemoteAccess" => LocalizedStrings.WF_Common_RemoteAccess,
                _ => value
            })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        return values.Length == 0 ? LocalizedStrings.WF_Common_AllInterfaceTypes : string.Join(", ", values);
    }

    private string GetAuthorizedIdentitiesDisplay(FirewallRuleModel rule)
    {
        string machineList = ToDisplayValue(rule.RemoteMachineAuthorizedList, string.Empty);
        string userList = ToDisplayValue(rule.RemoteUserAuthorizedList, string.Empty);

        if (string.IsNullOrWhiteSpace(machineList) && string.IsNullOrWhiteSpace(userList))
        {
            return LocalizedStrings.WF_Common_Any;
        }

        if (string.IsNullOrWhiteSpace(machineList))
        {
            return userList;
        }

        if (string.IsNullOrWhiteSpace(userList))
        {
            return machineList;
        }

        return $"{machineList}{Environment.NewLine}{userList}";
    }

    private string GetEdgeTraversalDisplay(FirewallEdgeTraversal edgeTraversal)
        => edgeTraversal switch
        {
            FirewallEdgeTraversal.Allow => LocalizedStrings.WF_EdgeTraversal_AllowDisplay,
            FirewallEdgeTraversal.DeferToUser => LocalizedStrings.WF_EdgeTraversal_DeferToUserDisplay,
            FirewallEdgeTraversal.DeferToApp => LocalizedStrings.WF_EdgeTraversal_DeferToAppDisplay,
            _ => LocalizedStrings.WF_EdgeTraversal_BlockDisplay
        };

    private static string ToDisplayValue(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private async System.Threading.Tasks.Task ShowPropertyDialogAsync(string title, IReadOnlyDictionary<string, string> properties)
    {
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        int row = 0;
        foreach ((string key, string value) in properties)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var keyBlock = new TextBlock { Text = $"{key}:", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            var valueBlock = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
            Grid.SetRow(keyBlock, row);
            Grid.SetColumn(keyBlock, 0);
            Grid.SetRow(valueBlock, row);
            Grid.SetColumn(valueBlock, 1);
            grid.Children.Add(keyBlock);
            grid.Children.Add(valueBlock);
            row++;
        }

        var dialog = new ContentDialog
        {
            Title = title,
            Content = new ScrollViewer { Content = grid },
            CloseButtonText = LocalizedStrings.Common_OKButton,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            RequestedTheme = App.CurrentTheme,
            XamlRoot = XamlRoot
        };

        await dialog.ShowAsync();
    }

    private string FormatPropertiesTitle(string name)
        => string.Format(CultureInfo.CurrentCulture, LocalizedStrings.WF_Monitoring_PropertiesTitleFormat, name);

    private sealed class MonitoringTreeItem
    {
        public string Name { get; init; } = string.Empty;
        public MonitoringType Type { get; init; }
    }

    private sealed class MonitoringItem
    {
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public FirewallRuleModel? FirewallRule { get; init; }
        public ConnectionSecurityRuleModel? ConnectionSecurityRule { get; init; }
        public MainModeSecurityAssociationModel? MainModeSecurityAssociation { get; init; }
        public QuickModeSecurityAssociationModel? QuickModeSecurityAssociation { get; init; }

        public static MonitoringItem FromFirewallRule(FirewallRuleModel rule)
            => new()
            {
                Name = rule.DisplayName,
                Description = rule.DisplayDescription,
                FirewallRule = rule
            };

        public static MonitoringItem FromConnectionSecurityRule(ConnectionSecurityRuleModel rule)
            => new()
            {
                Name = rule.Name,
                Description = rule.Summary,
                ConnectionSecurityRule = rule
            };

        public static MonitoringItem FromMainModeAssociation(MainModeSecurityAssociationModel association)
            => new()
            {
                Name = association.LocalEndpoint,
                Description = association.RemoteEndpoint,
                MainModeSecurityAssociation = association
            };

        public static MonitoringItem FromQuickModeAssociation(QuickModeSecurityAssociationModel association)
            => new()
            {
                Name = association.LocalAddress,
                Description = association.RemoteAddress,
                QuickModeSecurityAssociation = association
            };
    }

    private enum MonitoringType
    {
        None,
        Firewall,
        ConnectionSecurityRules,
        SecurityAssociations,
        MainMode,
        QuickMode
    }
}
