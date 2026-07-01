using System.Xml.Linq;
using OneMMC.Core.Features.PCManagement.Models.EventViewer;
using Microsoft.UI.Xaml.Controls;

namespace OneMMC.Views;

public sealed partial class EventDetailsPage : Page
{
    public EventLogEntry? SelectedEvent { get; set; }

    public EventDetailsPage()
    {
        InitializeComponent();
    }

    public void Refresh()
    {
        if (SelectedEvent is null)
        {
            XmlTextBox.Text = string.Empty;
            return;
        }

        try
        {
            var doc = XDocument.Parse(SelectedEvent.XmlData);
            XmlTextBox.Text = doc.ToString();
        }
        catch
        {
            XmlTextBox.Text = SelectedEvent.XmlData;
        }
    }
}

