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
using ManagementTools.Models;
using ManagementTools.Localization;
using ManagementTools.Services;
using ManagementTools.Views.PolicyManagement.GpEdit;
using CommunityToolkit.Mvvm.Input;

namespace ManagementTools.Views.PolicyManagement;

public sealed partial class PolicyManagement : Page
{
	public ObservableCollection<SettingItem> SettingsItems { get; set; } = new();
	private static LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;
	private static readonly SettingItemData[] SettingsData =
	[
		new SettingItemData
		{
			Glyph = "\uEA18",
			TitleKey = "SettingItem_GroupPolicyEditor_Title",
			SubtitleKey = "SettingItem_GroupPolicyEditor_Subtitle",
			NavigationIndex = 0
		},
		new SettingItemData
		{
			Glyph = "\uE713",
			TitleKey = "SettingItem_ResultantSetOfPolicy_Title",
			SubtitleKey = "SettingItem_ResultantSetOfPolicy_Subtitle",
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

	public PolicyManagement()
	{
		this.InitializeComponent();
		this.Loaded += PolicyManagement_Loaded;
		this.RequestedTheme = App.CurrentTheme;
		App.ThemeChanged += OnThemeChanged;
		this.Unloaded += (_, _) =>
		{
			App.ThemeChanged -= OnThemeChanged;
			SettingsItems.Clear();
			DataContext = null;
			this.Loaded -= PolicyManagement_Loaded;
		};
	}

	private void PolicyManagement_Loaded(object sender, RoutedEventArgs e)
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
			BreadcrumbNavigationService.AddBreadcrumb(LocalizedStrings.PageTitle_GroupPolicyEditor ?? "Group Policy", typeof(GroupPolicyEditorPage));
			this.Frame.Navigate(typeof(GroupPolicyEditorPage), null, new SlideNavigationTransitionInfo() { Effect = SlideNavigationTransitionEffect.FromRight });
		}
		else if (index == 1)
		{
			// Add breadcrumb and navigate
			BreadcrumbNavigationService.AddBreadcrumb(LocalizedStrings.PageTitle_ResultantSetOfPolicy ?? "Resultant Set of Policy", typeof(ResultantSetOfPolicyPage));
			this.Frame.Navigate(typeof(ResultantSetOfPolicyPage), null, new SlideNavigationTransitionInfo() { Effect = SlideNavigationTransitionEffect.FromRight });
		}
	}
}

