using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OneMMC.Core.Features.SystemManagement.Models.ComExp;
using OneMMC.Localization;

namespace OneMMC.Views.ComExp;

/// <summary>
/// Read-only properties dialog for a DCOM application, mirroring the
/// General / Location / Security / Endpoints / Identity tabs of dcomcnfg.
/// </summary>
public sealed partial class DcomPropertiesDialog : ContentDialog
{
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    public DcomPropertiesDialog(DcomApplicationInfo application)
    {
        InitializeComponent();
        Populate(application);
    }

    private void Populate(DcomApplicationInfo app)
    {
        var L = LocalizedStrings;
        string NotSet(string? value) => string.IsNullOrWhiteSpace(value) ? L.ComExp_Dcom_NotSet : value;

        AppTitle.Text = app.Name;
        AppIdText.Text = app.AppId;

        GeneralNameText.Text = app.Name;
        GeneralAppIdText.Text = app.AppId;
        GeneralTypeText.Text = app.ApplicationType;
        GeneralAuthText.Text = app.AuthenticationLevelDisplay;
        GeneralPathText.Text = NotSet(app.LocalPath);
        GeneralSurrogateText.Text = app.HasDllSurrogate ? NotSet(app.DllSurrogate) : L.ComExp_Dcom_NotSet;
        GeneralSvcParamsText.Text = NotSet(app.ServiceParameters);

        RunOnThisComputerCheck.IsChecked = app.RunOnThisComputer;
        bool runOnRemote = !string.IsNullOrWhiteSpace(app.RemoteServerName);
        RunOnRemoteCheck.IsChecked = runOnRemote;
        RemoteComputerBox.Text = app.RemoteServerName ?? string.Empty;

        LaunchPermText.Text = app.HasCustomLaunchPermissions ? L.ComExp_Dcom_UseCustom : L.ComExp_Dcom_UseDefault;
        AccessPermText.Text = app.HasCustomAccessPermissions ? L.ComExp_Dcom_UseCustom : L.ComExp_Dcom_UseDefault;

        IdentityText.Text = app.IdentityDisplay;
        IdentityRunAsText.Text = NotSet(app.RunAs);
        IdentityServiceText.Text = NotSet(app.LocalService);
    }

    private void DcomPropertiesSelectorBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        GeneralPanel.Visibility = Visibility.Collapsed;
        LocationPanel.Visibility = Visibility.Collapsed;
        SecurityPanel.Visibility = Visibility.Collapsed;
        EndpointsPanel.Visibility = Visibility.Collapsed;
        IdentityPanel.Visibility = Visibility.Collapsed;

        if (sender.SelectedItem is SelectorBarItem selectedItem && selectedItem.Tag is string tag)
        {
            switch (tag)
            {
                case "General":
                    GeneralPanel.Visibility = Visibility.Visible;
                    break;
                case "Location":
                    LocationPanel.Visibility = Visibility.Visible;
                    break;
                case "Security":
                    SecurityPanel.Visibility = Visibility.Visible;
                    break;
                case "Endpoints":
                    EndpointsPanel.Visibility = Visibility.Visible;
                    break;
                case "Identity":
                    IdentityPanel.Visibility = Visibility.Visible;
                    break;
            }
        }
    }
}
