// ModalDialogWindow.cs
//
// Purpose:
//   Provides a native WinUI 3 Window-based modal dialog that replaces the built-in
//   ContentDialog. Unlike ContentDialog, this implementation creates a real top-level
//   Win32/WinUI window so it can be used from any context (including non-XAML threads
//   or secondary windows) and supports Mica system backdrop, owner-window centering,
//   and async result awaiting via TaskCompletionSource.
//
// Architecture overview:
//   ModalDialogWindow  (extends Microsoft.UI.Xaml.Window)
//   ├── ModalDialogOptions  – immutable configuration bag passed at construction time
//   ├── BuildDialogRoot()   – constructs the XAML visual tree (content + button row)
//   ├── ConfigurePresenter()– sets OverlappedPresenter flags (modal, non-resizable …)
//   ├── ResizeAndCenter()   – DPI-scales the DIP size to physical pixels, clamps it to the
//   │                         target monitor's work area, and centers over the owner window
//   ├── ApplyTheme()        – propagates ElementTheme to the root grid and title bar
//   └── TaskCompletionSource<WindowDialogResult> – bridges the async Show/Close cycle

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using OneMMC.Interop;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Win32.Foundation;
using Win32PInvoke = Windows.Win32.PInvoke;
using Windows.Graphics;
using WinRT.Interop;

namespace OneMMC.Helpers;

/// <summary>
/// Represents the result returned when a <see cref="ModalDialogWindow"/> is dismissed.
/// Maps to the three standard dialog button roles used by WinUI ContentDialog.
/// </summary>
public enum WindowDialogResult
{
    /// <summary>The dialog was closed without an explicit button press (e.g. title-bar X).</summary>
    None,

    /// <summary>The user clicked the primary (affirmative / default) button.</summary>
    Primary,

    /// <summary>The user clicked the secondary (alternative) button.</summary>
    Secondary
}

/// <summary>
/// Immutable configuration object that fully describes a <see cref="ModalDialogWindow"/>
/// before it is constructed.  All layout, content, theming, and callback options are
/// expressed here so the window itself stays free of business logic.
/// </summary>
public sealed class ModalDialogOptions
{
    /// <summary>Window title bar text.</summary>
    public required string Title { get; init; }

    /// <summary>
    /// Body content placed inside the dialog.  Accepted types:
    /// <list type="bullet">
    ///   <item><see cref="UIElement"/> – rendered directly.</item>
    ///   <item><see cref="string"/> – wrapped in a <see cref="TextBlock"/>.</item>
    ///   <item>Any other object – <c>ToString()</c> is called and shown as text.</item>
    ///   <item><c>null</c> – an empty <see cref="Grid"/> placeholder is used.</item>
    /// </list>
    /// </summary>
    public object? Content { get; init; }

    /// <summary>
    /// XamlRoot of the owning window.  Used to derive the owner HWND when
    /// <see cref="OwnerHwnd"/> is not supplied directly.
    /// </summary>
    public XamlRoot? OwnerXamlRoot { get; init; }

    /// <summary>
    /// Win32 HWND of the owner window.  Takes precedence over
    /// <see cref="OwnerXamlRoot"/> when both are provided.
    /// </summary>
    public nint OwnerHwnd { get; init; }

    /// <summary>
    /// Initial theme applied to the dialog.  Defaults to
    /// <see cref="ElementTheme.Default"/>, which inherits the application theme.
    /// </summary>
    public ElementTheme RequestedTheme { get; init; } = ElementTheme.Default;

    /// <summary>
    /// Optional delegate used to subscribe to theme-change notifications from a
    /// custom source.  When <c>null</c>, the dialog subscribes to
    /// <see cref="App.ThemeChanged"/> instead.
    /// </summary>
    public Action<Action<ElementTheme>>? ThemeChangeSubscribe { get; init; }

    /// <summary>
    /// Paired unsubscribe delegate for <see cref="ThemeChangeSubscribe"/>.
    /// Called during <see cref="ModalDialogWindow.OnClosed"/> to prevent leaks.
    /// </summary>
    public Action<Action<ElementTheme>>? ThemeChangeUnsubscribe { get; init; }

    /// <summary>Label for the primary (accent-styled) button.  Omitted when <c>null</c> or whitespace.</summary>
    public string? PrimaryButtonText { get; init; }

    /// <summary>
    /// Initial enabled state of the primary button.  Defaults to <c>true</c>.  Set to <c>false</c> for
    /// dialogs that must validate before the affirmative action becomes available; update it later via
    /// <see cref="ModalDialogWindow.SetPrimaryButtonEnabled"/>.
    /// </summary>
    public bool IsPrimaryButtonInitiallyEnabled { get; init; } = true;

    /// <summary>Label for the secondary button.  Omitted when <c>null</c> or whitespace.</summary>
    public string? SecondaryButtonText { get; init; }

    /// <summary>Label for the close/cancel button.  Omitted when <c>null</c> or whitespace.</summary>
    public string? CloseButtonText { get; init; }

    /// <summary>
    /// Which button receives keyboard focus when the dialog opens.
    /// Defaults to <see cref="WindowDialogResult.Primary"/>.
    /// </summary>
    public WindowDialogResult DefaultButton { get; init; } = WindowDialogResult.Primary;

    /// <summary>
    /// Places the primary action before the close button in the footer when <c>true</c>.
    /// </summary>
    public bool IsPrimaryButtonLeading { get; init; }

    /// <summary>
    /// Initial dialog width in device-independent pixels (DIPs at 100% scale).
    /// Converted to physical pixels for the target monitor's DPI when the window is sized.
    /// </summary>
    public int Width { get; init; } = 640;

    /// <summary>
    /// Initial dialog height in device-independent pixels (DIPs at 100% scale).
    /// Converted to physical pixels for the target monitor's DPI when the window is sized.
    /// </summary>
    public int Height { get; init; } = 520;

    /// <summary>
    /// Optional validation callback invoked when the primary button is clicked.
    /// Return <c>false</c> to cancel the close (e.g. when form validation fails).
    /// </summary>
    public Func<bool>? OnPrimaryButtonClick { get; init; }

    /// <summary>
    /// Optional validation callback invoked when the secondary button is clicked.
    /// Return <c>false</c> to cancel the close.
    /// </summary>
    public Func<bool>? OnSecondaryButtonClick { get; init; }

    /// <summary>Optional side-effect callback invoked when the close button is clicked.</summary>
    public Action? OnCloseButtonClick { get; init; }
}

/// <summary>
/// A native WinUI 3 top-level window that behaves like a modal dialog.
///
/// <para><b>Why a Window instead of ContentDialog?</b><br/>
/// <see cref="Microsoft.UI.Xaml.Controls.ContentDialog"/> is hosted inside an existing
/// XAML island and therefore shares the same thread and visual tree as its parent.
/// This makes it impossible to show a dialog from a secondary window or from code that
/// does not have easy access to the parent's <see cref="XamlRoot"/>.
/// <see cref="ModalDialogWindow"/> creates a real Win32/WinUI window, which sidesteps
/// those constraints while still integrating with the WinUI theming and backdrop system.</para>
///
/// <para><b>Async result pattern</b><br/>
/// <see cref="ShowDialogAsync"/> returns a <see cref="Task{WindowDialogResult}"/> backed
/// by a <see cref="TaskCompletionSource{TResult}"/>.  The task completes only when the
/// window is fully closed, so callers can simply <c>await</c> the result.</para>
///
/// <para><b>Owner / modality</b><br/>
/// Win32 modality is achieved by setting the <c>GWLP_HWNDPARENT</c> window attribute via
/// <c>SetWindowLongPtr</c> / <c>SetWindowLong</c> and by configuring the
/// <see cref="OverlappedPresenter"/> with <c>IsModal = true</c>.  This prevents the owner
/// window from receiving input while the dialog is open.</para>
///
/// <para><b>Mica backdrop</b><br/>
/// When the OS supports it (<see cref="MicaController.IsSupported"/>), a
/// <see cref="MicaBackdrop"/> is applied and the root grid background is set to
/// transparent so the Mica material shows through.  On older OS versions a solid
/// fallback color is used instead.</para>
///
/// <para><b>Theme synchronization</b><br/>
/// The dialog subscribes to theme-change events (either via the caller-supplied
/// <see cref="ModalDialogOptions.ThemeChangeSubscribe"/> delegate or the global
/// <see cref="App.ThemeChanged"/> event) and re-applies the theme to both the XAML
/// visual tree and the AppWindow title bar on every change.</para>
/// </summary>
public sealed class ModalDialogWindow : Window
{
    // Tracks all currently open ModalDialogWindows so callers can enumerate them if needed.
    private static readonly HashSet<ModalDialogWindow> ActiveWindows = [];

    private readonly ModalDialogOptions _options;

    // Bridges the async Show/Close lifecycle: the task returned by ShowDialogAsync()
    // completes when _tcs.TrySetResult() is called inside OnClosed().
    private readonly TaskCompletionSource<WindowDialogResult> _tcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Root XAML element that holds the content area and the button row.
    private readonly FrameworkElement _dialogRoot;

    // Button references kept so FocusDefaultButton() can target the correct control.
    private readonly Button? _primaryButton;
    private readonly Button? _secondaryButton;
    private readonly Button? _closeButton;

    // Stored so it can be unsubscribed in OnClosed() without capturing a lambda.
    private readonly Action<ElementTheme> _themeHandler;

    // Cached to avoid repeated P/Invoke calls when updating the background brush.
    private readonly bool _isMicaBackdropEnabled;

    // Win32 HWND of the owner window; zero when there is no owner.
    private readonly nint _ownerHwnd;

    // Holds the result until the window closes and the TCS is resolved.
    private WindowDialogResult _result = WindowDialogResult.None;

    /// <summary>
    /// Initializes a new <see cref="ModalDialogWindow"/> from the supplied options.
    ///
    /// Construction order:
    /// <list type="number">
    ///   <item>Resolve the owner HWND (from options or the main window).</item>
    ///   <item>Build the XAML visual tree via <see cref="BuildDialogRoot"/>.</item>
    ///   <item>Attempt to enable Mica backdrop.</item>
    ///   <item>Apply the initial theme and subscribe to future theme changes.</item>
    ///   <item>Configure the <see cref="OverlappedPresenter"/> (modal, non-resizable).</item>
    ///   <item>Resize and center the window over the owner.</item>
    ///   <item>Register the <see cref="OnClosed"/> handler.</item>
    /// </list>
    /// </summary>
    public ModalDialogWindow(ModalDialogOptions options)
    {
        _options = options;
        _ownerHwnd = ResolveOwnerHwnd(options);
        Title = options.Title;

        (_dialogRoot, _primaryButton, _secondaryButton, _closeButton) = BuildDialogRoot();
        Content = _dialogRoot;
        _isMicaBackdropEnabled = TryConfigureSystemBackdrop();

        // Cache the delegate so the same instance can be unsubscribed later.
        _themeHandler = ApplyTheme;
        ApplyTheme(options.RequestedTheme);
        if (options.ThemeChangeSubscribe is not null)
        {
            options.ThemeChangeSubscribe.Invoke(_themeHandler);
        }
        else
        {
            App.ThemeChanged += _themeHandler;
        }

        ConfigurePresenter();
        ResizeAndCenter();
        Closed += OnClosed;
    }

    /// <summary>
    /// Makes the dialog visible, activates it, and returns a task that completes
    /// with the <see cref="WindowDialogResult"/> when the window is closed.
    ///
    /// <para>The method enqueues <see cref="FocusDefaultButton"/> on the dispatcher
    /// queue so that focus is set after the XAML layout pass has finished.</para>
    /// </summary>
    public Task<WindowDialogResult> ShowDialogAsync()
    {
        ActiveWindows.Add(this);
        AppWindow.Show();
        Activate();
        ApplyTheme(_options.RequestedTheme);
        DispatcherQueue.TryEnqueue(FocusDefaultButton);
        return _tcs.Task;
    }

    /// <summary>
    /// Enables or disables the primary (affirmative) button after the dialog is shown.  No-op when the
    /// dialog has no primary button.  Use this to gate the affirmative action on live form validation.
    /// </summary>
    /// <param name="enabled">Whether the primary button should be clickable.</param>
    public void SetPrimaryButtonEnabled(bool enabled)
    {
        if (_primaryButton is not null)
        {
            _primaryButton.IsEnabled = enabled;
        }
    }

    /// <summary>
    /// Constructs the two-row XAML layout:
    /// <list type="bullet">
    ///   <item>Row 0 (star height) – content area wrapped in a padded <see cref="Border"/>.</item>
    ///   <item>Row 1 (auto height) – button row, only added when at least one button is configured.</item>
    /// </list>
    /// Returns the root grid and nullable button references for later focus management.
    /// </summary>
    private (FrameworkElement root, Button? primary, Button? secondary, Button? close) BuildDialogRoot()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var contentBorder = new Border
        {
            Padding = new Thickness(20),
            Child = CreateContentPresenter()
        };
        grid.Children.Add(contentBorder);

        var (buttonRow, primary, secondary, close) = BuildButtonRow();
        if (buttonRow is not null)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(buttonRow, 1);
            grid.Children.Add(buttonRow);
        }

        return (grid, primary, secondary, close);
    }

    /// <summary>
    /// Converts <see cref="ModalDialogOptions.Content"/> to a renderable
    /// <see cref="UIElement"/> and ensures oversized content can be reached by mouse wheel:
    /// <list type="bullet">
    ///   <item><see cref="UIElement"/> – rendered directly or placed in the dialog scroll host.</item>
    ///   <item><see cref="string"/> – wrapped in a wrapping <see cref="TextBlock"/>.</item>
    ///   <item><c>null</c> – returns an empty <see cref="Grid"/> placeholder.</item>
    ///   <item>Anything else – <c>ToString()</c> result shown in a <see cref="TextBlock"/>.</item>
    /// </list>
    /// </summary>
    private UIElement CreateContentPresenter()
    {
        UIElement content = _options.Content switch
        {
            UIElement element => element,
            string text => new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
            null => new Grid(),
            var obj => new TextBlock { Text = obj.ToString(), TextWrapping = TextWrapping.Wrap }
        };

        if (content is ScrollViewer or UserControl)
        {
            return content;
        }

        return new ScrollViewer
        {
            Content = content,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Enabled
        };
    }

    /// <summary>
    /// Builds the horizontal button row at the bottom of the dialog.
    ///
    /// <para>Button order (left to right): Secondary → Close → Primary.
    /// This mirrors the WinUI ContentDialog layout where the accent button is
    /// rightmost, giving it visual prominence.</para>
    ///
    /// <para>Each button's click handler first invokes the corresponding
    /// <see cref="ModalDialogOptions"/> callback (if any).  When the callback returns
    /// <c>false</c> the close is cancelled, allowing callers to perform validation
    /// before the dialog is dismissed.</para>
    ///
    /// Returns <c>null</c> for the container when no button text is configured,
    /// which suppresses the entire button row.
    /// </summary>
    private (Border? container, Button? primary, Button? secondary, Button? close) BuildButtonRow()
    {
        bool hasPrimary = !string.IsNullOrWhiteSpace(_options.PrimaryButtonText);
        bool hasSecondary = !string.IsNullOrWhiteSpace(_options.SecondaryButtonText);
        bool hasClose = !string.IsNullOrWhiteSpace(_options.CloseButtonText);

        if (!hasPrimary && !hasSecondary && !hasClose)
        {
            return (null, null, null, null);
        }

        var panel = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ColumnSpacing = 8
        };

        Button? secondary = null;
        Button? close = null;
        Button? primary = null;
        int columnIndex = 0;

        void AddButton(Button button)
        {
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(button, columnIndex++);
            panel.Children.Add(button);
        }

        Button CreatePrimaryButton()
        {
            var button = new Button
            {
                Content = _options.PrimaryButtonText,
                Style = (Style)Application.Current.Resources["AccentButtonStyle"],
                HorizontalAlignment = HorizontalAlignment.Stretch,
                IsEnabled = _options.IsPrimaryButtonInitiallyEnabled
            };
            button.Click += (_, _) =>
            {
                // Allow the caller to cancel the close (e.g. form validation failure).
                if (_options.OnPrimaryButtonClick?.Invoke() == false)
                {
                    return;
                }

                CompleteWith(WindowDialogResult.Primary);
            };

            primary = button;
            return button;
        }

        Button CreateSecondaryButton()
        {
            var button = new Button
            {
                Content = _options.SecondaryButtonText,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            button.Click += (_, _) =>
            {
                // Allow the caller to cancel the close (e.g. unsaved changes guard).
                if (_options.OnSecondaryButtonClick?.Invoke() == false)
                {
                    return;
                }

                CompleteWith(WindowDialogResult.Secondary);
            };

            secondary = button;
            return button;
        }

        Button CreateCloseButton()
        {
            var button = new Button
            {
                Content = _options.CloseButtonText,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            button.Click += (_, _) =>
            {
                _options.OnCloseButtonClick?.Invoke();
                CompleteWith(WindowDialogResult.None);
            };

            close = button;
            return button;
        }

        if (_options.IsPrimaryButtonLeading && hasPrimary)
        {
            AddButton(CreatePrimaryButton());
        }

        if (hasSecondary)
        {
            AddButton(CreateSecondaryButton());
        }

        if (hasClose)
        {
            AddButton(CreateCloseButton());
        }

        if (hasPrimary && !_options.IsPrimaryButtonLeading)
        {
            AddButton(CreatePrimaryButton());
        }

        var container = new Border
        {
            Padding = new Thickness(20, 0, 20, 20),
            Child = panel
        };

        return (container, primary, secondary, close);
    }

    /// <summary>
    /// Configures the <see cref="OverlappedPresenter"/> to match standard dialog behavior:
    /// <list type="bullet">
    ///   <item>Modal when an owner window is available.</item>
    ///   <item>Non-resizable, non-maximizable, non-minimizable.</item>
    /// </list>
    /// </summary>
    private void ConfigurePresenter()
    {
        var presenter = OverlappedPresenter.CreateForDialog();
        bool hasOwner = SetWindowOwner();
        presenter.IsModal = hasOwner;
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;

        AppWindow.SetPresenter(presenter);
    }

    /// <summary>
    /// Sizes the <see cref="AppWindow"/> and centers it over the owner window.
    ///
    /// <para><see cref="ModalDialogOptions.Width"/>/<see cref="ModalDialogOptions.Height"/> are
    /// DIPs (100%-scale design values), while <see cref="AppWindow"/>.Resize takes physical
    /// pixels; the size is therefore multiplied by the DPI scale of the monitor hosting the
    /// owner (PerMonitorV2: WinUI scales the XAML content automatically, but never the window
    /// frame). The still-hidden dialog is first moved onto the owner's monitor so the later
    /// centering move cannot cross a DPI boundary and re-scale the just-set size via the
    /// framework's WM_DPICHANGED handling, and the result is clamped to that monitor's work
    /// area so oversized dialogs stay on screen.</para>
    ///
    /// <para>Centering algorithm:<br/>
    /// <c>x = ownerLeft + max(0, (ownerWidth  - dialogWidth)  / 2)</c><br/>
    /// <c>y = ownerTop  + max(0, (ownerHeight - dialogHeight) / 2)</c><br/>
    /// The <c>max(0, …)</c> guard prevents negative coordinates when the dialog is wider or
    /// taller than the owner; the final position is clamped into the work area.</para>
    ///
    /// When no owner HWND is available the window keeps its default OS position and uses the
    /// scale of whatever monitor hosts it.
    /// </summary>
    private void ResizeAndCenter()
    {
        // Resolve the owner AppWindow first: it anchors the target monitor and centering.
        AppWindow? ownerAppWindow = null;
        if (_ownerHwnd != nint.Zero)
        {
            var ownerWindowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_ownerHwnd);
            ownerAppWindow = AppWindow.GetFromWindowId(ownerWindowId);
        }

        // Land the still-hidden dialog on the owner's monitor BEFORE sizing so a later
        // cross-monitor move cannot re-scale the explicit size via WM_DPICHANGED handling.
        if (ownerAppWindow is not null)
        {
            AppWindow.Move(ownerAppWindow.Position);
        }

        // Options.Width/Height are DIPs (100%-scale design values); convert to physical
        // pixels for the monitor that will host the dialog (owner's monitor when owned).
        nint scaleHwnd = _ownerHwnd != nint.Zero
            ? _ownerHwnd
            : Microsoft.UI.Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        double scale = DpiScaleHelper.GetScaleForWindow(scaleHwnd);
        int width = (int)Math.Round(_options.Width * scale);
        int height = (int)Math.Round(_options.Height * scale);

        // Clamp to the hosting monitor's work area so oversized dialogs stay on screen.
        var anchorWindowId = ownerAppWindow?.Id ?? AppWindow.Id;
        var displayArea = DisplayArea.GetFromWindowId(anchorWindowId, DisplayAreaFallback.Nearest);
        RectInt32? workArea = displayArea?.WorkArea;
        if (workArea is RectInt32 sizeBounds)
        {
            width = Math.Min(width, sizeBounds.Width);
            height = Math.Min(height, sizeBounds.Height);
        }

        AppWindow.Resize(new SizeInt32(width, height));

        if (ownerAppWindow is null)
        {
            return;
        }

        int x = ownerAppWindow.Position.X
              + Math.Max(0, (ownerAppWindow.Size.Width - AppWindow.Size.Width) / 2);
        int y = ownerAppWindow.Position.Y
              + Math.Max(0, (ownerAppWindow.Size.Height - AppWindow.Size.Height) / 2);

        // Keep the dialog fully inside the work area when the owner sits near a screen edge.
        if (workArea is RectInt32 positionBounds)
        {
            x = Math.Clamp(x, positionBounds.X,
                positionBounds.X + Math.Max(0, positionBounds.Width - AppWindow.Size.Width));
            y = Math.Clamp(y, positionBounds.Y,
                positionBounds.Y + Math.Max(0, positionBounds.Height - AppWindow.Size.Height));
        }

        AppWindow.Move(new PointInt32(x, y));
    }

    /// <summary>
    /// Moves keyboard focus to the button that corresponds to
    /// <see cref="ModalDialogOptions.DefaultButton"/>.
    ///
    /// <para>This method is dispatched via <see cref="DispatcherQueue.TryEnqueue"/>
    /// so it runs after the XAML layout pass completes and the buttons are fully
    /// realized in the visual tree.</para>
    /// </summary>
    private void FocusDefaultButton()
    {
        var target = _options.DefaultButton switch
        {
            WindowDialogResult.Primary => _primaryButton,
            WindowDialogResult.Secondary => _secondaryButton,
            WindowDialogResult.None => _closeButton,
            _ => _primaryButton
        };

        target?.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// Applies <paramref name="theme"/> to the XAML visual tree and the AppWindow title bar.
    ///
    /// <para>Steps:
    /// <list type="number">
    ///   <item>Resolve <see cref="ElementTheme.Default"/> to a concrete Light/Dark value
    ///         using <see cref="ResolveTheme"/>.</item>
    ///   <item>Set <c>RequestedTheme</c> on the root <see cref="FrameworkElement"/> so all
    ///         child controls inherit the correct resource dictionary.</item>
    ///   <item>Update the root grid background (transparent for Mica, solid fallback otherwise).</item>
    ///   <item>Sync the <see cref="TitleBar.PreferredTheme"/> so the caption buttons
    ///         (minimize / maximize / close) match the content area.</item>
    /// </list>
    /// </para>
    /// </summary>
    private void ApplyTheme(ElementTheme theme)
    {
        ElementTheme resolvedTheme = ResolveTheme(theme);
        _dialogRoot.RequestedTheme = resolvedTheme;

        if (_dialogRoot is Grid rootGrid)
        {
            rootGrid.Background = ResolveDialogBackgroundBrush(resolvedTheme, _isMicaBackdropEnabled);
        }

        if (AppWindow?.TitleBar is { } titleBar)
        {
            titleBar.PreferredTheme = resolvedTheme switch
            {
                ElementTheme.Light => TitleBarTheme.Light,
                ElementTheme.Dark => TitleBarTheme.Dark,
                _ => TitleBarTheme.UseDefaultAppMode
            };
        }
    }

    /// <summary>
    /// Resolves <see cref="ElementTheme.Default"/> to the application's current theme.
    /// If the app theme is also <see cref="ElementTheme.Default"/> the value is returned
    /// as-is and WinUI will resolve it at render time.
    /// </summary>
    private static ElementTheme ResolveTheme(ElementTheme theme)
    {
        if (theme != ElementTheme.Default)
        {
            return theme;
        }

        return App.CurrentTheme == ElementTheme.Default ? ElementTheme.Default : App.CurrentTheme;
    }

    /// <summary>
    /// Attempts to enable the Mica system backdrop.
    ///
    /// <para>Mica is only available on Windows 11 (build 22000+).
    /// <see cref="MicaController.IsSupported"/> performs the OS version check.
    /// When unsupported, <see cref="Window.SystemBackdrop"/> is explicitly set to
    /// <c>null</c> and the method returns <c>false</c> so callers know to use a
    /// solid background brush instead.</para>
    /// </summary>
    /// <returns><c>true</c> if Mica was successfully configured; otherwise <c>false</c>.</returns>
    private bool TryConfigureSystemBackdrop()
    {
        if (!MicaController.IsSupported())
        {
            SystemBackdrop = null;
            return false;
        }

        SystemBackdrop = new MicaBackdrop { Kind = MicaKind.Base };
        return true;
    }

    /// <summary>
    /// Returns the appropriate background <see cref="Brush"/> for the dialog root grid.
    ///
    /// <para>When Mica is active the brush is fully transparent so the backdrop material
    /// is visible.  When Mica is unavailable a solid color is used:
    /// <list type="bullet">
    ///   <item>Dark theme → <c>#202020</c> (matches the WinUI dark surface color).</item>
    ///   <item>Light theme → <see cref="Colors.White"/>.</item>
    /// </list>
    /// </para>
    /// </summary>
    private static Brush ResolveDialogBackgroundBrush(ElementTheme theme, bool isMicaBackdropEnabled)
    {
        if (isMicaBackdropEnabled)
        {
            return new SolidColorBrush(Colors.Transparent);
        }

        ElementTheme effectiveTheme = theme == ElementTheme.Default
            ? (Application.Current.RequestedTheme == ApplicationTheme.Dark ? ElementTheme.Dark : ElementTheme.Light)
            : theme;

        return effectiveTheme switch
        {
            ElementTheme.Dark => new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x20, 0x20, 0x20)),
            _ => new SolidColorBrush(Colors.White)
        };
    }

    /// <summary>
    /// Sets the Win32 owner of this dialog window using <c>GWLP_HWNDPARENT</c>.
    ///
    /// <para>Setting the owner HWND achieves two things:
    /// <list type="number">
    ///   <item>The dialog stays on top of the owner window.</item>
    ///   <item>Combined with <see cref="OverlappedPresenter.IsModal"/>, it blocks
    ///         mouse and keyboard input to the owner while the dialog is open.</item>
    /// </list>
    /// </para>
    ///
    /// </summary>
    /// <returns><c>true</c> if the owner was set successfully; otherwise <c>false</c>.</returns>
    private bool SetWindowOwner()
    {
        if (_ownerHwnd == nint.Zero)
        {
            return false;
        }

        nint ownedHwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        if (ownedHwnd == nint.Zero)
        {
            return false;
        }

        WindowLongNativeMethods.SetOwnerWindow(ownedHwnd, _ownerHwnd);

        return true;
    }

    /// <summary>
    /// Records the intended <paramref name="result"/> and closes the window.
    /// The actual TCS resolution happens in <see cref="OnClosed"/> to ensure
    /// the task completes only after the window is fully torn down.
    /// </summary>
    private void CompleteWith(WindowDialogResult result)
    {
        _result = result;
        Close();
    }

    /// <summary>
    /// Handles the <see cref="Window.Closed"/> event.
    ///
    /// <para>Cleanup sequence:
    /// <list type="number">
    ///   <item>Unsubscribe from theme-change events to prevent memory leaks.</item>
    ///   <item>Remove this instance from <see cref="ActiveWindows"/>.</item>
    ///   <item>Restore focus to the owner window via <c>SetForegroundWindow</c>.</item>
    ///   <item>Resolve the <see cref="TaskCompletionSource{TResult}"/> with the
    ///         recorded <see cref="_result"/>, unblocking any awaiting caller.</item>
    /// </list>
    /// </para>
    /// </summary>
    private void OnClosed(object sender, WindowEventArgs args)
    {
        Closed -= OnClosed;
        if (_options.ThemeChangeUnsubscribe is not null)
        {
            _options.ThemeChangeUnsubscribe.Invoke(_themeHandler);
        }
        else
        {
            App.ThemeChanged -= _themeHandler;
        }
        ActiveWindows.Remove(this);

        // Return focus to the owner so the user can continue working immediately.
        if (_ownerHwnd != nint.Zero)
        {
            Win32PInvoke.SetForegroundWindow(new HWND(_ownerHwnd));
        }

        _tcs.TrySetResult(_result);
    }

    /// <summary>
    /// Determines the owner HWND using the following priority order:
    /// <list type="number">
    ///   <item><see cref="ModalDialogOptions.OwnerHwnd"/> – explicit HWND, highest priority.</item>
    ///   <item><see cref="ModalDialogOptions.OwnerXamlRoot"/> – derives the HWND from the
    ///         <c>ContentIslandEnvironment.AppWindowId</c>.</item>
    ///   <item><see cref="App.MainWindowInstance"/> – falls back to the application's main window.</item>
    ///   <item><see cref="nint.Zero"/> – no owner; the dialog will be unowned and non-modal.</item>
    /// </list>
    /// </summary>
    private static nint ResolveOwnerHwnd(ModalDialogOptions options)
    {
        if (options.OwnerHwnd != nint.Zero)
        {
            return options.OwnerHwnd;
        }

        if (options.OwnerXamlRoot?.ContentIslandEnvironment is { } env)
        {
            return Microsoft.UI.Win32Interop.GetWindowFromWindowId(env.AppWindowId);
        }

        return App.MainWindowInstance is null
            ? nint.Zero
            : WindowNative.GetWindowHandle(App.MainWindowInstance);
    }

}
