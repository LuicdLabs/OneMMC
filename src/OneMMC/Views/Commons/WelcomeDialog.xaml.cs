using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OneMMC.Models;

namespace OneMMC.Views.Commons;

/// <summary>
/// A welcome dialog shown on first launch or when the user opts to be reminded later.
/// Provides an overview of the application and a way to delay the reminder.
/// </summary>
public sealed partial class WelcomeDialog : ContentDialog
{
    private readonly List<string> _featureStrings;

    /// <summary>
    /// Gets a value indicating whether the user chose to be reminded after 30 days.
    /// </summary>
    public bool RemindAfter30Days { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="WelcomeDialog"/> class.
    /// </summary>
    public WelcomeDialog()
    {
        InitializeComponent();

        var localized = Localization.LocalizedStrings.Instance;

        _featureStrings =
        [
            localized.WelcomeDialog_Feature1 ?? "Built with WinUI 3, featuring native Dark Mode support, high-DPI awareness, smooth motions, and modern Fluent Design UI/UX behaviors",
            localized.WelcomeDialog_Feature2 ?? "Designed following the Windows 11 design principles, with improved visual hierarchy, simplified workflows, and optimized touch/tablet experience",
            localized.WelcomeDialog_Feature3 ?? "Consolidates commonly used administrative tools (Services, Device Manager, Event Viewer, Disk Management, Local Users and Groups, and more) into a unified experience",
            localized.WelcomeDialog_Feature4 ?? "Built directly on Win32 APIs, COM, WMI, and CIM for low-level and high-performance system management integration",
            localized.WelcomeDialog_Feature5 ?? "Avoids unnecessary abstraction layers to preserve compatibility with existing Windows management infrastructure"
        ];

        FeatureList.ItemsSource = _featureStrings;

        RemindAfter30Days = false;
        RemindCheckBox.Checked += (_, _) => RemindAfter30Days = true;
        RemindCheckBox.Unchecked += (_, _) => RemindAfter30Days = false;
        PrimaryButtonClick += WelcomeDialog_PrimaryButtonClick;

        RequestedTheme = App.CurrentTheme;
        App.ThemeChanged += OnThemeChanged;

        Closing += WelcomeDialog_Closing;
    }

    private void WelcomeDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (RemindAfter30Days)
        {
            var settings = AppSettings.Load();
            settings.WelcomeDialogHidden = true;
            settings.WelcomeDialogDismissedDate = DateTime.Now.ToString("yyyy-MM-dd");
            settings.Save();
        }
    }

    private void WelcomeDialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        App.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged(ElementTheme theme)
    {
        RequestedTheme = theme;
    }
}
