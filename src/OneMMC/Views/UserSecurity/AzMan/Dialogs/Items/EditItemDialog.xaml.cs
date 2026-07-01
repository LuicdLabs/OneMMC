// ============================================================================
// EditItemDialog.xaml.cs
// 
// Generic Edit Item Dialog - For editing properties of applications, groups, roles, tasks, operations, etc.
// ============================================================================

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OneMMC.Localization;
using OneMMC.Core.Features.UserSecurity.Models.AzMan;

namespace OneMMC.Views.UserSecurity.AzMan.Dialogs;

/// <summary>
/// Edit item type
/// </summary>
public enum EditItemType
{
    Application,
    Group,
    RoleDefinition,
    RoleAssignment,
    Task,
    Operation,
    Store
}

/// <summary>
/// Edit item data
/// </summary>
public class EditItemData
{
	public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

	
public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public AzGroupType GroupType { get; set; } = AzGroupType.Basic;
    public string LdapQuery { get; set; } = string.Empty;
    public int OperationId { get; set; }
}

/// <summary>
/// Generic Edit Item Dialog
/// </summary>
public sealed partial class EditItemDialog : ContentDialog
{
	
private readonly EditItemType _itemType;
    private readonly EditItemData _originalData;
    private readonly LocalizedStrings _localizedStrings = LocalizedStrings.Instance;

    /// <summary>
    /// Localized strings accessor
    /// </summary>
    public LocalizedStrings LocalizedStrings => _localizedStrings;

    /// <summary>
    /// Edit result
    /// </summary>
    public EditItemData? Result { get; private set; }

    /// <summary>
    /// Create dialog
    /// </summary>
    public EditItemDialog(EditItemType itemType, EditItemData data)
    {
        InitializeComponent();
        this.RequestedTheme = App.CurrentTheme;
        _itemType = itemType;
        _originalData = data;

        ConfigureForItemType();
        LoadData();
    }

    /// <summary>
    /// Configure UI based on item type
    /// </summary>
    private void ConfigureForItemType()
    {
        switch (_itemType)
        {
            case EditItemType.Store:
                Title = _localizedStrings.EditItemDialog_Title_Store;
                break;

            case EditItemType.Application:
                Title = _localizedStrings.EditItemDialog_Title_Application;
                break;

            case EditItemType.Group:
                Title = _localizedStrings.EditItemDialog_Title_Group;
                GroupTypePanel.Visibility = Visibility.Visible;
                break;

            case EditItemType.RoleDefinition:
                Title = _localizedStrings.EditItemDialog_Title_RoleDefinition;
                break;

            case EditItemType.RoleAssignment:
                Title = _localizedStrings.EditItemDialog_Title_RoleAssignment;
                break;

            case EditItemType.Task:
                Title = _localizedStrings.EditItemDialog_Title_Task;
                break;

            case EditItemType.Operation:
                Title = _localizedStrings.EditItemDialog_Title_Operation;
                OperationPanel.Visibility = Visibility.Visible;
                break;
        }
    }

    /// <summary>
    /// Load existing data
    /// </summary>
    private void LoadData()
    {
        NameTextBox.Text = _originalData.Name;
        DescriptionTextBox.Text = _originalData.Description;

        if (_itemType == EditItemType.Group)
        {
            GroupTypeComboBox.SelectedIndex = _originalData.GroupType switch
            {
                AzGroupType.LdapQuery => 1,
                AzGroupType.Bizrule => 2,
                _ => 0 // Basic
            };
            
            if (_originalData.GroupType == AzGroupType.LdapQuery)
            {
                LdapQueryPanel.Visibility = Visibility.Visible;
                LdapQueryTextBox.Text = _originalData.LdapQuery;
            }
        }

        if (_itemType == EditItemType.Operation)
        {
            OperationIdNumberBox.Value = _originalData.OperationId;
        }
    }

    /// <summary>
    /// Primary button click
    /// </summary>
    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Validate LDAP query group
        if (_itemType == EditItemType.Group && _originalData.GroupType == AzGroupType.LdapQuery)
        {
            if (string.IsNullOrWhiteSpace(LdapQueryTextBox.Text))
            {
                ShowError(_localizedStrings.EditItemDialog_Error_LdapQueryRequired);
                args.Cancel = true;
                return;
            }
        }

        Result = new EditItemData
        {
            Name = _originalData.Name, // Name cannot be changed
            Description = DescriptionTextBox.Text.Trim(),
            GroupType = _originalData.GroupType, // Group type cannot be changed
            LdapQuery = LdapQueryTextBox.Text.Trim(),
            OperationId = _itemType == EditItemType.Operation && !double.IsNaN(OperationIdNumberBox.Value)
                ? (int)OperationIdNumberBox.Value
                : _originalData.OperationId
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
