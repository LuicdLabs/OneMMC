using System.Threading.Tasks;
using ManagementTools.Core.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ManagementTools.Helpers;

/// <summary>The user's choice in the unsaved-changes prompt shown on back-navigation.</summary>
public enum UnsavedChangesChoice
{
    /// <summary>Save the pending edits, then continue navigating.</summary>
    Save,

    /// <summary>Discard the pending edits and continue navigating.</summary>
    Discard,

    /// <summary>Stay on the page (abort the navigation).</summary>
    Cancel,
}

/// <summary>
/// Shows the shared "you have unsaved changes" Save / Don't Save / Cancel dialog. Pages with editable
/// content show this from <c>OnNavigatingFrom</c> when they have pending edits, so leaving the page
/// (back button, breadcrumb, etc.) does not silently discard the user's changes.
/// </summary>
public static class UnsavedChangesPrompt
{
    /// <summary>Prompts the user and returns their choice.</summary>
    public static async Task<UnsavedChangesChoice> ShowAsync(XamlRoot xamlRoot)
    {
        var dialog = new ContentDialog
        {
            Title = L(CommonKeys.UnsavedChangesTitle),
            Content = L(CommonKeys.UnsavedChangesMessage),
            PrimaryButtonText = L(CommonKeys.SaveButton),
            SecondaryButtonText = L(CommonKeys.DontSaveButton),
            CloseButtonText = L(CommonKeys.CancelButton),
            DefaultButton = ContentDialogButton.Primary,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            XamlRoot = xamlRoot,
            RequestedTheme = App.CurrentTheme,
        };

        return await dialog.ShowAsync() switch
        {
            ContentDialogResult.Primary => UnsavedChangesChoice.Save,
            ContentDialogResult.Secondary => UnsavedChangesChoice.Discard,
            _ => UnsavedChangesChoice.Cancel,
        };
    }

    private static string L(string key) =>
        LocalizationProvider.Current.GetString(ResourceFileNames.Common, key);
}
