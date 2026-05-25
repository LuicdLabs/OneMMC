using Microsoft.UI.Xaml.Controls;
using ManagementTools.Models;
using ManagementTools.Localization;

namespace ManagementTools.Views;

public sealed partial class CurrentUserCertificatesPage : Page
{
	public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

	public CurrentUserCertificatesPage()
	{
		InitializeComponent();
	}
}