using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Authentication;
using ManagementTools.Core.Features.SystemManagement.Models.WF.ConnectionSecurity;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Monitoring;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Profiles;
using ManagementTools.Core.Features.SystemManagement.Models.WF.Rules;
using ManagementTools.Localization;
using Microsoft.UI.Xaml.Controls;

namespace ManagementTools.Views.Dialogs.WFProperties;

public sealed partial class ProtectedNetworkConnectionsDialog : ContentDialog
{
    public ObservableCollection<NetworkConnectionItem> Connections { get; } = [];
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    public ProtectedNetworkConnectionsDialog(string profileName, IEnumerable<NetworkConnectionItem> connections)
    {
        InitializeComponent();
        RequestedTheme = App.CurrentTheme;
        App.ThemeChanged += OnThemeChanged;
        Unloaded += ProtectedNetworkConnectionsDialog_Unloaded;

        Title = string.Format(System.Globalization.CultureInfo.CurrentCulture, LocalizedStrings.WF_ProtectedNetworkConnections_TitleFormat, profileName);
        ConnectionsListView.ItemsSource = Connections;

        foreach (NetworkConnectionItem connection in connections.OrderBy(item => item.Name, System.StringComparer.CurrentCultureIgnoreCase))
        {
            Connections.Add(new NetworkConnectionItem
            {
                Name = connection.Name,
                IsSelected = connection.IsSelected
            });
        }
    }

    private void OnThemeChanged(Microsoft.UI.Xaml.ElementTheme theme)
    {
        RequestedTheme = theme;
    }

    private void ProtectedNetworkConnectionsDialog_Unloaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        App.ThemeChanged -= OnThemeChanged;
    }
}
