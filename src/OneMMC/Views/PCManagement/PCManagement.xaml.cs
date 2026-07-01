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
using CommunityToolkit.Mvvm.Input;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.
namespace OneMMC.Views.PCManagement;

public sealed partial class PCManagement : Page
{
	public ObservableCollection<SettingItem> SettingsItems { get; set; } = new();
	private static readonly SettingItemData[] SettingsData =
	[
		new SettingItemData
		{
			Glyph = "\uE912",
			TitleKey = "SettingItem_DeviceManager_Title",
			SubtitleKey = "SettingItem_DeviceManager_Subtitle",
			NavigationIndex = 0
		},
		new SettingItemData
		{
			Glyph = "\uEDA2",
			TitleKey = "SettingItem_DiskManagement_Title",
			SubtitleKey = "SettingItem_DiskManagement_Subtitle",
			NavigationIndex = 1
		},
		new SettingItemData
		{
			Glyph = "\uEA86",
			TitleKey = "SettingItem_Services_Title",
			SubtitleKey = "SettingItem_Services_Subtitle",
			NavigationIndex = 2
		},
		new SettingItemData
		{
			Glyph = "\uEBE8",
			TitleKey = "SettingItem_EventViewer_Title",
			SubtitleKey = "SettingItem_EventViewer_Subtitle",
			NavigationIndex = 3
		},
		new SettingItemData
		{
			Glyph = "\uE823",
			TitleKey = "SettingItem_TaskScheduler_Title",
			SubtitleKey = "SettingItem_TaskScheduler_Subtitle",
			NavigationIndex = 4
		},
		new SettingItemData
		{
			Glyph = "\uE9D9",
			TitleKey = "SettingItem_PerformanceMonitor_Title",
			SubtitleKey = "SettingItem_PerformanceMonitor_Subtitle",
			NavigationIndex = 5
		},
		new SettingItemData
		{
			Glyph = "\uE716",
			TitleKey = "SettingItem_LocalUsersGroups_Title",
			SubtitleKey = "SettingItem_LocalUsersGroups_Subtitle",
			NavigationIndex = 6
		},
		new SettingItemData
		{
			Glyph = "\uEC25",
			TitleKey = "SettingItem_SharedFolders_Title",
			SubtitleKey = "SettingItem_SharedFolders_Subtitle",
			NavigationIndex = 7
		}
	];

	private sealed class SettingItemData
	{
		public string Glyph { get; init; } = string.Empty;
		public string TitleKey { get; init; } = string.Empty;
		public string SubtitleKey { get; init; } = string.Empty;
		public int NavigationIndex { get; init; }
	}

	public PCManagement()
	{
		InitializeComponent();
		this.Loaded += PCManagement_Loaded;
		this.RequestedTheme = App.CurrentTheme;
		App.ThemeChanged += OnThemeChanged;
		this.Unloaded += (_, _) =>
		{
			App.ThemeChanged -= OnThemeChanged;
			SettingsItems.Clear();
			DataContext = null;
			this.Loaded -= PCManagement_Loaded;
		};
	}
	
	private void OnThemeChanged(Microsoft.UI.Xaml.ElementTheme theme)
	{
		this.RequestedTheme = theme;
	}
	
	private void PCManagement_Loaded(object sender, RoutedEventArgs e)
	{
		var mainWindow = App.MainWindowInstance;
		SettingsItems.Clear();

        // Handle localization in the UI layer, converting ViewModel data into UI SettingItems
		foreach (var data in SettingsData)
		{
			SettingsItems.Add(new SettingItem 
			{ 
				Glyph = data.Glyph, 
				TitleKey = data.TitleKey, 
				SubtitleKey = data.SubtitleKey,
				Command = new RelayCommand(() => mainWindow?.NavigateToPage(data.NavigationIndex))
			});
		}
		
		this.DataContext = this;
	}
}
