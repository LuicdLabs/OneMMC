using Microsoft.UI.Xaml.Controls;

namespace ManagementTools.Views.PCManagement;

public sealed partial class TaskSchedulerPage : Page
{
	public TaskSchedulerPage()
	{
		InitializeComponent();
		this.RequestedTheme = App.CurrentTheme;
		App.ThemeChanged += OnThemeChanged;
		this.Unloaded += (_, _) => App.ThemeChanged -= OnThemeChanged;
	}

	private void OnThemeChanged(Microsoft.UI.Xaml.ElementTheme theme)
	{
		this.RequestedTheme = theme;
	}
}
