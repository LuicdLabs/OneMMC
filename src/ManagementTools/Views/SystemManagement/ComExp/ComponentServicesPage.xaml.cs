using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.WinUI.Controls;
using ManagementTools.Core.Localization;
using ManagementTools.Core.Features.SystemManagement.Models.ComExp;
using ManagementTools.Core.Features.SystemManagement.Services.ComExp;
using ManagementTools.Helpers;
using ManagementTools.Localization;
using ManagementTools.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace ManagementTools.Views;

public sealed partial class ComponentServicesPage : Page
{
	public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;
	public ObservableCollection<ComPlusApplicationInfo> ComPlusApplications { get; } = new();
	private readonly ComponentServicesManager _componentServicesService;
	private bool _isInitialized;
	private SettingsExpander? _comPlusExpander;
	private SettingsCard? _dcomConfigCard;
	private SettingsCard? _runningProcessesCard;
	private SettingsCard? _transactionListCard;
	private SettingsCard? _transactionStatisticsCard;
	private TextBlock? _currentPcText;
	private AppBarButton? _refreshButton;

	public string CurrentPCText => string.Format(LocalizedStrings.ComExp_CurrentPC, Environment.MachineName);

	public ComponentServicesPage()
	{
		_componentServicesService = App.GetRequiredService<ComponentServicesManager>();
		InitializeComponent();
		Loaded += ComponentServicesPage_Loaded;
		Unloaded += ComponentServicesPage_Unloaded;
	}

	private void ComponentServicesPage_Unloaded(object sender, RoutedEventArgs e)
	{
		Loaded -= ComponentServicesPage_Loaded;
		Unloaded -= ComponentServicesPage_Unloaded;
		ComPlusApplications.Clear();
	}

	private async void ComponentServicesPage_Loaded(object sender, RoutedEventArgs e)
	{
		ManagementTools.Services.Logging.UiLogger.LogDebug("[ComponentServicesPage] Loaded.");
		if (_isInitialized)
		{
			return;
		}

		_isInitialized = true;
		CaptureUiElements();
		AttachEventHandlers();
		UpdateCurrentMachineInfo();
		await RefreshComPlusSummaryAsync();
	}

	private void CaptureUiElements()
	{
		_currentPcText = FindVisualChildren<TextBlock>(this)
			.FirstOrDefault(textBlock => textBlock.Text.Contains(Environment.MachineName));
		_comPlusExpander = FindVisualChildren<SettingsExpander>(this)
			.FirstOrDefault(expander => string.Equals(expander.Header?.ToString(), LocalizedStrings.ComExp_ComPlusApplications, StringComparison.Ordinal));
		_dcomConfigCard = FindSettingsCard(LocalizedStrings.ComExp_DcomConfig);
		_runningProcessesCard = FindSettingsCard(LocalizedStrings.ComExp_RunningProcesses);
		_transactionListCard = FindSettingsCard(LocalizedStrings.ComExp_Dtc_TransactionList);
		_transactionStatisticsCard = FindSettingsCard(LocalizedStrings.ComExp_Dtc_TransactionStatistics);

		var commandBar = FindVisualChildren<CommandBar>(this).FirstOrDefault();
		if (commandBar != null)
		{
			_refreshButton = FindVisualChildren<AppBarButton>(commandBar)
				.FirstOrDefault(button => string.Equals(button.Label, LocalizedStrings.ComExp_RefreshButton, StringComparison.Ordinal));
		}
	}

	private void AttachEventHandlers()
	{
		if (_refreshButton != null)
		{
			_refreshButton.Click += RefreshButton_Click;
		}

		AttachCardButton(_dcomConfigCard, DcomConfig_Click);
		AttachCardButton(_runningProcessesCard, RunningProcesses_Click);

		if (_transactionListCard != null)
		{
			_transactionListCard.Click += TransactionList_Click;
		}

		if (_transactionStatisticsCard != null)
		{
			_transactionStatisticsCard.Click += TransactionStatistics_Click;
		}
	}

	private void UpdateCurrentMachineInfo()
	{
		if (_currentPcText == null)
		{
			return;
		}

		_currentPcText.Text = string.Format(LocalizedStrings.ComExp_CurrentPC, Environment.MachineName);
		ManagementTools.Services.Logging.UiLogger.LogDebug("[ComponentServicesPage] Updated current PC label.");
	}

	private async Task RefreshComPlusSummaryAsync()
	{
		ManagementTools.Services.Logging.UiLogger.LogDebug("[ComponentServicesPage] Refreshing COM+ summary...");
		var applications = await _componentServicesService.GetComPlusApplicationsAsync();
		if (_comPlusExpander != null)
		{
			_comPlusExpander.Description = string.Format(LocalizedStrings.ComExp_ApplicationsCount, applications.Count);
		}

		ComPlusApplications.Clear();
		foreach (var app in applications)
		{
			app.Summary = FormatComPlusDescription(app);
			ComPlusApplications.Add(app);
		}
	}

    // OpenComexpLegacy_Click
	private async void OpenComexpLegacy_Click(object sender, RoutedEventArgs e)
	{
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "comexp.msc",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            var adminService = App.GetRequiredService<IAdminService>();
            if (adminService.IsPermissionError(ex))
            {
                await AdminDialogHelper.ShowAdminRequiredDialogAsync(this.XamlRoot);
            }
            else
            {
                ManagementTools.Services.Logging.UiLogger.LogDebug($"[ComponentServicesPage] Error opening comexp.msc: {ex.Message}");
            }
        }
    }

    private static string FormatComPlusDescription(ComPlusApplicationInfo app)
	{
		var idPart = string.IsNullOrWhiteSpace(app.Id) ? LocalizedStrings.Instance.ComExp_Format_NoId : app.Id;
		var authPart = string.IsNullOrWhiteSpace(app.AuthenticationLevel) ? LocalizedStrings.Instance.ComExp_Format_Unknown : app.AuthenticationLevel;
		return $"{idPart} | {authPart}";
	}

	private static void AttachCardButton(SettingsCard? card, RoutedEventHandler handler)
	{
		if (card == null)
		{
			return;
		}

		var button = FindVisualChildren<Button>(card).FirstOrDefault();
		if (button != null)
		{
			button.Click += handler;
		}
		else
		{
			card.Click += handler;
		}
	}

	private async void RefreshButton_Click(object sender, RoutedEventArgs e)
	{
		ManagementTools.Services.Logging.UiLogger.LogDebug("[ComponentServicesPage] Refresh requested.");
		await RefreshComPlusSummaryAsync();
	}

	private async void ComPlusApplicationProperties_Click(object sender, RoutedEventArgs e)
	{
		ManagementTools.Services.Logging.UiLogger.LogDebug("[ComponentServicesPage] COM+ Application Properties requested.");
		
		// Get the ComPlusApplicationInfo from the Tag property
		if (sender is not FrameworkElement element || element.Tag is not ComPlusApplicationInfo appInfo)
		{
			ManagementTools.Services.Logging.UiLogger.LogDebug("[ComponentServicesPage] Failed to get application info from Tag.");
			return;
		}

		// Create the dialog content
		var scrollViewer = new ScrollViewer
		{
			MaxHeight = 500,
			Padding = new Thickness(0, 8, 0, 0)
		};

		var detailsPanel = new StackPanel { Spacing = 12 };

		// Helper method to add property rows
		void AddPropertyRow(string label, string? value)
		{
			var grid = new Grid
			{
				ColumnDefinitions =
				{
					new ColumnDefinition { Width = new GridLength(140) },
					new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
				}
			};

			var labelBlock = new TextBlock
			{
				Text = label,
				Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
				VerticalAlignment = VerticalAlignment.Top
			};
			Grid.SetColumn(labelBlock, 0);

			var valueBlock = new TextBlock
			{
				Text = value ?? LocalizedStrings.ComExp_Dialog_NotSet,
				TextWrapping = TextWrapping.Wrap,
				IsTextSelectionEnabled = true,
				VerticalAlignment = VerticalAlignment.Top
			};
			Grid.SetColumn(valueBlock, 1);

			grid.Children.Add(labelBlock);
			grid.Children.Add(valueBlock);
			detailsPanel.Children.Add(grid);
		}

		// Add all properties
		AddPropertyRow(LocalizedStrings.ComExp_Dialog_Name, appInfo.Name);
		AddPropertyRow(LocalizedStrings.ComExp_Dialog_ID, appInfo.Id);
		AddPropertyRow(LocalizedStrings.ComExp_Dialog_Description, appInfo.Description);
		AddPropertyRow(LocalizedStrings.ComExp_Dialog_Activation, appInfo.Activation);
		AddPropertyRow(LocalizedStrings.ComExp_Dialog_Authentication, appInfo.AuthenticationLevel);
		AddPropertyRow(LocalizedStrings.ComExp_Dialog_AccessChecks, appInfo.AccessChecksLevel);
		AddPropertyRow(LocalizedStrings.ComExp_Dialog_Identity, appInfo.Identity);

		scrollViewer.Content = detailsPanel;

		// Create and show the dialog
		var dialog = new ContentDialog
		{
			Title = LocalizedStrings.ComExp_Dialog_ApplicationDetails,
			Content = scrollViewer,
			CloseButtonText = LocalizedStrings.ComExp_Dialog_Close,
            XamlRoot = XamlRoot,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            RequestedTheme = App.CurrentTheme
        };

		await dialog.ShowAsync();
	}

	private void DcomConfig_Click(object sender, RoutedEventArgs e)
	{
		ManagementTools.Services.Logging.UiLogger.LogDebug("[ComponentServicesPage] DCOM Config requested.");
		BreadcrumbNavigationService.AddBreadcrumb(LocalizedStrings.ComExp_Breadcrumb_DcomConfig ?? "DCOM Config", typeof(ComExp.DcomConfigPage));
		Frame?.Navigate(typeof(ComExp.DcomConfigPage), null, new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight });
	}

	private void RunningProcesses_Click(object sender, RoutedEventArgs e)
	{
		ManagementTools.Services.Logging.UiLogger.LogDebug("[ComponentServicesPage] Running processes requested.");
		BreadcrumbNavigationService.AddBreadcrumb(LocalizedStrings.ComExp_Breadcrumb_RunningProcesses ?? "Running Processes", typeof(ComExp.RunningProcessesPage));
		Frame?.Navigate(typeof(ComExp.RunningProcessesPage), null, new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight });
	}

	private void TransactionList_Click(object sender, RoutedEventArgs e)
	{
		ManagementTools.Services.Logging.UiLogger.LogDebug("[ComponentServicesPage] Transaction list requested.");
		BreadcrumbNavigationService.AddBreadcrumb(LocalizedStrings.ComExp_Breadcrumb_TransactionList ?? "Transaction List", typeof(ComExp.DtcTransactionListPage));
		Frame?.Navigate(typeof(ComExp.DtcTransactionListPage), null, new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight });
	}

	private void TransactionStatistics_Click(object sender, RoutedEventArgs e)
	{
		ManagementTools.Services.Logging.UiLogger.LogDebug("[ComponentServicesPage] Transaction statistics requested.");
		BreadcrumbNavigationService.AddBreadcrumb(LocalizedStrings.ComExp_Breadcrumb_TransactionStatistics ?? "Transaction Statistics", typeof(ComExp.DtcStatisticsPage));
		Frame?.Navigate(typeof(ComExp.DtcStatisticsPage), null, new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight });
	}

	private SettingsCard? FindSettingsCard(string header)
	{
		return FindVisualChildren<SettingsCard>(this)
			.FirstOrDefault(card => string.Equals(card.Header?.ToString(), header, StringComparison.OrdinalIgnoreCase));
	}

	private static System.Collections.Generic.IEnumerable<T> FindVisualChildren<T>(DependencyObject? parent) where T : DependencyObject
	{
		if (parent == null)
		{
			yield break;
		}

		var childCount = VisualTreeHelper.GetChildrenCount(parent);
		for (int i = 0; i < childCount; i++)
		{
			var child = VisualTreeHelper.GetChild(parent, i);
			if (child is T typedChild)
			{
				yield return typedChild;
			}

			foreach (var descendant in FindVisualChildren<T>(child))
			{
				yield return descendant;
			}
		}
	}
}
