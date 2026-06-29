using ManagementTools.Core.Features.PCManagement.Models.TaskSchd;
using ManagementTools.Core.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

namespace ManagementTools.Views.PCManagement;

/// <summary>
/// "New Action" / "Edit Action" editor. Builds an <see cref="ActionModel"/> (Exec / Email / Message)
/// from the selected panel. The deprecated e-mail and message actions are kept for parity with
/// taskschd.msc so existing tasks that use them remain editable.
/// </summary>
public sealed partial class NewActionDialog : ContentDialog
{
    private const int ActionExec = 0;
    private const int ActionEmail = 1;
    private const int ActionMessage = 2;

    /// <summary>The action built when the dialog is committed; <see langword="null"/> if cancelled.</summary>
    public ActionModel? ResultAction { get; private set; }

    /// <summary>Creates the dialog in create mode, or edit mode pre-populated from <paramref name="actionToEdit"/>.</summary>
    public NewActionDialog(ActionModel? actionToEdit = null)
    {
        InitializeComponent();
        Title = L(actionToEdit is null ? TaskSchdKeys.DialogNewAction : TaskSchdKeys.DialogEditAction);
        PrimaryButtonText = L(TaskSchdKeys.ButtonOk);
        CloseButtonText = L(TaskSchdKeys.ButtonCancel);
        Closing += OnClosing;

        if (actionToEdit is null)
        {
            ActionComboBox.SelectedIndex = ActionExec;
        }
        else
        {
            PopulateFrom(actionToEdit);
        }
    }

    private static string L(string key) => LocalizationProvider.Current.GetString(ResourceFileNames.TaskSchd, key);

    private static nint OwnerHwnd => App.MainWindowInstance is null ? 0 : WindowNative.GetWindowHandle(App.MainWindowInstance);

    private void PopulateFrom(ActionModel action)
    {
        switch (action)
        {
            case ExecActionModel exec:
                ActionComboBox.SelectedIndex = ActionExec;
                ProgramScriptBox.Text = exec.Path;
                ArgumentsBox.Text = exec.Arguments ?? string.Empty;
                StartInBox.Text = exec.WorkingDirectory ?? string.Empty;
                break;
            case EmailActionModel email:
                ActionComboBox.SelectedIndex = ActionEmail;
                EmailFromBox.Text = email.From ?? string.Empty;
                EmailToBox.Text = email.To ?? string.Empty;
                EmailSubjectBox.Text = email.Subject ?? string.Empty;
                EmailTextBox.Text = email.Body ?? string.Empty;
                EmailSmtpBox.Text = email.Server ?? string.Empty;
                break;
            case ShowMessageActionModel msg:
                ActionComboBox.SelectedIndex = ActionMessage;
                MessageTitleBox.Text = msg.Title ?? string.Empty;
                MessageBodyBox.Text = msg.MessageBody ?? string.Empty;
                break;
            default:
                ActionComboBox.SelectedIndex = ActionExec;
                break;
        }
    }

    /// <summary>Shows the settings panel that matches the chosen action; the rest stay collapsed.</summary>
    private void ActionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StartProgramPanel is null)
        {
            return;
        }

        StartProgramPanel.Visibility = SendEmailPanel.Visibility = DisplayMessagePanel.Visibility = Visibility.Collapsed;
        switch (ActionComboBox.SelectedIndex)
        {
            case ActionExec: StartProgramPanel.Visibility = Visibility.Visible; break;
            case ActionEmail: SendEmailPanel.Visibility = Visibility.Visible; break;
            case ActionMessage: DisplayMessagePanel.Visibility = Visibility.Visible; break;
        }
    }

    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (args.Result != ContentDialogResult.Primary)
        {
            return;
        }

        switch (ActionComboBox.SelectedIndex)
        {
            case ActionExec:
                if (string.IsNullOrWhiteSpace(ProgramScriptBox.Text))
                {
                    args.Cancel = true;
                    ValidationInfoBar.Message = L(TaskSchdKeys.ValidationProgramRequired);
                    ValidationInfoBar.IsOpen = true;
                    return;
                }
                ResultAction = new ExecActionModel
                {
                    Path = ProgramScriptBox.Text.Trim(),
                    Arguments = NullIfEmpty(ArgumentsBox.Text),
                    WorkingDirectory = NullIfEmpty(StartInBox.Text),
                };
                break;
            case ActionEmail:
                var email = new EmailActionModel
                {
                    From = NullIfEmpty(EmailFromBox.Text),
                    To = NullIfEmpty(EmailToBox.Text),
                    Subject = NullIfEmpty(EmailSubjectBox.Text),
                    Body = NullIfEmpty(EmailTextBox.Text),
                    Server = NullIfEmpty(EmailSmtpBox.Text),
                };
                if (!string.IsNullOrWhiteSpace(EmailAttachmentBox.Text))
                {
                    email.Attachments.Add(EmailAttachmentBox.Text.Trim());
                }
                ResultAction = email;
                break;
            case ActionMessage:
                ResultAction = new ShowMessageActionModel
                {
                    Title = NullIfEmpty(MessageTitleBox.Text),
                    MessageBody = NullIfEmpty(MessageBodyBox.Text),
                };
                break;
        }
    }

    private async void BrowseProgram_Click(object sender, RoutedEventArgs e)
    {
        var path = await App.GetRequiredService<IFileDialogService>()
            .OpenFileAsync(OwnerHwnd, "Programs\0*.exe;*.bat;*.cmd;*.ps1\0All Files\0*.*\0", title: L(TaskSchdKeys.ActionProgramScript));
        if (!string.IsNullOrEmpty(path))
        {
            ProgramScriptBox.Text = path;
        }
    }

    private async void BrowseAttachment_Click(object sender, RoutedEventArgs e)
    {
        var path = await App.GetRequiredService<IFileDialogService>()
            .OpenFileAsync(OwnerHwnd, "All Files\0*.*\0", title: L(TaskSchdKeys.ActionEmailAttachment));
        if (!string.IsNullOrEmpty(path))
        {
            EmailAttachmentBox.Text = path;
        }
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
