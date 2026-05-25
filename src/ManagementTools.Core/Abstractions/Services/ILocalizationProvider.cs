namespace ManagementTools.Core.Abstractions.Services;

/// <summary>
/// Localization service abstraction interface.
/// The Core layer obtains localized strings through this interface, implemented by the UI layer.
/// </summary>
public interface ILocalizationProvider
{
    /// <summary>
    /// Get a localized string from the specified resource file.
    /// </summary>
    /// <param name="resourceFile">Resource file name</param>
    /// <param name="key">Resource key</param>
    /// <returns>Localized string</returns>
    string GetString(string resourceFile, string key);

    /// <summary>
    /// Get a localized string from the default resource file.
    /// </summary>
    /// <param name="key">Resource key</param>
    /// <returns>Localized string</returns>
    string GetString(string key);

    /// <summary>
    /// Get a formatted localized string.
    /// </summary>
    /// <param name="resourceFile">Resource file name</param>
    /// <param name="key">Resource key</param>
    /// <param name="args">Formatting arguments</param>
    /// <returns>Formatted localized string</returns>
    string GetFormattedString(string resourceFile, string key, params object[] args);
}
