using OneMMC.Core.Localization;

namespace OneMMC.Localization;

/// <summary>
/// UI layer localization provider implementation
/// </summary>
public class UILocalizationProvider : ILocalizationProvider
{
    private static UILocalizationProvider? _instance;
    private static readonly object _lock = new();

    /// <summary>
    /// Singleton instance of the UI localization provider
    /// </summary>
    public static UILocalizationProvider Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new UILocalizationProvider();
                }
            }
            return _instance;
        }
    }

    private UILocalizationProvider() { }

    /// <inheritdoc/>
    public string GetString(string resourceFile, string key)
    {
        return LocalizationService.Instance.GetString(resourceFile, key);
    }

    /// <inheritdoc/>
    public string GetString(string key)
    {
        return LocalizationService.Instance.GetString(key);
    }

    /// <inheritdoc/>
    public string GetFormattedString(string resourceFile, string key, params object[] args)
    {
        return LocalizationService.Instance.GetFormattedString(resourceFile, key, args);
    }

    /// <summary>
    /// Initializes the Core localization provider.
    /// </summary>
    public static void InitializeCoreLocalization()
    {
        LocalizationProvider.Initialize(Instance);
    }
}
