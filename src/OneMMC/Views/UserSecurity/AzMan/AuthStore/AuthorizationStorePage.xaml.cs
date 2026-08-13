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
    private int _navigationGeneration;

    public AuthorizationStorePage()
    {
        InitializeComponent();
        this.RequestedTheme = App.CurrentTheme;
        App.ThemeChanged += OnThemeChanged;
        this.Unloaded += (_, _) =>
        {
            ++_navigationGeneration;
            App.ThemeChanged -= OnThemeChanged;
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                _viewModel = null;
            }
            _service = null;
            _store = null;
        };
    }

    #region Navigation

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is StoreNavigationParameter param)
        {
            int generation = ++_navigationGeneration;
            // AzManService is a DI singleton; the journal carries only the stable store path.
            _service = App.GetRequiredService<AzManService>();
            var viewModel = new AuthorizationStoreViewModel(_service);
            _viewModel = viewModel;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;

            await viewModel.LoadAsync(param.StorePath);
            if (!IsCurrentViewModel(generation, viewModel))
            {
                return;
            }

            if (viewModel.Store is not null)
            {
                _store = viewModel.Store;
                UpdateStoreHeader();
            }
            // UI will update automatically
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ++_navigationGeneration;
        base.OnNavigatedFrom(e);
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        // Clear the current identity immediately, rather than waiting for Unloaded. A PropertyChanged
        // invocation may already have copied the delegate before the unsubscribe above; nulling the
        // field ensures its queued DispatcherQueue callback cannot pass IsCurrentViewModel.
        _viewModel = null;
        _service = null;
        _store = null;
    }

    #endregion

    #region Event Handlers

    private void OnThemeChanged(ElementTheme theme)
    {
        this.RequestedTheme = theme;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not AuthorizationStoreViewModel viewModel)
        {
            return;
        }

        int generation = _navigationGeneration;
        // UI will update automatically through x:Bind Mode=OneWay
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!IsCurrentViewModel(generation, viewModel))
            {
                return;
            }

            switch (e.PropertyName)
            {
                case nameof(AuthorizationStoreViewModel.IsLoading):
                    LoadingRing.IsActive = viewModel.IsLoading;
                    break;

                case nameof(AuthorizationStoreViewModel.HasError):
                case nameof(AuthorizationStoreViewModel.StatusMessage):
                    UpdateStatusBar();
                    break;

                case nameof(AuthorizationStoreViewModel.Store):
                    if (viewModel.Store is not null)
                    {
                        _store = viewModel.Store;
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
        AuthorizationStoreViewModel? viewModel = _viewModel;
        AzAuthorizationStoreInfo? store = _store;
        int generation = _navigationGeneration;
        if (viewModel is not null && store is not null)
        {
            await viewModel.LoadAsync(store.StorePath);
            if (!IsCurrentViewModel(generation, viewModel))
            {
                return;
            }

            if (viewModel.Store is not null)
            {
                _store = viewModel.Store;
                UpdateStoreHeader();
            }
            // UI will update automatically
        }
    }

    private async void OnPropertiesClick(object sender, RoutedEventArgs e)
    {
        AzAuthorizationStoreInfo? store = _store;
        AuthorizationStoreViewModel? viewModel = _viewModel;
        AzManService? service = _service;
        int generation = _navigationGeneration;
        if (store is null) return;

        if (viewModel is not null)
        {
            try
            {
                await viewModel.LoadAsync(store.StorePath);
                if (!IsCurrentViewModel(generation, viewModel))
                {
                    return;
                }

                if (viewModel.Store is not null)
                {
                    store = viewModel.Store;
                    _store = store;
                    UpdateStoreHeader();
                }
            }
            catch
            {
                // Keep properties dialog usable even when pre-refresh fails.
            }
        }

        if (!IsCurrentGeneration(generation))
        {
            return;
        }

        StoreAdvancedProperties? advancedProperties = null;
        if (service is not null)
        {
            try
            {
                advancedProperties = await service.GetStoreAdvancedPropertiesAsync(store.StorePath);
                if (!IsCurrentGeneration(generation))
                {
                    return;
                }
            }
            catch
            {
                // Keep dialog usable even when advanced properties cannot be read.
            }
        }

        if (!IsCurrentGeneration(generation))
        {
            return;
        }

        var dialog = new StorePropertiesDialog(store, advancedProperties)
        {
            XamlRoot = this.XamlRoot,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            RequestedTheme = App.CurrentTheme
        };

        while (true)
        {
            var result = await dialog.ShowAsync();
            if (!IsCurrentGeneration(generation))
            {
                return;
            }

            if (dialog.ReopenRequested)
            {
                await dialog.WaitForReopenAsync();
                if (!IsCurrentGeneration(generation))
                {
                    return;
                }
                continue;
            }

            if (result == ContentDialogResult.Primary && dialog.Result != null && service is not null)
            {
                try
                {
                    // Update store properties
                    await service.UpdateStorePropertiesAsync(
                        store.StorePath,
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

                    await service.UpdateStoreAdvancedPropertiesAsync(store.StorePath, advancedUpdate);

                    if (dialog.Result.UpgradeSchemaToV2)
                    {
                        await service.UpgradeStoreSchemaToV2Async(store.StorePath);
                    }

                    // Update Policy Administrators
                    foreach (var admin in dialog.Result.AddedPolicyAdmins)
                    {
                        await service.AddPolicyAdministratorAsync(store.StorePath, admin);
                    }
                    foreach (var admin in dialog.Result.RemovedPolicyAdmins)
                    {
                        await service.RemovePolicyAdministratorAsync(store.StorePath, admin);
                    }

                    // Update Policy Readers
                    foreach (var reader in dialog.Result.AddedPolicyReaders)
                    {
                        await service.AddPolicyReaderAsync(store.StorePath, reader);
                    }
                    foreach (var reader in dialog.Result.RemovedPolicyReaders)
                    {
                        await service.RemovePolicyReaderAsync(store.StorePath, reader);
                    }

                    // Reload
                    if (viewModel is not null)
                    {
                        await viewModel.LoadAsync(store.StorePath);
                        if (!IsCurrentViewModel(generation, viewModel))
                        {
                            return;
                        }

                        if (viewModel.Store is not null)
                        {
                            store = viewModel.Store;
                            _store = store;
                            UpdateStoreHeader();
                        }
                    }
                    if (IsCurrentGeneration(generation))
                    {
                        ShowStatus(LocalizedStrings.AuthorizationStorePage_Status_PropertiesUpdated, false);
                    }
                }
                catch (Exception ex)
                {
                    if (IsCurrentGeneration(generation))
                    {
                        ShowStatus(string.Format(LocalizedStrings.AuthorizationStorePage_Status_PropertiesUpdateFailed, ex.Message), true);
                    }
                }
            }

            break;
        }
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        AzAuthorizationStoreInfo? store = _store;
        AzManService? service = _service;
        int generation = _navigationGeneration;
        if (store is null || service is null) return;

        var dialog = new ContentDialog
        {
            XamlRoot = this.XamlRoot,
            Title = LocalizedStrings.AuthorizationStorePage_DeleteStore_Title,
            Content = string.Format(LocalizedStrings.AuthorizationStorePage_DeleteStore_Content, store.Name),
            PrimaryButtonText = LocalizedStrings.Common_DeleteButton,
            CloseButtonText = LocalizedStrings.Common_CancelButton,
            DefaultButton = ContentDialogButton.Close,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            RequestedTheme = App.CurrentTheme
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && IsCurrentGeneration(generation))
        {
            try
            {
                await service.DeleteStoreAsync(store.StorePath);
                if (!IsCurrentGeneration(generation))
                {
                    return;
                }

                if (this.Frame.CanGoBack)
                {
                    this.Frame.GoBack();
                }
            }
            catch (Exception ex)
            {
                if (IsCurrentGeneration(generation))
                {
                    ShowStatus(ex.Message, true);
                }
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
        AuthorizationStoreViewModel? viewModel = _viewModel;
        int generation = _navigationGeneration;
        if (viewModel is null) return;

        var dialog = new CreateItemDialog(CreateItemType.Application)
        {
            XamlRoot = this.XamlRoot,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            RequestedTheme = App.CurrentTheme
        };

        var result = await dialog.ShowAsync();
        var createResult = dialog.Result;
        if (result == ContentDialogResult.Primary &&
            createResult is not null &&
            IsCurrentViewModel(generation, viewModel))
        {
            var app = await viewModel.CreateApplicationAsync(createResult.Name, createResult.Description);
            if (app is not null && IsCurrentViewModel(generation, viewModel))
            {
                ShowStatus(string.Format(LocalizedStrings.AuthorizationStorePage_Status_AppCreated, app.Name), false);
            }
        }
    }

    private void OnApplicationClick(object sender, RoutedEventArgs e)
    {
        if (sender is SettingsCard card && card.Tag is AzApplicationInfo app && _service != null && _store != null)
        {
            var navigationParameter = new ApplicationNavigationParameter(_store.StorePath, app.Name);

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
        AuthorizationStoreViewModel? viewModel = _viewModel;
        int generation = _navigationGeneration;
        if (sender is Button btn && btn.Tag is AzApplicationInfo app && viewModel is not null)
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
            if (result == ContentDialogResult.Primary && IsCurrentViewModel(generation, viewModel))
            {
                var success = await viewModel.DeleteApplicationAsync(app.Name);
                if (success && IsCurrentViewModel(generation, viewModel))
                {
                    ShowStatus(string.Format(LocalizedStrings.AuthorizationStorePage_Status_AppDeleted, app.Name), false);
                }
            }
        }
    }

    private async void OnAddGroupClick(object sender, RoutedEventArgs e)
    {
        AuthorizationStoreViewModel? viewModel = _viewModel;
        int generation = _navigationGeneration;
        if (viewModel is null) return;

        var dialog = new CreateItemDialog(CreateItemType.Group)
        {
            XamlRoot = this.XamlRoot,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            RequestedTheme = App.CurrentTheme
        };

        var result = await dialog.ShowAsync();
        var createResult = dialog.Result;
        if (result == ContentDialogResult.Primary &&
            createResult is not null &&
            IsCurrentViewModel(generation, viewModel))
        {
            var group = await viewModel.CreateGroupAsync(
                createResult.Name,
                createResult.GroupType,
                createResult.Description,
                createResult.LdapQuery);

            if (group is not null && IsCurrentViewModel(generation, viewModel))
            {
                ShowStatus(string.Format(LocalizedStrings.AuthorizationStorePage_Status_GroupCreated, group.Name), false);
            }
        }
    }

    private async void OnEditGroupClick(object sender, RoutedEventArgs e)
    {
        AzManService? service = _service;
        AzAuthorizationStoreInfo? store = _store;
        AuthorizationStoreViewModel? viewModel = _viewModel;
        int generation = _navigationGeneration;
        if (sender is Button btn &&
            btn.Tag is AzApplicationGroupInfo group &&
            service is not null &&
            store is not null)
        {
            // Show group members management dialog
            var availableStoreGroups = viewModel?.Groups
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
            var changes = dialog.Result;

            if (result == ContentDialogResult.Primary &&
                changes is not null &&
                IsCurrentGeneration(generation))
            {
                try
                {
                    // Process member changes for store-level groups
                    foreach (var member in changes.AddedMembers)
                    {
                        await service.AddGroupMemberAsync(store.StorePath, group.Name, member);
                    }
                    foreach (var member in changes.RemovedMembers)
                    {
                        await service.RemoveGroupMemberAsync(store.StorePath, group.Name, member);
                    }

                    // Process non-member changes
                    foreach (var nonMember in changes.AddedNonMembers)
                    {
                        await service.AddGroupNonMemberAsync(store.StorePath, group.Name, nonMember);
                    }
                    foreach (var nonMember in changes.RemovedNonMembers)
                    {
                        await service.RemoveGroupNonMemberAsync(store.StorePath, group.Name, nonMember);
                    }

                    // Process application group member link changes
                    foreach (var appMember in changes.AddedAppMemberLinks)
                    {
                        await service.AddGroupMemberAsync(store.StorePath, group.Name, appMember, isAppGroup: true);
                    }
                    foreach (var appMember in changes.RemovedAppMemberLinks)
                    {
                        await service.RemoveGroupMemberAsync(store.StorePath, group.Name, appMember, isAppGroup: true);
                    }

                    // Process application group non-member link changes
                    foreach (var appNonMember in changes.AddedAppNonMemberLinks)
                    {
                        await service.AddGroupNonMemberAsync(store.StorePath, group.Name, appNonMember, isAppGroup: true);
                    }
                    foreach (var appNonMember in changes.RemovedAppNonMemberLinks)
                    {
                        await service.RemoveGroupNonMemberAsync(store.StorePath, group.Name, appNonMember, isAppGroup: true);
                    }

                    if (changes.BizRuleChanged)
                    {
                        await service.SetStoreGroupBizRuleAsync(store.StorePath, group.Name, changes.BizRule, changes.BizRuleLanguage);
                    }

                    // Reload
                    if (viewModel is not null && IsCurrentViewModel(generation, viewModel))
                    {
                        await viewModel.LoadAsync(store.StorePath);
                        if (!IsCurrentViewModel(generation, viewModel))
                        {
                            return;
                        }
                    }

                    if (IsCurrentGeneration(generation))
                    {
                        ShowStatus(string.Format(LocalizedStrings.AuthorizationStorePage_Status_GroupUpdated, group.Name), false);
                    }
                }
                catch (Exception ex)
                {
                    if (IsCurrentGeneration(generation))
                    {
                        ShowStatus(string.Format(LocalizedStrings.AuthorizationStorePage_Status_GroupUpdateFailed, ex.Message), true);
                    }
                }
            }
        }
    }

    private async void OnDeleteGroupClick(object sender, RoutedEventArgs e)
    {
        AuthorizationStoreViewModel? viewModel = _viewModel;
        int generation = _navigationGeneration;
        if (sender is Button btn && btn.Tag is AzApplicationGroupInfo group && viewModel is not null)
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
            if (result == ContentDialogResult.Primary && IsCurrentViewModel(generation, viewModel))
            {
                var success = await viewModel.DeleteGroupAsync(group.Name);
                if (success && IsCurrentViewModel(generation, viewModel))
                {
                    ShowStatus(string.Format(LocalizedStrings.AuthorizationStorePage_Status_GroupDeleted, group.Name), false);
                }
            }
        }
    }

        #endregion

    #region Private Methods

        private bool IsCurrentViewModel(int generation, AuthorizationStoreViewModel viewModel)
            => generation == _navigationGeneration && ReferenceEquals(_viewModel, viewModel);

        private bool IsCurrentGeneration(int generation)
            => generation == _navigationGeneration;

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
/// Application navigation parameter. Identifiers only — see <see cref="StoreNavigationParameter"/> for
/// why navigation parameters must not carry live services or view models.
/// </summary>
public class ApplicationNavigationParameter
{
    public string StorePath { get; }
    public string ApplicationName { get; }

    public ApplicationNavigationParameter(string storePath, string appName)
    {
        StorePath = storePath;
        ApplicationName = appName;
    }
}
