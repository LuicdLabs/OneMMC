// ============================================================================
// AuthorizationStorePage.xaml.cs
//
// Authorization Store Page - Display applications and groups within the store
// ============================================================================

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using OneMMC.Core.Features.UserSecurity.Services.AzMan;
using OneMMC.Localization;
using OneMMC.Services;
using OneMMC.Views.UserSecurity.AzMan.AuthStore.AuthApplications;
using OneMMC.Views.UserSecurity.AzMan.Dialogs;
using CommunityToolkit.WinUI.Controls;
using System;
using System.Linq;
using OneMMC.Core.Features.UserSecurity.ViewModels.AzMan;
using OneMMC.Core.Features.UserSecurity.Models.AzMan;

namespace OneMMC.Views.UserSecurity.AzMan.AuthStore;

/// <summary>
/// Authorization Store Page
/// </summary>
public sealed partial class AuthorizationStorePage : Page
{
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    private AzManService? _service;
    private AuthorizationStoreViewModel? _viewModel;
    private AzAuthorizationStoreInfo? _store;
    private AuthorizationManagerViewModel? _managerViewModel;

    public AuthorizationStorePage()
    {
        InitializeComponent();
        this.RequestedTheme = App.CurrentTheme;
        App.ThemeChanged += OnThemeChanged;
        this.Unloaded += (_, _) =>
        {
            App.ThemeChanged -= OnThemeChanged;
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                _viewModel = null;
            }
            _service = null;
            _store = null;
            _managerViewModel = null;
        };
    }

    #region Navigation

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is StoreNavigationParameter param)
        {
            _service = param.Service;
            _store = param.Store;
            _managerViewModel = param.ManagerViewModel;
            _viewModel = new AuthorizationStoreViewModel(_service);
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

            UpdateStoreHeader();

            // Load data
            await _viewModel.LoadAsync(_store.StorePath);
            if (_viewModel.Store is not null)
            {
                _store = _viewModel.Store;
                UpdateStoreHeader();
            }
            // UI will update automatically
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
    }

    #endregion

    #region Event Handlers

    private void OnThemeChanged(ElementTheme theme)
    {
        this.RequestedTheme = theme;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // UI will update automatically through x:Bind Mode=OneWay
        DispatcherQueue.TryEnqueue(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(AuthorizationStoreViewModel.IsLoading):
                    LoadingRing.IsActive = _viewModel?.IsLoading ?? false;
                    break;

                case nameof(AuthorizationStoreViewModel.HasError):
                case nameof(AuthorizationStoreViewModel.StatusMessage):
                    UpdateStatusBar();
                    break;

                case nameof(AuthorizationStoreViewModel.Store):
                    if (_viewModel?.Store is not null)
                    {
                        _store = _viewModel.Store;
                    }
                    UpdateStoreHeader();
                    break;
            }
        });
    }

    private void OnApplicationSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (_viewModel != null && args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            _viewModel.ApplicationSearchText = sender.Text;
            // FilteredApplications will update automatically, UI will refresh through binding
        }
    }

    private void OnGroupSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (_viewModel != null && args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            _viewModel.GroupSearchText = sender.Text;
            // FilteredGroups will update automatically, UI will refresh through binding
        }
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null && _store != null)
        {
            await _viewModel.LoadAsync(_store.StorePath);
            if (_viewModel.Store is not null)
            {
                _store = _viewModel.Store;
                UpdateStoreHeader();
            }
            // UI will update automatically
        }
    }

    private async void OnPropertiesClick(object sender, RoutedEventArgs e)
    {
        if (_store == null) return;

        if (_viewModel != null)
        {
            try
            {
                await _viewModel.LoadAsync(_store.StorePath);
                if (_viewModel.Store is not null)
                {
                    _store = _viewModel.Store;
                    UpdateStoreHeader();
                }
            }
            catch
            {
                // Keep properties dialog usable even when pre-refresh fails.
            }
        }

        StoreAdvancedProperties? advancedProperties = null;
        if (_service != null)
        {
            try
            {
                advancedProperties = await _service.GetStoreAdvancedPropertiesAsync(_store.StorePath);
            }
            catch
            {
                // Keep dialog usable even when advanced properties cannot be read.
            }
        }

        var dialog = new StorePropertiesDialog(_store, advancedProperties)
        {
            XamlRoot = this.XamlRoot,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            RequestedTheme = App.CurrentTheme
        };

        while (true)
        {
            var result = await dialog.ShowAsync();
            if (dialog.ReopenRequested)
            {
                await dialog.WaitForReopenAsync();
                continue;
            }

            if (result == ContentDialogResult.Primary && dialog.Result != null && _service != null)
            {
                try
                {
                    // Update store properties
                    await _service.UpdateStorePropertiesAsync(
                        _store.StorePath,
                        dialog.Result.Description,
                        dialog.Result.ApplicationData,
                        dialog.Result.GenerateAudits);

                    // Update advanced limit/auditing settings
                    var advancedUpdate = new StoreAdvancedProperties
                    {
                        DomainTimeout = dialog.Result.UseDefaultValues ? StorePropertiesDialog.DefaultLdapQueryTimeout : dialog.Result.LdapQueryTimeout,
                        ScriptEngineTimeout = dialog.Result.AuthorizationRulesMode == AuthorizationRulesMode.EnableSpecifiedTimeout
                            ? dialog.Result.AuthorizationRuleTimeout
                            : 0,
                        MaxScriptEngines = dialog.Result.UseDefaultValues ? StorePropertiesDialog.DefaultMaxCachedAuthorizationRules : dialog.Result.MaximumCachedAuthorizationRules,
                        GenerateAudits = dialog.Result.RuntimeApplicationInitializationAuditing,
                        RuntimeApplicationInitializationAuditing = dialog.Result.RuntimeApplicationInitializationAuditing,
                        AuthorizationStoreChangeAuditing = dialog.Result.AuthorizationStoreChangeAuditing
                    };

                    if (dialog.Result.AuthorizationRulesMode == AuthorizationRulesMode.Disable)
                    {
                        advancedUpdate.ScriptEngineTimeout = 0;
                        advancedUpdate.MaxScriptEngines = 0;
                    }

                    await _service.UpdateStoreAdvancedPropertiesAsync(_store.StorePath, advancedUpdate);

                    if (dialog.Result.UpgradeSchemaToV2)
                    {
                        await _service.UpgradeStoreSchemaToV2Async(_store.StorePath);
                    }

                    // Update Policy Administrators
                    foreach (var admin in dialog.Result.AddedPolicyAdmins)
                    {
                        await _service.AddPolicyAdministratorAsync(_store.StorePath, admin);
                    }
                    foreach (var admin in dialog.Result.RemovedPolicyAdmins)
                    {
                        await _service.RemovePolicyAdministratorAsync(_store.StorePath, admin);
                    }

                    // Update Policy Readers
                    foreach (var reader in dialog.Result.AddedPolicyReaders)
                    {
                        await _service.AddPolicyReaderAsync(_store.StorePath, reader);
                    }
                    foreach (var reader in dialog.Result.RemovedPolicyReaders)
                    {
                        await _service.RemovePolicyReaderAsync(_store.StorePath, reader);
                    }

                    // Reload
                    if (_viewModel != null)
                    {
                        await _viewModel.LoadAsync(_store.StorePath);
                        if (_viewModel.Store is not null)
                        {
                            _store = _viewModel.Store;
                            UpdateStoreHeader();
                        }
                    }
                    ShowStatus(LocalizedStrings.AuthorizationStorePage_Status_PropertiesUpdated, false);
                }
                catch (Exception ex)
                {
                    ShowStatus(string.Format(LocalizedStrings.AuthorizationStorePage_Status_PropertiesUpdateFailed, ex.Message), true);
                }
            }

            break;
        }
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (_store == null) return;

        var dialog = new ContentDialog
        {
            XamlRoot = this.XamlRoot,
            Title = LocalizedStrings.AuthorizationStorePage_DeleteStore_Title,
            Content = string.Format(LocalizedStrings.AuthorizationStorePage_DeleteStore_Content, _store.Name),
            PrimaryButtonText = LocalizedStrings.Common_DeleteButton,
            CloseButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = ContentDialogButton.Close,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            RequestedTheme = App.CurrentTheme
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            try
            {
                bool success;

                if (_managerViewModel != null)
                {
                    success = await _managerViewModel.DeleteStoreAsync(_store);
                }
                else
                {
                    if (_service == null)
                    {
                        ShowStatus(LocalizedStrings.Common_ErrorTitle, true);
                        return;
                    }

                    await _service.DeleteStoreAsync(_store.StorePath);
                    success = true;
                }

                if (success && this.Frame.CanGoBack)
                {
                    this.Frame.GoBack();
                }
                else if (!success)
                {
                    ShowStatus(_managerViewModel?.StatusMessage ?? LocalizedStrings.Common_ErrorTitle, true);
                }
            }
            catch (Exception ex)
            {
                ShowStatus(ex.Message, true);
            }
        }
    }

    private async void OnHelpClick(object sender, RoutedEventArgs e)
    {
        await Windows.System.Launcher.LaunchUriAsync(
            new Uri("https://learn.microsoft.com/en-us/windows/win32/secauthz/authorization-manager"));
    }

    private async void OnAddApplicationClick(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateItemDialog(CreateItemType.Application)
        {
            XamlRoot = this.XamlRoot,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            RequestedTheme = App.CurrentTheme
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && dialog.Result != null && _viewModel != null)
        {
            var app = await _viewModel.CreateApplicationAsync(dialog.Result.Name, dialog.Result.Description);
            if (app != null)
            {
                ShowStatus(string.Format(LocalizedStrings.AuthorizationStorePage_Status_AppCreated, app.Name), false);
            }
        }
    }

    private void OnApplicationClick(object sender, RoutedEventArgs e)
    {
        if (sender is SettingsCard card && card.Tag is AzApplicationInfo app && _service != null && _store != null)
        {
            var navigationParameter = new ApplicationNavigationParameter(_service, _store.StorePath, app.Name);

            if (this.Frame == null)
            {
                // Frame is not available, cannot navigate
                return;
            }

            BreadcrumbNavigationService.AddBreadcrumb(app.Name, typeof(AuthApplicationsPage), navigationParameter);
            this.Frame.Navigate(
                typeof(AuthApplicationsPage),
                navigationParameter,
                new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight });
        }
    }

    private async void OnDeleteApplicationClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is AzApplicationInfo app && _viewModel != null)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = LocalizedStrings.AuthorizationStorePage_DeleteApplication_Title,
                Content = string.Format(LocalizedStrings.AuthorizationStorePage_DeleteApplication_Content, app.Name),
                PrimaryButtonText = LocalizedStrings.Common_DeleteButton,
                CloseButtonText = LocalizedStrings.Common_CancelButton,
                DefaultButton = ContentDialogButton.Close,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                RequestedTheme = App.CurrentTheme
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                var success = await _viewModel.DeleteApplicationAsync(app.Name);
                if (success)
                {
                    ShowStatus(string.Format(LocalizedStrings.AuthorizationStorePage_Status_AppDeleted, app.Name), false);
                }
            }
        }
    }

    private async void OnAddGroupClick(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateItemDialog(CreateItemType.Group)
        {
            XamlRoot = this.XamlRoot,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            RequestedTheme = App.CurrentTheme
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && dialog.Result != null && _viewModel != null)
        {
            var group = await _viewModel.CreateGroupAsync(
                dialog.Result.Name,
                dialog.Result.GroupType,
                dialog.Result.Description,
                dialog.Result.LdapQuery);

            if (group != null)
            {
                ShowStatus(string.Format(LocalizedStrings.AuthorizationStorePage_Status_GroupCreated, group.Name), false);
            }
        }
    }

    private async void OnEditGroupClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is AzApplicationGroupInfo group && _service != null && _store != null)
        {
            // Show group members management dialog
            var availableStoreGroups = _viewModel?.Groups
                .Where(g => !string.Equals(g.Name, group.Name, StringComparison.OrdinalIgnoreCase))
                .Select(g => g.Name)
                .ToList();

            var dialog = new GroupMembersDialog(group, availableStoreGroups)
            {
                XamlRoot = this.XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                RequestedTheme = App.CurrentTheme
            };
            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary && dialog.Result != null)
            {
                try
                {
                    // Process member changes for store-level groups
                    foreach (var member in dialog.Result.AddedMembers)
                    {
                        await _service.AddGroupMemberAsync(_store.StorePath, group.Name, member);
                    }
                    foreach (var member in dialog.Result.RemovedMembers)
                    {
                        await _service.RemoveGroupMemberAsync(_store.StorePath, group.Name, member);
                    }

                    // Process non-member changes
                    foreach (var nonMember in dialog.Result.AddedNonMembers)
                    {
                        await _service.AddGroupNonMemberAsync(_store.StorePath, group.Name, nonMember);
                    }
                    foreach (var nonMember in dialog.Result.RemovedNonMembers)
                    {
                        await _service.RemoveGroupNonMemberAsync(_store.StorePath, group.Name, nonMember);
                    }

                    // Process application group member link changes
                    foreach (var appMember in dialog.Result.AddedAppMemberLinks)
                    {
                        await _service.AddGroupMemberAsync(_store.StorePath, group.Name, appMember, isAppGroup: true);
                    }
                    foreach (var appMember in dialog.Result.RemovedAppMemberLinks)
                    {
                        await _service.RemoveGroupMemberAsync(_store.StorePath, group.Name, appMember, isAppGroup: true);
                    }

                    // Process application group non-member link changes
                    foreach (var appNonMember in dialog.Result.AddedAppNonMemberLinks)
                    {
                        await _service.AddGroupNonMemberAsync(_store.StorePath, group.Name, appNonMember, isAppGroup: true);
                    }
                    foreach (var appNonMember in dialog.Result.RemovedAppNonMemberLinks)
                    {
                        await _service.RemoveGroupNonMemberAsync(_store.StorePath, group.Name, appNonMember, isAppGroup: true);
                    }

                    if (dialog.Result.BizRuleChanged)
                    {
                        await _service.SetStoreGroupBizRuleAsync(_store.StorePath, group.Name, dialog.Result.BizRule, dialog.Result.BizRuleLanguage);
                    }

                    // Reload
                    if (_viewModel != null)
                    {
                        await _viewModel.LoadAsync(_store.StorePath);
                    }
                    ShowStatus(string.Format(LocalizedStrings.AuthorizationStorePage_Status_GroupUpdated, group.Name), false);
                }
                catch (Exception ex)
                {
                    ShowStatus(string.Format(LocalizedStrings.AuthorizationStorePage_Status_GroupUpdateFailed, ex.Message), true);
                }
            }
        }
    }

    private async void OnDeleteGroupClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is AzApplicationGroupInfo group && _viewModel != null)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = LocalizedStrings.AuthorizationStorePage_DeleteGroup_Title,
                Content = string.Format(LocalizedStrings.AuthorizationStorePage_DeleteGroup_Content, group.Name),
                PrimaryButtonText = LocalizedStrings.Common_DeleteButton,
                CloseButtonText = LocalizedStrings.Common_CancelButton,
                DefaultButton = ContentDialogButton.Close,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                RequestedTheme = App.CurrentTheme
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                var success = await _viewModel.DeleteGroupAsync(group.Name);
                if (success)
                {
                    ShowStatus(string.Format(LocalizedStrings.AuthorizationStorePage_Status_GroupDeleted, group.Name), false);
                }
            }
        }
    }

        #endregion

        #region Private Methods

        private void UpdateStatusBar()
        {
            if (_viewModel == null) return;

            if (_viewModel.HasError)
            {
                StatusInfoBar.Severity = InfoBarSeverity.Error;
                StatusInfoBar.Title = LocalizedStrings.Common_ErrorTitle;
                StatusInfoBar.Message = _viewModel.StatusMessage;
                StatusInfoBar.IsOpen = true;
            }
        }

        private void ShowStatus(string message, bool isError)
        {
            StatusInfoBar.Severity = isError ? InfoBarSeverity.Error : InfoBarSeverity.Success;
            StatusInfoBar.Title = isError ? LocalizedStrings.Common_ErrorTitle : LocalizedStrings.Common_SuccessTitle;
            StatusInfoBar.Message = message;
            StatusInfoBar.IsOpen = true;
        }

        private void UpdateStoreHeader()
        {
            StoreNameText.Text = _store?.Name ?? string.Empty;
            StoreTypeText.Text = _store?.StoreType.ToString() ?? string.Empty;
        }

        #endregion
    }

/// <summary>
/// Application navigation parameter
/// </summary>
public class ApplicationNavigationParameter
{
    public AzManService Service { get; }
    public string StorePath { get; }
    public string ApplicationName { get; }

    public ApplicationNavigationParameter(AzManService service, string storePath, string appName)
    {
        Service = service;
        StorePath = storePath;
        ApplicationName = appName;
    }
}
