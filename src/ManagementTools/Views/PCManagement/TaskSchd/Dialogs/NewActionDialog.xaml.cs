using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ManagementTools.Views.PCManagement;

/// <summary>
/// UI prototype for the "New Action" / "Edit Action" editor of a modern Task Scheduler
/// (taskschd.msc) replacement, launched from <see cref="TaskPropertiesPage"/>'s Actions
/// &gt; "New" command (create mode) and from each action row's Edit button (edit mode).
/// </summary>
/// <remarks>
/// Everything here is MOCK / SAMPLE behaviour: the code only swaps which Settings panel is visible
/// for the chosen action type and performs a trivial validation. No action is ever created or saved.
/// TODO(TaskScheduler): build the matching <c>IAction</c> from the selection (see the IAction map in
/// the XAML header) and add it to / update it in the task's <c>IActionCollection</c> via the Task
/// Scheduler 2.0 COM API, and migrate all strings to ResourceKeys / .resw.
/// </remarks>
public sealed partial class NewActionDialog : ContentDialog
{
	// "Action" indices. Must match the ComboBoxItem order in NewActionDialog.xaml.
	private const int ActionStartProgram = 0;
	private const int ActionSendEmail = 1;
	private const int ActionDisplayMessage = 2;

	/// <summary>
	/// Creates the dialog in <b>create</b> mode (Title "Create a New Action") when
	/// <paramref name="actionToEdit"/> is <c>null</c>, or <b>edit</b> mode (Title "Edit Action")
	/// when an existing row is supplied.
	/// </summary>
	/// <param name="actionToEdit">
	/// The action row being edited, or <c>null</c> to create a new action. In edit mode only the
	/// action <i>type</i> is pre-selected from the row's <see cref="TaskActionItem.Header"/>; the
	/// per-panel fields stay empty because this prototype has no real action definition to read.
	/// </param>
	public NewActionDialog(TaskActionItem? actionToEdit = null)
	{
		this.InitializeComponent();
		this.Closing += NewActionDialog_Closing;

		if (actionToEdit is null)
		{
			// Create mode: default to "Start a program" (taskschd's default new-action type).
			// The XAML Title ("Create a New Action") already reflects this mode.
			ActionComboBox.SelectedIndex = ActionStartProgram;
		}
		else
		{
			// Edit mode: retitle and pre-select the action type of the row being edited.
			this.Title = "Edit Action";
			ActionComboBox.SelectedIndex = IndexForHeader(actionToEdit.Header);

			// TODO(TaskScheduler): when this binds to a real task, populate the matching panel's
			// fields from the IAction (IExecAction.Path/Arguments/WorkingDirectory, IEmailAction's
			// From/To/Subject/Body/Server, or IShowMessageAction.Title/MessageBody). The current
			// TaskActionItem is sample data with no structured fields to restore, so only the type
			// is seeded here.
		}
	}

	/// <summary>
	/// Maps a <see cref="TaskActionItem.Header"/> (the action type label) to its combo index. A real
	/// editor would switch on the IAction's <c>Type</c> (TASK_ACTION_EXEC / _EMAIL / _SHOW_MESSAGE)
	/// rather than the display string; the string match suffices for the sample rows.
	/// </summary>
	private static int IndexForHeader(string header) => header switch
	{
		"Send an e-mail (deprecated)" => ActionSendEmail,
		"Display a message (deprecated)" => ActionDisplayMessage,
		_ => ActionStartProgram,
	};

	/// <summary>Shows the Settings panel that matches the chosen action; the rest stay collapsed.</summary>
	private void ActionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		// Guard against the event firing before the named panels have been created.
		if (StartProgramPanel is null)
		{
			return;
		}

		StartProgramPanel.Visibility = SendEmailPanel.Visibility =
			DisplayMessagePanel.Visibility = Visibility.Collapsed;

		switch (ActionComboBox.SelectedIndex)
		{
			case ActionStartProgram: StartProgramPanel.Visibility = Visibility.Visible; break;
			case ActionSendEmail: SendEmailPanel.Visibility = Visibility.Visible; break;
			case ActionDisplayMessage: DisplayMessagePanel.Visibility = Visibility.Visible; break;
		}
	}

	/// <summary>
	/// Validates the minimal required input before the dialog closes with OK. taskschd requires a
	/// Program/script for "Start a program"; the deprecated actions have no hard-required field here.
	/// </summary>
	private void NewActionDialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
	{
		if (args.Result != ContentDialogResult.Primary)
		{
			return;
		}

		if (ActionComboBox.SelectedIndex == ActionStartProgram && string.IsNullOrWhiteSpace(ProgramScriptBox.Text))
		{
			args.Cancel = true;
			ValidationInfoBar.Message = "Enter the program or script to run.";
			ValidationInfoBar.IsOpen = true;
		}
	}

	// TODO(TaskScheduler): replace with a real file picker (FileOpenPicker initialized with the
	// owning window's HWND) that sets ProgramScriptBox.Text. No-op in this UI prototype.
	private void BrowseProgram_Click(object sender, RoutedEventArgs e)
	{
	}

	// TODO(TaskScheduler): replace with a real file picker that sets EmailAttachmentBox.Text.
	// No-op in this UI prototype.
	private void BrowseAttachment_Click(object sender, RoutedEventArgs e)
	{
	}
}
