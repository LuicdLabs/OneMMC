// ============================================================================
// CreateItemDialog.xaml.cs
// 
// Generic Create Item Dialog - For creating applications, groups, roles, tasks, operations, etc.
// ============================================================================

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OneMMC.Localization;
using OneMMC.Core.Features.UserSecurity.Models.AzMan;

namespace OneMMC.Views.UserSecurity.AzMan.Dialogs;

/// <summary>
/// Item type
/// </summary>
public enum CreateItemType
{
    Application,
    Group,
    RoleDefinition,
    RoleAssignment,
    Task,
    Operation
}

/// <summary>
/// Create item result
/// </summary>
public class CreateItemResult
{
	public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

	
public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public AzGroupType GroupType { get; set; } = AzGroupType.Basic;
    public string LdapQuery { get; set; } = string.Empty;
    public int OperationId { get; set; } = -1; // -1 means auto-assign
}

/// <summary>
/// Generic Create Item Dialog
/// </summary>
public sealed partial class CreateItemDialog : ContentDialog
{
	
private readonly CreateItemType _itemType;
    private readonly LocalizedStrings _localizedStrings = LocalizedStrings.Instance;

    /// <summary>
    public LocalizedStrings LocalizedStrings => _localizedStrings;
    /// Create result
    /// </summary>
    public CreateItemResult? Result { get; private set; }

    /// <summary>
    /// Create dialog
    /// </summary>
    /// <param name="itemType">Item type</param>
    public CreateItemDialog(CreateItemType itemType)
    {
        InitializeComponent();
        this.RequestedTheme = App.CurrentTheme;
        _itemType = itemType;

        // Configure title and UI based on item type
        ConfigureForItemType();
    }

    /// <summary>
    /// Configure UI based on item type
    /// </summary>
    private void ConfigureForItemType()
    {
        switch (_itemType)
        {
            case CreateItemType.Application:
                Title = _localizedStrings.CreateItemDialog_Title_Application;
                NameTextBox.PlaceholderText = _localizedStrings.CreateItemDialog_Name_Placeholder_Application;
                break;

            case CreateItemType.Group:
                Title = _localizedStrings.CreateItemDialog_Title_Group;
                NameTextBox.PlaceholderText = _localizedStrings.CreateItemDialog_Name_Placeholder_Group;
                GroupTypePanel.Visibility = Visibility.Visible;
                GroupTypeComboBox.SelectionChanged += OnGroupTypeChanged;
                break;

            case CreateItemType.RoleDefinition:
                Title = _localizedStrings.CreateItemDialog_Title_RoleDefinition;
                NameTextBox.PlaceholderText = _localizedStrings.CreateItemDialog_Name_Placeholder_RoleDefinition;
                break;

            case CreateItemType.RoleAssignment:
                Title = _localizedStrings.CreateItemDialog_Title_RoleAssignment;
                NameTextBox.PlaceholderText = _localizedStrings.CreateItemDialog_Name_Placeholder_RoleAssignment;
                break;

            case CreateItemType.Task:
                Title = _localizedStrings.CreateItemDialog_Title_Task;
                NameTextBox.PlaceholderText = _localizedStrings.CreateItemDialog_Name_Placeholder_Task;
                break;

            case CreateItemType.Operation:
                Title = _localizedStrings.CreateItemDialog_Title_Operation;
                NameTextBox.PlaceholderText = _localizedStrings.CreateItemDialog_Name_Placeholder_Operation;
                OperationIdPanel.Visibility = Visibility.Visible;
                break;
        }
    }

    /// <summary>
    /// Group type changed
    /// </summary>
    private void OnGroupTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GroupTypeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            LdapQueryPanel.Visibility = tag == "LdapQuery" ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Primary button click
    /// </summary>
    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        string name = NameTextBox.Text.Trim();

        // Validate name
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowError(_localizedStrings.CreateItemDialog_Error_EnterName);
            args.Cancel = true;
            return;
        }

        // Validate name format (cannot contain special characters)
        if (name.Contains('/') || name.Contains('\\') || name.Contains(':'))
        {
            ShowError(_localizedStrings.CreateItemDialog_Error_InvalidNameCharacters);
            args.Cancel = true;
            return;
        }

        // Get group type
        AzGroupType groupType = AzGroupType.Basic;
        string ldapQuery = string.Empty;

        if (_itemType == CreateItemType.Group)
        {
            if (GroupTypeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                groupType = tag == "LdapQuery" ? AzGroupType.LdapQuery : AzGroupType.Basic;
            }

            if (groupType == AzGroupType.LdapQuery)
            {
                ldapQuery = LdapQueryTextBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(ldapQuery))
                {
                    ShowError(_localizedStrings.CreateItemDialog_Error_LdapQueryRequired);
                    args.Cancel = true;
                    return;
                }
            }
        }

        // Get OperationId for operations
        int operationId = -1;
        if (_itemType == CreateItemType.Operation)
        {
            // Read from NumberBox - NaN means no value
            if (!double.IsNaN(OperationIdNumberBox.Value))
            {
                operationId = (int)OperationIdNumberBox.Value;
            }
        }

        Result = new CreateItemResult
        {
            Name = name,
            Description = DescriptionTextBox.Text.Trim(),
            GroupType = groupType,
            LdapQuery = ldapQuery,
            OperationId = operationId
        };

        ErrorInfoBar.IsOpen = false;
    }

    /// <summary>
    /// Show error message
    /// </summary>
    private void ShowError(string message)
    {
        ErrorInfoBar.Message = message;
        ErrorInfoBar.IsOpen = true;
    }
}
