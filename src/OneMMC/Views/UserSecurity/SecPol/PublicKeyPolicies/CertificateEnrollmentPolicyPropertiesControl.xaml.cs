using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using OneMMC.Core.Features.UserSecurity.Models.SecPol.PublicKeyPolicies;
using OneMMC.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OneMMC.Views;

/// <summary>
/// Edits local machine certificate enrollment policy settings.
/// </summary>
public sealed partial class CertificateEnrollmentPolicyPropertiesControl : UserControl
{
    private const uint UseClientIdFlag = 0x00000004;
    private const uint AutoEnrollmentFlag = 0x00000010;
    private const uint AllowUntrustedCaFlag = 0x00000020;
    private const uint DefaultPolicyServerCost = 0x7FFFFFFD;

    private CertificateEnrollmentPolicyServer? _editingServer;
    private bool _isAddingServer;

    /// <summary>
    /// Gets localized strings for XAML binding.
    /// </summary>
    public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

    /// <summary>
    /// Gets the editable policy server list.
    /// </summary>
    public ObservableCollection<CertificateEnrollmentPolicyServer> Servers { get; } = [];

    /// <summary>
    /// Initializes the certificate enrollment policy editor.
    /// </summary>
    /// <param name="settings">The settings to edit.</param>
    public CertificateEnrollmentPolicyPropertiesControl(CertificateEnrollmentPolicySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        InitializeComponent();
        ConfigurationModelComboBox.SelectedIndex = settings.State switch
        {
            CertificateEnrollmentPolicyState.Enabled => 1,
            CertificateEnrollmentPolicyState.Disabled => 2,
            _ => 0
        };
        DisableUserServersToggle.IsOn = settings.DisableUserConfiguredServers;

        foreach (CertificateEnrollmentPolicyServer server in settings.Servers)
        {
            Servers.Add(CloneServer(server));
        }

        UpdateCommandState();
    }

    /// <summary>
    /// Gets the currently selected certificate enrollment policy settings.
    /// </summary>
    /// <returns>The edited certificate enrollment policy settings.</returns>
    public CertificateEnrollmentPolicySettings GetSettings()
    {
        CertificateEnrollmentPolicySettings settings = new()
        {
            State = ConfigurationModelComboBox.SelectedIndex switch
            {
                1 => CertificateEnrollmentPolicyState.Enabled,
                2 => CertificateEnrollmentPolicyState.Disabled,
                _ => CertificateEnrollmentPolicyState.NotConfigured
            },
            DisableUserConfiguredServers = DisableUserServersToggle.IsOn
        };

        foreach (CertificateEnrollmentPolicyServer server in Servers)
        {
            settings.Servers.Add(CloneServer(server));
        }

        return settings;
    }

    private void ConfigurationModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateCommandState();
    }

    private void ServersListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateCommandState();
    }

    private void AddServerButton_Click(object sender, RoutedEventArgs e)
    {
        _editingServer = null;
        _isAddingServer = true;
        PopulateEditor(new CertificateEnrollmentPolicyServer
        {
            Flags = AutoEnrollmentFlag,
            AuthType = CertificateEnrollmentPolicyAuthType.Kerberos,
            Cost = DefaultPolicyServerCost,
            PolicyId = $"{{{Guid.NewGuid():D}}}".ToUpperInvariant()
        });
        ServerEditorTitle.Text = LocalizedStrings.PKP_EnrollmentPolicy_ServerEditor_AddTitle;
        ShowEditor();
    }

    private void EditServerButton_Click(object sender, RoutedEventArgs e)
    {
        if (ServersListView.SelectedItem is not CertificateEnrollmentPolicyServer server)
        {
            return;
        }

        _editingServer = server;
        _isAddingServer = false;
        PopulateEditor(server);
        ServerEditorTitle.Text = LocalizedStrings.PKP_EnrollmentPolicy_ServerEditor_EditTitle;
        ShowEditor();
    }

    private void RemoveServerButton_Click(object sender, RoutedEventArgs e)
    {
        if (ServersListView.SelectedItem is not CertificateEnrollmentPolicyServer server)
        {
            return;
        }

        _ = Servers.Remove(server);
        HideEditor();
        UpdateCommandState();
    }

    private void SaveServerButton_Click(object sender, RoutedEventArgs e)
    {
        CertificateEnrollmentPolicyServer? editedServer = CreateServerFromEditor();
        if (editedServer is null)
        {
            return;
        }

        if (_isAddingServer)
        {
            Servers.Add(editedServer);
            ServersListView.SelectedItem = editedServer;
        }
        else if (_editingServer is not null)
        {
            int index = Servers.IndexOf(_editingServer);
            if (index >= 0)
            {
                Servers[index] = editedServer;
                ServersListView.SelectedItem = editedServer;
            }
        }

        HideEditor();
        UpdateCommandState();
    }

    private void CancelServerEditButton_Click(object sender, RoutedEventArgs e)
    {
        HideEditor();
    }

    private CertificateEnrollmentPolicyServer? CreateServerFromEditor()
    {
        string url = ServerUrlTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            ShowValidation(LocalizedStrings.PKP_EnrollmentPolicy_ServerValidation_UrlRequired);
            return null;
        }

        if (Servers.Any(server => !ReferenceEquals(server, _editingServer)
            && string.Equals(server.Url.Trim(), url, StringComparison.OrdinalIgnoreCase)))
        {
            ShowValidation(LocalizedStrings.PKP_EnrollmentPolicy_ServerValidation_DuplicateUrl);
            return null;
        }

        CertificateEnrollmentPolicyAuthType authType = GetSelectedAuthType();
        uint flags = 0U;
        if (ServerAutoEnrollmentCheckBox.IsChecked == true)
        {
            flags |= AutoEnrollmentFlag;
        }

        if (ServerUseClientIdCheckBox.IsChecked == true)
        {
            flags |= UseClientIdFlag;
        }

        if (ServerAllowUntrustedCaCheckBox.IsChecked == true)
        {
            flags |= AllowUntrustedCaFlag;
        }

        string friendlyName = ServerNameTextBox.Text.Trim();
        string policyId = string.IsNullOrWhiteSpace(ServerPolicyIdTextBox.Text)
            ? $"{{{Guid.NewGuid():D}}}".ToUpperInvariant()
            : ServerPolicyIdTextBox.Text.Trim();
        uint cost = double.IsNaN(ServerCostNumberBox.Value)
            ? DefaultPolicyServerCost
            : (uint)Math.Clamp(ServerCostNumberBox.Value, 0, int.MaxValue);

        return new CertificateEnrollmentPolicyServer
        {
            Id = string.IsNullOrWhiteSpace(_editingServer?.Id)
                ? CreateServerId(url)
                : _editingServer.Id,
            Url = url,
            PolicyId = policyId,
            FriendlyName = string.IsNullOrWhiteSpace(friendlyName) ? url : friendlyName,
            Flags = flags,
            AuthType = authType,
            Cost = cost,
            AuthenticationText = FormatAuthentication(authType)
        };
    }

    private void PopulateEditor(CertificateEnrollmentPolicyServer server)
    {
        ServerNameTextBox.Text = server.FriendlyName;
        ServerUrlTextBox.Text = server.Url;
        ServerPolicyIdTextBox.Text = server.PolicyId;
        ServerAuthenticationComboBox.SelectedIndex = GetAuthenticationIndex(server.AuthType);
        ServerCostNumberBox.Value = server.Cost == 0U ? DefaultPolicyServerCost : server.Cost;
        ServerAutoEnrollmentCheckBox.IsChecked = (server.Flags & AutoEnrollmentFlag) == AutoEnrollmentFlag;
        ServerUseClientIdCheckBox.IsChecked = (server.Flags & UseClientIdFlag) == UseClientIdFlag;
        ServerAllowUntrustedCaCheckBox.IsChecked = (server.Flags & AllowUntrustedCaFlag) == AllowUntrustedCaFlag;
        ValidationInfoBar.IsOpen = false;
    }

    private void ShowEditor()
    {
        ServerEditorPanel.Visibility = Visibility.Visible;
        UpdateCommandState();
    }

    private void HideEditor()
    {
        _editingServer = null;
        _isAddingServer = false;
        ServerEditorPanel.Visibility = Visibility.Collapsed;
        ValidationInfoBar.IsOpen = false;
        UpdateCommandState();
    }

    private void ShowValidation(string message)
    {
        ValidationInfoBar.Message = message;
        ValidationInfoBar.IsOpen = true;
    }

    private void UpdateCommandState()
    {
        if (AddServerButton is null)
        {
            return;
        }

        bool isPolicyEnabled = ConfigurationModelComboBox.SelectedIndex == 1;
        bool isPolicyConfigured = ConfigurationModelComboBox.SelectedIndex is 1 or 2;
        bool hasSelection = ServersListView.SelectedItem is CertificateEnrollmentPolicyServer;
        bool isEditing = ServerEditorPanel.Visibility == Visibility.Visible;
        AddServerButton.IsEnabled = isPolicyEnabled && !isEditing;
        EditServerButton.IsEnabled = isPolicyEnabled && hasSelection && !isEditing;
        RemoveServerButton.IsEnabled = isPolicyEnabled && hasSelection && !isEditing;
        ServersListView.IsEnabled = isPolicyEnabled && !isEditing;
        SaveServerButton.IsEnabled = isPolicyEnabled;
        DisableUserServersToggle.IsEnabled = isPolicyConfigured && !isEditing;
    }

    private CertificateEnrollmentPolicyAuthType GetSelectedAuthType()
    {
        return ServerAuthenticationComboBox.SelectedIndex switch
        {
            0 => CertificateEnrollmentPolicyAuthType.Anonymous,
            2 => CertificateEnrollmentPolicyAuthType.UserName,
            3 => CertificateEnrollmentPolicyAuthType.ClientCertificate,
            _ => CertificateEnrollmentPolicyAuthType.Kerberos
        };
    }

    private static int GetAuthenticationIndex(CertificateEnrollmentPolicyAuthType authType)
    {
        return authType switch
        {
            CertificateEnrollmentPolicyAuthType.Anonymous => 0,
            CertificateEnrollmentPolicyAuthType.UserName => 2,
            CertificateEnrollmentPolicyAuthType.ClientCertificate => 3,
            _ => 1
        };
    }

    private string FormatAuthentication(CertificateEnrollmentPolicyAuthType authType)
    {
        return authType switch
        {
            CertificateEnrollmentPolicyAuthType.Anonymous => LocalizedStrings.PKP_EnrollmentPolicy_Auth_Anonymous,
            CertificateEnrollmentPolicyAuthType.UserName => LocalizedStrings.PKP_EnrollmentPolicy_Auth_UserName,
            CertificateEnrollmentPolicyAuthType.ClientCertificate => LocalizedStrings.PKP_EnrollmentPolicy_Auth_ClientCertificate,
            _ => LocalizedStrings.PKP_EnrollmentPolicy_Auth_Kerberos
        };
    }

    private static CertificateEnrollmentPolicyServer CloneServer(CertificateEnrollmentPolicyServer server)
    {
        return new CertificateEnrollmentPolicyServer
        {
            Id = server.Id,
            Url = server.Url,
            PolicyId = server.PolicyId,
            FriendlyName = server.FriendlyName,
            Flags = server.Flags,
            AuthType = server.AuthType,
            Cost = server.Cost,
            AuthenticationText = server.AuthenticationText
        };
    }

    private static string CreateServerId(string url)
    {
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(url.Trim()))).ToLowerInvariant();
    }
}
