using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using ManagementTools.Localization;
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
using ManagementTools.Models;
using ManagementTools.Services;
using CommunityToolkit.Mvvm.Input;

namespace ManagementTools.Views;

public sealed partial class CertificatesCredential : Page
{
	public ObservableCollection<SettingItem> SettingsItems { get; set; } = new();
	private static LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;
	private static readonly SettingItemData[] SettingsData =
	[
		new SettingItemData
		{
			Glyph = "\uEB95",
			TitleKey = "SettingItem_LocalCertificates_Title",
			SubtitleKey = "SettingItem_LocalCertificates_Subtitle",
			NavigationIndex = 0
		},
		new SettingItemData
		{
			Glyph = "\uECA7",
			TitleKey = "SettingItem_CurrentUserCertificates_Title",
			SubtitleKey = "SettingItem_CurrentUserCertificates_Subtitle",
			NavigationIndex = 1
		}
	];

	private sealed class SettingItemData
	{
		public string Glyph { get; init; } = string.Empty;
		public string TitleKey { get; init; } = string.Empty;
		public string SubtitleKey { get; init; } = string.Empty;
		public int NavigationIndex { get; init; }
	}

	public CertificatesCredential()
	{
		this.InitializeComponent();
		this.Loaded += CertificatesCredential_Loaded;
		this.RequestedTheme = App.CurrentTheme;
		App.ThemeChanged += OnThemeChanged;
		this.Unloaded += (_, _) =>
		{
			App.ThemeChanged -= OnThemeChanged;
			SettingsItems.Clear();
			DataContext = null;
			this.Loaded -= CertificatesCredential_Loaded;
		};
	}

	private void CertificatesCredential_Loaded(object sender, RoutedEventArgs e)
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
			BreadcrumbNavigationService.AddBreadcrumb(LocalizedStrings.PageTitle_LocalCertificates ?? "Local Certificates", typeof(LocalComputerCertificatesPage));
			this.Frame.Navigate(typeof(LocalComputerCertificatesPage), null, new SlideNavigationTransitionInfo() { Effect = SlideNavigationTransitionEffect.FromRight });
		}
		else if (index == 1)
		{
			// Add breadcrumb and navigate
			BreadcrumbNavigationService.AddBreadcrumb(LocalizedStrings.PageTitle_CurrentUserCertificates ?? "Current User Certificates", typeof(CurrentUserCertificatesPage));
			this.Frame.Navigate(typeof(CurrentUserCertificatesPage), null, new SlideNavigationTransitionInfo() { Effect = SlideNavigationTransitionEffect.FromRight });
		}
	}
}
