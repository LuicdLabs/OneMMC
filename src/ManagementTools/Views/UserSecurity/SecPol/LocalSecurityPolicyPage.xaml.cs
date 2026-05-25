using System.Diagnostics;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using ManagementTools.Localization;
using ManagementTools.Services;

namespace ManagementTools.Views;

public sealed partial class LocalSecurityPolicyPage : Page
{
	public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;

	public LocalSecurityPolicyPage()
	{
		ManagementTools.Services.Logging.UiLogger.LogDebug("[LocalSecurityPolicyPage] Initializing");
		InitializeComponent();
	}

	private void AccountPolicies_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
	{
		ManagementTools.Services.Logging.UiLogger.LogDebug("[LocalSecurityPolicyPage] Navigating to Account Policies");
		BreadcrumbNavigationService.AddBreadcrumb(LocalizedStrings.SecPol_AccountPolicies_Header, typeof(AccountPoliciesPage));
		Frame.Navigate(typeof(AccountPoliciesPage), null, new SlideNavigationTransitionInfo() { Effect = SlideNavigationTransitionEffect.FromRight });
	}

	private void LocalPolicies_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
	{
		ManagementTools.Services.Logging.UiLogger.LogDebug("[LocalSecurityPolicyPage] Navigating to Local Policies");
		BreadcrumbNavigationService.AddBreadcrumb(LocalizedStrings.SecPol_LocalPolicies_Header, typeof(LocalPoliciesPage));
		Frame.Navigate(typeof(LocalPoliciesPage), null, new SlideNavigationTransitionInfo() { Effect = SlideNavigationTransitionEffect.FromRight });
	}

	private void WindowsFirewall_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
	{
		ManagementTools.Services.Logging.UiLogger.LogDebug("[LocalSecurityPolicyPage] Navigating to Windows Firewall");
		BreadcrumbNavigationService.AddBreadcrumb(LocalizedStrings.SecPol_Firewall_Header, typeof(WindowsFirewallSecurityPage));
		Frame.Navigate(typeof(WindowsFirewallSecurityPage), null, new SlideNavigationTransitionInfo() { Effect = SlideNavigationTransitionEffect.FromRight });
	}

	private void NetworkListManager_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
	{
		ManagementTools.Services.Logging.UiLogger.LogDebug("[LocalSecurityPolicyPage] Navigating to Network List Manager");
		BreadcrumbNavigationService.AddBreadcrumb(LocalizedStrings.SecPol_NetworkListManager_Header, typeof(NetworkListManagerPage));
		Frame.Navigate(typeof(NetworkListManagerPage), null, new SlideNavigationTransitionInfo() { Effect = SlideNavigationTransitionEffect.FromRight });
	}

	private void PublicKeyPolicies_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
	{
		ManagementTools.Services.Logging.UiLogger.LogDebug("[LocalSecurityPolicyPage] Navigating to Public Key Policies");
		BreadcrumbNavigationService.AddBreadcrumb(LocalizedStrings.SecPol_PublicKeyPolicies_Header, typeof(PublicKeyPoliciesPage));
		Frame.Navigate(typeof(PublicKeyPoliciesPage), null, new SlideNavigationTransitionInfo() { Effect = SlideNavigationTransitionEffect.FromRight });
	}

	private void SoftwareRestriction_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
	{
		ManagementTools.Services.Logging.UiLogger.LogDebug("[LocalSecurityPolicyPage] Navigating to Software Restriction");
		BreadcrumbNavigationService.AddBreadcrumb(LocalizedStrings.SecPol_SoftwareRestriction_Header, typeof(SoftwareRestrictionPage));
		Frame.Navigate(typeof(SoftwareRestrictionPage), null, new SlideNavigationTransitionInfo() { Effect = SlideNavigationTransitionEffect.FromRight });
	}

	private void AppLocker_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
	{
		ManagementTools.Services.Logging.UiLogger.LogDebug("[LocalSecurityPolicyPage] Navigating to AppLocker");
		BreadcrumbNavigationService.AddBreadcrumb(LocalizedStrings.SecPol_AppLocker_Header, typeof(AppLockerPage));
		Frame.Navigate(typeof(AppLockerPage), null, new SlideNavigationTransitionInfo() { Effect = SlideNavigationTransitionEffect.FromRight });
	}

	private void IPSecurity_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
	{
		ManagementTools.Services.Logging.UiLogger.LogDebug("[LocalSecurityPolicyPage] Navigating to IP Security");
		BreadcrumbNavigationService.AddBreadcrumb(LocalizedStrings.SecPol_IPSecurity_Header, typeof(IPSecurityPage));
		Frame.Navigate(typeof(IPSecurityPage), null, new SlideNavigationTransitionInfo() { Effect = SlideNavigationTransitionEffect.FromRight });
	}

	private void SystemAudit_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
	{
		ManagementTools.Services.Logging.UiLogger.LogDebug("[LocalSecurityPolicyPage] Navigating to System Audit");
		BreadcrumbNavigationService.AddBreadcrumb(LocalizedStrings.SecPol_SystemAudit_Header, typeof(SystemAuditPage));
		Frame.Navigate(typeof(SystemAuditPage), null, new SlideNavigationTransitionInfo() { Effect = SlideNavigationTransitionEffect.FromRight });
	}
}
