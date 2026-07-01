namespace OneMMC.Core.Localization;

/// <summary>
/// Fallback localization provider used before the UI layer registers its implementation.
/// Returns a bracketed placeholder so missing registrations are immediately visible.
/// </summary>
internal sealed class DefaultLocalizationProvider : ILocalizationProvider
{
    public static ILocalizationProvider Instance { get; } = new DefaultLocalizationProvider();

    private DefaultLocalizationProvider() { }

    /// <inheritdoc/>
    public string GetString(string resourceFile, string key) => $"[{resourceFile}/{key}]";

    /// <inheritdoc/>
    public string GetString(string key) => $"[{key}]";

    /// <inheritdoc/>
    public string GetFormattedString(string resourceFile, string key, params object[] args)
    {
        try
        {
            return string.Format(GetString(resourceFile, key), args);
        }
        catch
        {
            return GetString(resourceFile, key);
        }
    }
}

/// <summary>
/// Service-locator for the Core layer's localization provider.
/// The UI layer calls <see cref="Initialize"/> once at startup to register its implementation.
/// All Core code accesses strings through <see cref="Current"/>.
/// </summary>
public static class LocalizationProvider
{
    private static ILocalizationProvider _current = DefaultLocalizationProvider.Instance;

    /// <summary>
    /// Gets or sets the active localization provider.
    /// Setting to <see langword="null"/> resets to the default placeholder provider.
    /// </summary>
    public static ILocalizationProvider Current
    {
        get => _current;
        set => _current = value ?? DefaultLocalizationProvider.Instance;
    }

    /// <summary>
    /// Registers the UI-layer localization provider. Called once during application startup.
    /// </summary>
    /// <param name="provider">The provider to register; <see langword="null"/> resets to the default.</param>
    public static void Initialize(ILocalizationProvider provider)
    {
        _current = provider ?? DefaultLocalizationProvider.Instance;
    }
}
