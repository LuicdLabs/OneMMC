using OneMMC.Core.Features.PCManagement.Models.LusrMgr;
using OneMMC.Core.Features.PCManagement.ViewModels.LusrMgr;
using OneMMC.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;

namespace OneMMC.Views.LusrMgr;

/// <summary>
/// Page for managing local users and groups.
/// </summary>
public sealed partial class LocalUsersGroupsPage : Page
{
    public OneMMC.Localization.LocalizedStrings LocalizedStrings { get; } = OneMMC.Localization.LocalizedStrings.Instance;
    public LocalUsersGroupsViewModel ViewModel { get; }

    public LocalUsersGroupsPage()
    {
        ViewModel = App.GetRequiredService<LocalUsersGroupsViewModel>();
        InitializeComponent();
        this.DataContext = ViewModel;

        ViewModel.AddUserCommand = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(async () =>
        {
            // Pre-flight admin check
            if (!App.GetRequiredService<IAdminService>().IsRunningAsAdmin)
            {
                await AdminDialogHelper.ShowAdminRequiredDialogAsync(this.Content.XamlRoot);
                return;
            }

            var dialog = new AddUserDialog();
            dialog.RequestedTheme = App.CurrentTheme;
            dialog.XamlRoot = this.Content.XamlRoot;
            var result = await dialog.ShowAsync().AsTask();

            if (result == ContentDialogResult.Primary)
                {
                try
                {
                    ViewModel.CreateUser(dialog.UserName, dialog.Password, dialog.FullName, dialog.Description, 
                        dialog.UserCannotChangePassword, dialog.PasswordNeverExpires, dialog.AccountDisabled, dialog.UserMustChangePassword);
                    }
                    catch (Exception ex)
                    {
                        var adminService = App.GetRequiredService<IAdminService>();
                        if (adminService.IsPermissionError(ex))
                        {
                            await AdminDialogHelper.ShowAdminRequiredDialogAsync(this.Content.XamlRoot);
                        }
                        else
                        {
                            var err = new ContentDialog
                            {
                                Title = LocalizedStrings.Common_ErrorTitle,
                                Content = ex.Message,
                                CloseButtonText = LocalizedStrings.Common_OKButton,
                                XamlRoot = this.Content.XamlRoot,
                                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                                RequestedTheme = App.CurrentTheme
                            };
                            await err.ShowAsync();
                        }
                    }
                }
        });

        ViewModel.AddGroupCommand = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(async () =>
        {
            // Pre-flight admin check
            if (!App.GetRequiredService<IAdminService>().IsRunningAsAdmin)
            {
                await AdminDialogHelper.ShowAdminRequiredDialogAsync(this.Content.XamlRoot);
                return;
            }

            var dialog = new AddGroupDialog();
            dialog.RequestedTheme = App.CurrentTheme;
            dialog.XamlRoot = this.Content.XamlRoot;
            var result = await dialog.ShowAsync().AsTask();
            if (result == ContentDialogResult.Primary)
            {
                ViewModel.CreateGroup(dialog.GroupName, dialog.Description, dialog.Members);
            }
        });

        ViewModel.EditUserCommand = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand<LocalUser>(async (user) =>
        {
            if (user == null) return;

            // Pre-flight admin check
            if (!App.GetRequiredService<IAdminService>().IsRunningAsAdmin)
            {
                await AdminDialogHelper.ShowAdminRequiredDialogAsync(this.Content.XamlRoot);
                return;
            }

            var dialog = new UserPropertiesDialog(user);
            dialog.RequestedTheme = App.CurrentTheme;
            dialog.XamlRoot = this.Content.XamlRoot;
            var result = await dialog.ShowAsync().AsTask();
            if (result == ContentDialogResult.Primary)
            {
                try
                {
                    await ViewModel.EditUserAsync(user.Name, dialog.FullName, dialog.Description, dialog.UserCannotChangePassword, dialog.PasswordNeverExpires, dialog.AccountDisabled, dialog.UserMustChangePassword, null);
                }
                catch (Exception ex)
                {
                    var adminService = App.GetRequiredService<IAdminService>();
                    if (adminService.IsPermissionError(ex))
                    {
                        await AdminDialogHelper.ShowAdminRequiredDialogAsync(this.Content.XamlRoot);
                    }
                    else
                    {
                        var err = new ContentDialog
                        {
                            Title = LocalizedStrings.Common_ErrorTitle,
                            Content = ex.Message,
                            CloseButtonText = LocalizedStrings.Common_OKButton,
                            XamlRoot = this.Content.XamlRoot,
                            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                            RequestedTheme = App.CurrentTheme
                        };
                        await err.ShowAsync();
                    }
                }
            }
        });

        ViewModel.EditGroupCommand = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand<LocalGroup>(async (group) =>
        {
            if (group == null) return;

            // Pre-flight admin check
            if (!App.GetRequiredService<IAdminService>().IsRunningAsAdmin)
            {
                await AdminDialogHelper.ShowAdminRequiredDialogAsync(this.Content.XamlRoot);
                return;
            }

            var dialog = new GroupPropertiesDialog(group);
            dialog.RequestedTheme = App.CurrentTheme;
            dialog.XamlRoot = this.Content.XamlRoot;
            var result = await dialog.ShowAsync().AsTask();
            if (result == ContentDialogResult.Primary)
            {
                await ViewModel.EditGroupAsync(group.Name, dialog.Description, null);
            }
        });

        ViewModel.DeleteUserCommand = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand<LocalUser>(async (user) =>
        {
            if (user == null) return;

            // Pre-flight admin check
            if (!App.GetRequiredService<IAdminService>().IsRunningAsAdmin)
            {
                await AdminDialogHelper.ShowAdminRequiredDialogAsync(this.Content.XamlRoot);
                return;
            }

            var dialog = new ContentDialog
            {
                Title = LocalizedStrings.LusrMgr_DeleteUser_Title,
                Content = string.Format(LocalizedStrings.LusrMgr_DeleteUser_ConfirmFormat, user.Name),
                PrimaryButtonText = LocalizedStrings.Common_DeleteButton,
                CloseButtonText = LocalizedStrings.Common_CancelButton,
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.Content.XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                RequestedTheme = App.CurrentTheme
            };
            var result = await dialog.ShowAsync().AsTask();
            if (result == ContentDialogResult.Primary)
            {
                ViewModel.DeleteUser(user.Name);
            }
        });

        ViewModel.DeleteGroupCommand = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand<LocalGroup>(async (group) =>
        {
            if (group == null) return;

            // Pre-flight admin check
            if (!App.GetRequiredService<IAdminService>().IsRunningAsAdmin)
            {
                await AdminDialogHelper.ShowAdminRequiredDialogAsync(this.Content.XamlRoot);
                return;
            }

            var dialog = new ContentDialog
            {
                Title = LocalizedStrings.LusrMgr_DeleteGroup_Title,
                Content = string.Format(LocalizedStrings.LusrMgr_DeleteGroup_ConfirmFormat, group.Name),
                PrimaryButtonText = LocalizedStrings.Common_DeleteButton,
                CloseButtonText = LocalizedStrings.Common_CancelButton,
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.Content.XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                RequestedTheme = App.CurrentTheme
            };
            var result = await dialog.ShowAsync().AsTask();
            if (result == ContentDialogResult.Primary)
            {
                ViewModel.DeleteGroup(group.Name);
            }
        });
        this.Loaded += LocalUsersGroupsPage_Loaded;
        this.Unloaded += (_, _) =>
        {
            ViewModel.ClearCachedData();
            DataContext = null;
            this.Loaded -= LocalUsersGroupsPage_Loaded;
        };
    }

    private void LocalUsersGroupsPage_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (ViewModel.Users.Count == 0 && ViewModel.Groups.Count == 0)
        {
            ViewModel.Reload();
        }
    }

    private void EditUserButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is LocalUser user)
        {
            ViewModel.EditUserCommand?.Execute(user);
        }
    }

    private void DeleteUserButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is LocalUser user)
        {
            ViewModel.DeleteUserCommand?.Execute(user);
        }
    }

    private void EditGroupButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is LocalGroup group)
        {
            ViewModel.EditGroupCommand?.Execute(group);
        }
    }

    private void DeleteGroupButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is LocalGroup group)
        {
            ViewModel.DeleteGroupCommand?.Execute(group);
        }
    }
}

