namespace ManagementTools.Views.UserSecurity.AzMan.Dialogs;

public class AssignableItem
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? OperationId { get; set; }
    public bool IsSelected { get; set; }
    public object? Tag { get; set; }
}
