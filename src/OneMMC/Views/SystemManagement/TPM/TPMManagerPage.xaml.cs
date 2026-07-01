using System;
using System.Threading.Tasks;
using OneMMC.Core.Features.SystemManagement.ViewModels.TPM;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OneMMC.Helpers;
using OneMMC.Localization;
using OneMMC.Core.Features.SystemManagement.Services.TPM;

namespace OneMMC.Views;

public sealed partial class TPMManagerPage : Page
{
	public LocalizedStrings LocalizedStrings { get; } = LocalizedStrings.Instance;
	public TPMManagerViewModel ViewModel { get; }
	private readonly TPMService _tpmService;

	public TPMManagerPage()
	{
		ViewModel = App.GetRequiredService<TPMManagerViewModel>();
		_tpmService = App.GetRequiredService<TPMService>();
		InitializeComponent();
		
		// Defer ViewModel initialization until the page is loaded so XamlRoot is available
		this.Loaded += TPMManagerPage_Loaded;
	}

	private void TPMManagerPage_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
	{
		this.Loaded -= TPMManagerPage_Loaded;
	}

	private async void ClearTPM_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			// Pre-flight admin check
			if (!App.GetRequiredService<IAdminService>().IsRunningAsAdmin)
			{
				await AdminDialogHelper.ShowAdminRequiredDialogAsync(this.XamlRoot);
				return;
			}

			if (this.XamlRoot == null)
			{
				ShowStatus(InfoBarSeverity.Error, LocalizedStrings.TPM_Error, LocalizedStrings.TPM_NoXamlRootError);
				return;
			}

			var dialog = new ContentDialog
			{
				Title = LocalizedStrings.TPM_ClearTPMTitle,
				Content = LocalizedStrings.TPM_ClearTPMConfirmMessage,
				PrimaryButtonText = LocalizedStrings.TPM_ClearTPMConfirmPrimary,
				CloseButtonText = LocalizedStrings.TPM_ClearTPMConfirmCancel,
				DefaultButton = ContentDialogButton.Close,
				XamlRoot = this.XamlRoot,
				Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
				RequestedTheme = App.CurrentTheme
			};

			var result = await dialog.ShowAsync();
			if (result == ContentDialogResult.Primary)
			{
				ShowStatus(InfoBarSeverity.Warning, LocalizedStrings.TPM_ClearTPMTitle, LocalizedStrings.TPM_ClearTPMStarted);

				var clearResult = _tpmService.ClearTPM();

				if (clearResult.Success)
				{
					ShowStatus(InfoBarSeverity.Success, LocalizedStrings.TPM_Success, LocalizedStrings.TPM_ClearTPMSucceeded);
				}
				else
				{
					switch (clearResult.Status)
					{
						case TPMService.ClearStatus.RequiresAdmin:
							ShowStatus(InfoBarSeverity.Error, LocalizedStrings.TPM_Error, LocalizedStrings.TPM_ClearTPMRequiresAdmin);
							break;
						case TPMService.ClearStatus.NotFoundObject:
							ShowStatus(InfoBarSeverity.Error, LocalizedStrings.TPM_Error, LocalizedStrings.TPM_ClearTPM_NoWin32Tpm);
							break;
						case TPMService.ClearStatus.NoSuitableMethod:
							ShowStatus(InfoBarSeverity.Error, LocalizedStrings.TPM_Error, LocalizedStrings.TPM_ClearTPM_NoMethod);
							break;
						case TPMService.ClearStatus.NeedsParameters:
							ShowStatus(InfoBarSeverity.Error, LocalizedStrings.TPM_Error, LocalizedStrings.TPM_ClearTPM_NeedsParameters);
							break;
						case TPMService.ClearStatus.InvocationFailed:
						case TPMService.ClearStatus.Unknown:
						default:
							if (!string.IsNullOrWhiteSpace(clearResult.ErrorMessage))
							{
								try
								{
									var text = string.Format(LocalizedStrings.TPM_ClearTPM_InvokeFailed, clearResult.ErrorMessage);
									ShowStatus(InfoBarSeverity.Error, LocalizedStrings.TPM_Error, text);
								}
								catch
								{
									ShowStatus(InfoBarSeverity.Error, LocalizedStrings.TPM_Error, clearResult.ErrorMessage);
								}
							}
							else
							{
								ShowStatus(InfoBarSeverity.Error, LocalizedStrings.TPM_Error, LocalizedStrings.TPM_ClearTPMError);
							}
							break;
					}
				}
			}
		}
		catch (Exception ex)
		{
			ShowStatus(InfoBarSeverity.Error, LocalizedStrings.TPM_Error, $"{LocalizedStrings.TPM_ClearTPMError}: {ex.Message}");
		}
	}

	private void ShowStatus(InfoBarSeverity severity, string title, string message)
	{
		ViewModel.StatusSeverity = severity switch
		{
			InfoBarSeverity.Success => TpmStatusSeverity.Success,
			InfoBarSeverity.Warning => TpmStatusSeverity.Warning,
			InfoBarSeverity.Error => TpmStatusSeverity.Error,
			_ => TpmStatusSeverity.Informational
		};
		ViewModel.StatusTitle = title;
		ViewModel.StatusMessage = message;
		ViewModel.ShowStatusMessage = true;

		_ = HideStatusAfterDelayAsync();
	}

	private async Task HideStatusAfterDelayAsync()
	{
		await Task.Delay(5000);
		ViewModel.ShowStatusMessage = false;
	}

	public SolidColorBrush HexToBrush(string hex)
	{
		try
		{
			if (hex?.Length == 7 && hex.StartsWith("#"))
			{
				byte r = Convert.ToByte(hex.Substring(1, 2), 16);
				byte g = Convert.ToByte(hex.Substring(3, 2), 16);
				byte b = Convert.ToByte(hex.Substring(5, 2), 16);
				return new SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b));
			}
		}
		catch { }
		return new SolidColorBrush(Colors.Transparent);
	}
}
