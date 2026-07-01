using OneMMC.Core.Features.PCManagement.Models.EventViewer;
using OneMMC.Localization;
using Microsoft.UI.Xaml.Controls;

namespace OneMMC.Views;

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

