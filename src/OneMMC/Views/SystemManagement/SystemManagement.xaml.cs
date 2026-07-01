using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using OneMMC.Models;
using OneMMC.Localization;
using OneMMC.Services;
using CommunityToolkit.Mvvm.Input;

namespace OneMMC.Views;

public sealed partial class SystemManagement : Page
{
	public ObservableCollection<SettingItem> SettingsItems { get; set; } = new();
	private static LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;
	private static readonly SettingItemData[] SettingsData =
	[
		new SettingItemData
		{
			Glyph = "\uE9F5",
			TitleKey = "SettingItem_ComponentServices_Title",
			SubtitleKey = "SettingItem_ComponentServices_Subtitle",
			NavigationIndex = 0
		},
		new SettingItemData
		{
			Glyph = "\uE88A",
			TitleKey = "SettingItem_WindowsFirewall_Title",
			SubtitleKey = "SettingItem_WindowsFirewall_Subtitle",
			NavigationIndex = 1
		},
		new SettingItemData
		{
			Glyph = "\uEC19",
			TitleKey = "SettingItem_TPMManager_Title",
			SubtitleKey = "SettingItem_TPMManager_Subtitle",
			NavigationIndex = 2
		}
	];

	private sealed class SettingItemData
	{
		public string Glyph { get; init; } = string.Empty;
		public string TitleKey { get; init; } = string.Empty;
		public string SubtitleKey { get; init; } = string.Empty;
		public int NavigationIndex { get; init; }
	}

	public SystemManagement()
	{
		this.InitializeComponent();
		this.Loaded += SystemManagement_Loaded;
		this.RequestedTheme = App.CurrentTheme;
		App.ThemeChanged += OnThemeChanged;
		this.Unloaded += (_, _) =>
		{
			App.ThemeChanged -= OnThemeChanged;
			SettingsItems.Clear();
			DataContext = null;
			this.Loaded -= SystemManagement_Loaded;
		};
	}

	private void SystemManagement_Loaded(object sender, RoutedEventArgs e)
	{
        SettingsItems.Clear();

        // Handle localization in the UI layer, converting ViewModel data into UI SettingItems
        foreach (var data in SettingsData)
		{
			SettingsItems.Add(new SettingItem 
			{ 
				Glyph = data.Glyph, 
				TitleKey = data.TitleKey, 
				SubtitleKey = data.SubtitleKey,
				Command = new RelayCommand(() => NavigateToPage(data.NavigationIndex))
			});
		}
		
		this.DataContext = this;
	}

	private void OnThemeChanged(Microsoft.UI.Xaml.ElementTheme theme)
	{
		this.RequestedTheme = theme;
	}

	public void NavigateToPage(int index)
	{
		if (this.Frame == null)
		{
			// Frame is not available, cannot navigate
			return;
		}

		if (index == 0)
		{			
			// Add breadcrumb and navigate
			BreadcrumbNavigationService.AddBreadcrumb(LocalizedStrings.PageTitle_ComponentServices ?? "Component Services", typeof(ComponentServicesPage));
			this.Frame.Navigate(typeof(ComponentServicesPage), null, new SlideNavigationTransitionInfo() { Effect = SlideNavigationTransitionEffect.FromRight });
		}
		else if (index == 1)
		{
			// Add breadcrumb and navigate
			BreadcrumbNavigationService.AddBreadcrumb(LocalizedStrings.PageTitle_WindowsFirewall ?? "Windows Firewall", typeof(WindowsFirewallPage));
			this.Frame.Navigate(typeof(WindowsFirewallPage), null, new SlideNavigationTransitionInfo() { Effect = SlideNavigationTransitionEffect.FromRight });
		}
		else if (index == 2)
		{			
			// Add breadcrumb and navigate
			BreadcrumbNavigationService.AddBreadcrumb(LocalizedStrings.PageTitle_TPMManagement ?? "TPM Management", typeof(TPMManagerPage));
			this.Frame.Navigate(typeof(TPMManagerPage), null, new SlideNavigationTransitionInfo() { Effect = SlideNavigationTransitionEffect.FromRight });
		}
	}
}
