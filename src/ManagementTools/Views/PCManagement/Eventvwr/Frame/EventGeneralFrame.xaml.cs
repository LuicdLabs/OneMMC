using ManagementTools.Core.Features.PCManagement.Models.EventViewer;
using ManagementTools.Localization;
using Microsoft.UI.Xaml.Controls;

namespace ManagementTools.Views;

public sealed partial class EventGeneralPage : Page
{
    public EventLogEntry? SelectedEvent { get; set; }
    public LocalizedStrings Strings { get; } = LocalizedStrings.Instance;

    public EventGeneralPage()
    {
        InitializeComponent();
    }

    public void Refresh()
    {
        Bindings.Update();
    }
}

