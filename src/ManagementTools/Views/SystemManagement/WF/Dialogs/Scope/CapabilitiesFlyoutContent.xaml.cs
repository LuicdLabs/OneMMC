using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;

namespace ManagementTools.Views.Dialogs.Scope;

public sealed partial class CapabilitiesFlyoutContent : UserControl
{
    public event System.EventHandler<IList<(string Name, string Sid)>>? CapabilitiesConfirmed;
    public event System.EventHandler? Cancelled;

    private static readonly (string Sid, string FallbackName)[] WellKnownCapabilities =
    [
        ("S-1-15-2-1",  "ALL APPLICATION PACKAGES"),
        ("S-1-15-3-1",  "internetClient"),
        ("S-1-15-3-2",  "internetClientServer"),
        ("S-1-15-3-3",  "privateNetworkClientServer"),
        ("S-1-15-3-4",  "picturesLibrary"),
        ("S-1-15-3-5",  "videosLibrary"),
        ("S-1-15-3-6",  "musicLibrary"),
        ("S-1-15-3-7",  "documentsLibrary"),
        ("S-1-15-3-8",  "enterpriseAuthentication"),
        ("S-1-15-3-9",  "sharedUserCertificates"),
        ("S-1-15-3-10", "removableStorage"),
    ];

    public CapabilitiesFlyoutContent()
    {
        InitializeComponent();
        PopulateCapabilities();
    }

    private void PopulateCapabilities()
    {
        CapabilitiesListView.Items.Clear();

        foreach (var (sid, fallbackName) in WellKnownCapabilities)
        {
            string displayName = ResolveDisplayName(sid, fallbackName);
            CapabilitiesListView.Items.Add(new ListViewItem
            {
                Content = displayName,
                Tag = sid
            });
        }
    }

    private static string ResolveDisplayName(string sidString, string fallback)
    {
        try
        {
            var sid = new SecurityIdentifier(sidString);
            return sid.Translate(typeof(NTAccount)).Value;
        }
        catch
        {
            return fallback;
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = CapabilitiesListView.SelectedItems
            .OfType<ListViewItem>()
            .Select(i => (Name: i.Content?.ToString() ?? string.Empty, Sid: i.Tag?.ToString() ?? string.Empty))
            .Where(x => !string.IsNullOrEmpty(x.Name) && !string.IsNullOrEmpty(x.Sid))
            .ToList();
            
        CapabilitiesConfirmed?.Invoke(this, selected);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Cancelled?.Invoke(this, EventArgs.Empty);
    }
}
