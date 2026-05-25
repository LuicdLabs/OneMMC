using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using ManagementTools.Localization;

namespace ManagementTools.Views.LusrMgr;

/// <summary>
/// Dialog for adding a new local user.
/// </summary>
public sealed partial class AddUserDialog : ContentDialog
{
	public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    private bool _isInitialized = false;

    public string UserName => UserNameBox.Text;
    public string FullName => FullNameBox.Text;
    public string Description => DescriptionBox.Text;
    public string Password => PasswordBox.Password;
    public string ConfirmPassword => ConfirmPasswordBox.Password;
    public bool UserMustChangePassword => UserMustChangePasswordCheck.IsChecked ?? false;
    public bool UserCannotChangePassword => UserCannotChangePasswordCheck.IsChecked ?? false;
    public bool PasswordNeverExpires => PasswordNeverExpiresCheck.IsChecked ?? false;
    public bool AccountDisabled => AccountDisabledCheck.IsChecked ?? false;

    public AddUserDialog()
    {
        this.InitializeComponent();
        this.Closing += AddUserDialog_Closing;
        _isInitialized = true;
    }

    private void AddUserDialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (args.Result == ContentDialogResult.Primary)
        {
            if (string.IsNullOrWhiteSpace(UserName))
            {
                args.Cancel = true;
                return;
            }
            if (Password != ConfirmPassword)
            {
                args.Cancel = true;
                return;
            }
        }
    }

    private void OnPasswordOptionChanged(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;

        if (UserMustChangePasswordCheck.IsChecked == true)
        {
            UserCannotChangePasswordCheck.IsChecked = false;
            PasswordNeverExpiresCheck.IsChecked = false;
        }

        if (UserCannotChangePasswordCheck.IsChecked == true)
        {
            UserMustChangePasswordCheck.IsChecked = false;
        }

        if (PasswordNeverExpiresCheck.IsChecked == true)
        {
            UserMustChangePasswordCheck.IsChecked = false;
        }

        bool disableMustChange = UserCannotChangePasswordCheck.IsChecked == true ||
                                 PasswordNeverExpiresCheck.IsChecked == true;

        UserMustChangePasswordCheck.IsEnabled = !disableMustChange;
        bool disableCannotOrNever = UserMustChangePasswordCheck.IsChecked == true;

        UserCannotChangePasswordCheck.IsEnabled = !disableCannotOrNever;
        PasswordNeverExpiresCheck.IsEnabled = !disableCannotOrNever;

        var conflict = (UserMustChangePasswordCheck.IsChecked == true && PasswordNeverExpiresCheck.IsChecked == true) ||
                       (UserMustChangePasswordCheck.IsChecked == true && UserCannotChangePasswordCheck.IsChecked == true);

        PasswordConflictInfo.IsOpen = conflict;
    }
}
