// ============================================================================
// GroupMembersDialog.xaml.cs
//
// Group Members Management Dialog - Manage group members and non-members
// ============================================================================

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OneMMC.Localization;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OneMMC.Core.Features.UserSecurity.Models.AzMan;

namespace OneMMC.Views.UserSecurity.AzMan.Dialogs;

/// <summary>
/// Group members change result
/// </summary>
public class GroupMembersResult
{
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    public List<string> AddedMembers { get; set; } = [];
    public List<string> RemovedMembers { get; set; } = [];
    public List<string> AddedNonMembers { get; set; } = [];
    public List<string> RemovedNonMembers { get; set; } = [];
    public List<string> AddedAppMemberLinks { get; set; } = [];
    public List<string> RemovedAppMemberLinks { get; set; } = [];
    public List<string> AddedAppNonMemberLinks { get; set; } = [];
    public List<string> RemovedAppNonMemberLinks { get; set; } = [];
    public bool BizRuleChanged { get; set; }
    public string BizRule { get; set; } = string.Empty;
    public string BizRuleLanguage { get; set; } = string.Empty;
}

/// <summary>
/// Group Members Management Dialog
/// </summary>
public sealed partial class GroupMembersDialog : ContentDialog
{
    private readonly AzApplicationGroupInfo _group;
    private readonly LocalizedStrings _localizedStrings = LocalizedStrings.Instance;

    private readonly ObservableCollection<string> _members = [];
    private readonly ObservableCollection<string> _nonMembers = [];
    private readonly ObservableCollection<string> _appMemberLinks = [];
    private readonly ObservableCollection<string> _appNonMemberLinks = [];
    private readonly ObservableCollection<string> _availableAppGroups = [];

    private readonly Dictionary<string, string> _memberSidMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _nonMemberSidMap = new(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _originalMemberSids;
    private readonly HashSet<string> _originalNonMemberSids;
    private readonly HashSet<string> _originalAppMemberLinks;
    private readonly HashSet<string> _originalAppNonMemberLinks;
    private readonly string _originalBizRule;
    private readonly string _originalBizRuleLanguage;
    private string _currentBizRule;
    private string _currentBizRuleLanguage;

    public LocalizedStrings LocalizedStrings => _localizedStrings;

    /// <summary>
    /// Change result
    /// </summary>
    public GroupMembersResult? Result { get; private set; }

    /// <summary>
    /// Create dialog
    /// </summary>
    public GroupMembersDialog(AzApplicationGroupInfo group, IEnumerable<string>? availableAppGroups = null)
    {
        InitializeComponent();
        this.RequestedTheme = App.CurrentTheme;
        _group = group;

        _originalMemberSids = new HashSet<string>(group.Members, StringComparer.OrdinalIgnoreCase);
        _originalNonMemberSids = new HashSet<string>(group.NonMembers, StringComparer.OrdinalIgnoreCase);
        _originalAppMemberLinks = new HashSet<string>(group.AppMemberLinks, StringComparer.OrdinalIgnoreCase);
        _originalAppNonMemberLinks = new HashSet<string>(group.AppNonMemberLinks, StringComparer.OrdinalIgnoreCase);
        _originalBizRule = group.BizRule ?? string.Empty;
        _originalBizRuleLanguage = string.IsNullOrWhiteSpace(group.BizRuleLanguage) ? "VBScript" : group.BizRuleLanguage;
        _currentBizRule = _originalBizRule;
        _currentBizRuleLanguage = _originalBizRuleLanguage;

        foreach (var appGroup in availableAppGroups ?? [])
        {
            _availableAppGroups.Add(appGroup);
        }

        for (int i = 0; i < group.Members.Count; i++)
        {
            string displayName = (i < group.MemberNames.Count && !string.IsNullOrEmpty(group.MemberNames[i]))
                ? group.MemberNames[i]
                : group.Members[i];
            _memberSidMap[displayName] = group.Members[i];
        }

        for (int i = 0; i < group.NonMembers.Count; i++)
        {
            string displayName = (i < group.NonMemberNames.Count && !string.IsNullOrEmpty(group.NonMemberNames[i]))
                ? group.NonMemberNames[i]
                : group.NonMembers[i];
            _nonMemberSidMap[displayName] = group.NonMembers[i];
        }

        Title = string.Format(_localizedStrings.GroupMembersDialog_TitleWithName, group.Name);
        LoadData();
        ConfigureForGroupType();
    }

    /// <summary>
    /// Load existing data
    /// </summary>
    private void LoadData()
    {
        GroupNameTextBox.Text = _group.Name;
        GroupDescriptionTextBox.Text = _group.Description;
        GroupTypeTextBox.Text = _group.GroupTypeDisplayText;
        LdapQueryTextBox.Text = _group.LdapQuery;

        foreach (var name in _memberSidMap.Keys)
        {
            _members.Add(name);
        }

        foreach (var name in _nonMemberSidMap.Keys)
        {
            _nonMembers.Add(name);
        }

        foreach (var link in _group.AppMemberLinks)
        {
            _appMemberLinks.Add(link);
        }

        foreach (var link in _group.AppNonMemberLinks)
        {
            _appNonMemberLinks.Add(link);
        }

        MembersListView.ItemsSource = _members;
        NonMembersListView.ItemsSource = _nonMembers;
        AppMemberLinksListView.ItemsSource = _appMemberLinks;
        AppNonMemberLinksListView.ItemsSource = _appNonMemberLinks;
    }

    private void ConfigureForGroupType()
    {
        if (_group.GroupType == AzGroupType.LdapQuery)
        {
            // LDAP query groups: show General + Query tabs only
            MembersTabItem.Visibility = Visibility.Collapsed;
            ExclusionsTabItem.Visibility = Visibility.Collapsed;
            QueryTabItem.Visibility = Visibility.Visible;
            GroupMembersSelectorBar.SelectedItem = GeneralTabItem;

            GeneralPanel.Visibility = Visibility.Visible;
            QueryPanel.Visibility = Visibility.Collapsed;
            MembersPanel.Visibility = Visibility.Collapsed;
            ExclusionsPanel.Visibility = Visibility.Collapsed;

            IsPrimaryButtonEnabled = false;
        }
        else if (_group.GroupType == AzGroupType.Bizrule || _group.IsBizruleGroup)
        {
            // Business Rule groups: show General tab only, members are determined by script
            MembersTabItem.Visibility = Visibility.Collapsed;
            ExclusionsTabItem.Visibility = Visibility.Collapsed;
            QueryTabItem.Visibility = Visibility.Collapsed;
            EditBusinessRuleScriptButton.Visibility = Visibility.Visible;
            GroupMembersSelectorBar.SelectedItem = GeneralTabItem;

            GeneralPanel.Visibility = Visibility.Visible;
            QueryPanel.Visibility = Visibility.Collapsed;
            MembersPanel.Visibility = Visibility.Collapsed;
            ExclusionsPanel.Visibility = Visibility.Collapsed;

            IsPrimaryButtonEnabled = true;
        }
        else
        {
            // Basic groups: show General + Members + Exclusions tabs
            QueryTabItem.Visibility = Visibility.Collapsed;
            MembersTabItem.Visibility = Visibility.Visible;
            ExclusionsTabItem.Visibility = Visibility.Visible;
            GroupMembersSelectorBar.SelectedItem = MembersTabItem;
            GroupMembersSelectorBar_SelectionChanged(GroupMembersSelectorBar, null!);
            IsPrimaryButtonEnabled = true;
        }
    }

    private void OnAddMemberClick(object sender, RoutedEventArgs e)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        var selections = DirectoryObjectPickerService.ShowDialog(
            hwnd,
            ObjectPickerTypes.UsersAndGroups,
            multiSelect: true);

        if (selections is { Count: > 0 })
        {
            foreach (var obj in selections)
            {
                string sid = obj.Sid;
                if (!string.IsNullOrEmpty(sid) && !_memberSidMap.Values.Contains(sid, StringComparer.OrdinalIgnoreCase))
                {
                    _members.Add(obj.Name);
                    _memberSidMap[obj.Name] = sid;
                }
            }
        }
    }

    private void OnRemoveMemberClick(object sender, RoutedEventArgs e)
    {
        var selectedItems = MembersListView.SelectedItems.Cast<string>().ToList();
        foreach (var item in selectedItems)
        {
            _members.Remove(item);
            _memberSidMap.Remove(item);
        }
    }

    private void OnAddAppMemberClick(object sender, RoutedEventArgs e)
    {
        ShowAppGroupPickerFlyout(_appMemberLinks, sender as FrameworkElement);
    }

    private void OnRemoveAppMemberClick(object sender, RoutedEventArgs e)
    {
        var selectedItems = AppMemberLinksListView.SelectedItems.Cast<string>().ToList();
        foreach (var item in selectedItems)
        {
            _appMemberLinks.Remove(item);
        }
    }

    private void OnAddNonMemberClick(object sender, RoutedEventArgs e)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        var selections = DirectoryObjectPickerService.ShowDialog(
            hwnd,
            ObjectPickerTypes.UsersAndGroups,
            multiSelect: true);

        if (selections is { Count: > 0 })
        {
            foreach (var obj in selections)
            {
                string sid = obj.Sid;
                if (!string.IsNullOrEmpty(sid) && !_nonMemberSidMap.Values.Contains(sid, StringComparer.OrdinalIgnoreCase))
                {
                    _nonMembers.Add(obj.Name);
                    _nonMemberSidMap[obj.Name] = sid;
                }
            }
        }
    }

    private void OnRemoveNonMemberClick(object sender, RoutedEventArgs e)
    {
        var selectedItems = NonMembersListView.SelectedItems.Cast<string>().ToList();
        foreach (var item in selectedItems)
        {
            _nonMembers.Remove(item);
            _nonMemberSidMap.Remove(item);
        }
    }

    private void OnAddAppNonMemberClick(object sender, RoutedEventArgs e)
    {
        ShowAppGroupPickerFlyout(_appNonMemberLinks, sender as FrameworkElement);
    }

    private void OnRemoveAppNonMemberClick(object sender, RoutedEventArgs e)
    {
        var selectedItems = AppNonMemberLinksListView.SelectedItems.Cast<string>().ToList();
        foreach (var item in selectedItems)
        {
            _appNonMemberLinks.Remove(item);
        }
    }

    private void ShowAppGroupPickerFlyout(ObservableCollection<string> targetCollection, FrameworkElement? targetElement)
    {
        var existingSet = new HashSet<string>(targetCollection, StringComparer.OrdinalIgnoreCase);
        var candidates = _availableAppGroups.Where(g => !existingSet.Contains(g)).ToList();

        if (candidates.Count == 0)
        {
            return;
        }

        var flyout = new MenuFlyout();
        foreach (var candidate in candidates)
        {
            var item = new MenuFlyoutItem { Text = candidate };
            item.Click += (_, _) =>
            {
                if (!targetCollection.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                {
                    targetCollection.Add(candidate);
                }
            };
            flyout.Items.Add(item);
        }

        if (targetElement is not null)
        {
            flyout.ShowAt(targetElement);
        }
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var currentMemberSids = new HashSet<string>(
            _members.Select(name => _memberSidMap.GetValueOrDefault(name))
                .Where(sid => !string.IsNullOrEmpty(sid))!,
            StringComparer.OrdinalIgnoreCase);

        var currentNonMemberSids = new HashSet<string>(
            _nonMembers.Select(name => _nonMemberSidMap.GetValueOrDefault(name))
                .Where(sid => !string.IsNullOrEmpty(sid))!,
            StringComparer.OrdinalIgnoreCase);

        var currentAppMemberLinks = new HashSet<string>(_appMemberLinks, StringComparer.OrdinalIgnoreCase);
        var currentAppNonMemberLinks = new HashSet<string>(_appNonMemberLinks, StringComparer.OrdinalIgnoreCase);

        Result = new GroupMembersResult
        {
            AddedMembers = currentMemberSids.Except(_originalMemberSids, StringComparer.OrdinalIgnoreCase).ToList(),
            RemovedMembers = _originalMemberSids.Except(currentMemberSids, StringComparer.OrdinalIgnoreCase).ToList(),
            AddedNonMembers = currentNonMemberSids.Except(_originalNonMemberSids, StringComparer.OrdinalIgnoreCase).ToList(),
            RemovedNonMembers = _originalNonMemberSids.Except(currentNonMemberSids, StringComparer.OrdinalIgnoreCase).ToList(),
            AddedAppMemberLinks = currentAppMemberLinks.Except(_originalAppMemberLinks, StringComparer.OrdinalIgnoreCase).ToList(),
            RemovedAppMemberLinks = _originalAppMemberLinks.Except(currentAppMemberLinks, StringComparer.OrdinalIgnoreCase).ToList(),
            AddedAppNonMemberLinks = currentAppNonMemberLinks.Except(_originalAppNonMemberLinks, StringComparer.OrdinalIgnoreCase).ToList(),
            RemovedAppNonMemberLinks = _originalAppNonMemberLinks.Except(currentAppNonMemberLinks, StringComparer.OrdinalIgnoreCase).ToList(),
            BizRuleChanged = !_currentBizRule.Equals(_originalBizRule, StringComparison.Ordinal) || !_currentBizRuleLanguage.Equals(_originalBizRuleLanguage, StringComparison.Ordinal),
            BizRule = _currentBizRule,
            BizRuleLanguage = _currentBizRuleLanguage
        };
    }

    private async void OnEditBusinessRuleScriptClick(object sender, RoutedEventArgs e)
    {
        var languageComboBox = new ComboBox
        {
            Header = _localizedStrings.GroupMembersDialog_Language_Header,
            ItemsSource = new[] { "VBScript", "JScript" },
            SelectedItem = string.IsNullOrWhiteSpace(_currentBizRuleLanguage) ? "VBScript" : _currentBizRuleLanguage,
            Margin = new Thickness(0, 0, 0, 12)
        };

        var scriptTextBox = new TextBox
        {
            Header = _localizedStrings.GroupMembersDialog_BusinessRuleScript_Header,
            Text = _currentBizRule,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            MinHeight = 220
        };

        var panel = new StackPanel();
        panel.Children.Add(languageComboBox);
        panel.Children.Add(scriptTextBox);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = _localizedStrings.GroupMembersDialog_BusinessRuleScript_Title,
            Content = panel,
            PrimaryButtonText = LocalizedStrings.Common_SaveButton,
            CloseButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = ContentDialogButton.Primary,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            RequestedTheme = App.CurrentTheme
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            _currentBizRule = scriptTextBox.Text ?? string.Empty;
            _currentBizRuleLanguage = languageComboBox.SelectedItem as string ?? "VBScript";
        }
    }

    private void GroupMembersSelectorBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        GeneralPanel.Visibility = Visibility.Collapsed;
        QueryPanel.Visibility = Visibility.Collapsed;
        MembersPanel.Visibility = Visibility.Collapsed;
        ExclusionsPanel.Visibility = Visibility.Collapsed;

        if (sender.SelectedItem is SelectorBarItem { Tag: string tag })
        {
            switch (tag)
            {
                case "General":
                    GeneralPanel.Visibility = Visibility.Visible;
                    break;
                case "Query":
                    QueryPanel.Visibility = Visibility.Visible;
                    break;
                case "Members":
                    MembersPanel.Visibility = Visibility.Visible;
                    break;
                case "Exclusions":
                    ExclusionsPanel.Visibility = Visibility.Visible;
                    break;
            }
        }
    }
}
