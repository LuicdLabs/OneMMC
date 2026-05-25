using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ManagementTools.Core.Features.UserSecurity.Services.SecPol.SystemAudit;

internal sealed class SystemAuditExplainTextProvider
{
    private const int MaxAuditPolicyResourceId = 2000;
    private const string AuditPolicyMessageDllName = "auditpolmsg.dll";
    private const string AuditPolicyMessageMuiName = "auditpolmsg.dll.mui";

    private static readonly IReadOnlyDictionary<int, Guid[]> ResourceIdSubcategoryAliases =
        new Dictionary<int, Guid[]>
        {
            [706] = [new Guid("0cce9248-69ae-11d9-bed3-505054503030")],
            [707] = [new Guid("0cce924a-69ae-11d9-bed3-505054503030")],
            [770] = [new Guid("0cce9225-69ae-11d9-bed3-505054503030")],
            [771] = [new Guid("0cce9226-69ae-11d9-bed3-505054503030")],
            [775] = [new Guid("0cce9246-69ae-11d9-bed3-505054503030")],
            [793] = [new Guid("0cce923c-69ae-11d9-bed3-505054503030")]
        };

    private readonly ILogger _logger;
    private readonly string _systemDirectory;
    private readonly Lazy<IReadOnlyDictionary<string, string>> _explainTextByDisplayName;

    public SystemAuditExplainTextProvider(ILogger? logger, string? systemDirectory = null)
    {
        _logger = logger ?? NullLogger.Instance;
        _systemDirectory = string.IsNullOrWhiteSpace(systemDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.System)
            : systemDirectory;
        _explainTextByDisplayName = new Lazy<IReadOnlyDictionary<string, string>>(
            LoadExplainTexts,
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string GetExplainText(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return string.Empty;

        IReadOnlyDictionary<string, string> explainTexts = _explainTextByDisplayName.Value;
        return GetExplainTextFromDisplayName(explainTexts, displayName);
    }

    public string GetExplainText(Guid subcategoryGuid, string displayName)
    {
        IReadOnlyDictionary<string, string> explainTexts = _explainTextByDisplayName.Value;
        if (explainTexts.TryGetValue(CreateGuidKey(subcategoryGuid), out string? explainText))
            return explainText;

        return GetExplainTextFromDisplayName(explainTexts, displayName);
    }

    private static string GetExplainTextFromDisplayName(IReadOnlyDictionary<string, string> explainTexts, string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return string.Empty;

        return explainTexts.TryGetValue(NormalizeName(displayName), out string? explainText)
            ? explainText
            : string.Empty;
    }

    private IReadOnlyDictionary<string, string> LoadExplainTexts()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string modulePath in GetResourceModulePaths())
        {
            LoadExplainTextsFromModule(modulePath, result);
        }

        return result;
    }

    private void LoadExplainTextsFromModule(string modulePath, IDictionary<string, string> result)
    {
        if (!File.Exists(modulePath))
            return;

        IntPtr module = SecurityPolicyNativeMethods.LoadLibraryEx(
            modulePath,
            IntPtr.Zero,
            SecurityPolicyNativeMethods.LOAD_LIBRARY_AS_DATAFILE);
        if (module == IntPtr.Zero)
        {
            _logger.LogDebug(
                "[SystemAuditExplainTextProvider] Failed to load {ModulePath}: {Error}",
                modulePath,
                Marshal.GetLastWin32Error());
            return;
        }

        try
        {
            for (uint resourceId = 1; resourceId <= MaxAuditPolicyResourceId; resourceId++)
            {
                string? raw = LoadStringResource(module, resourceId);
                if (raw is null)
                    continue;

                if (!TrySplitExplainResource(raw, out string displayName, out string explainText))
                    continue;

                string formattedExplainText = FormatExplainText(displayName, explainText);
                AddExplainText(result, displayName, formattedExplainText);
                AddResourceIdAliases(result, (int)resourceId, formattedExplainText);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "[SystemAuditExplainTextProvider] Failed to read audit policy resources from {ModulePath}",
                modulePath);
        }
        finally
        {
            SecurityPolicyNativeMethods.FreeLibrary(module);
        }
    }

    private static void AddResourceIdAliases(IDictionary<string, string> result, int resourceId, string explainText)
    {
        if (!ResourceIdSubcategoryAliases.TryGetValue(resourceId, out Guid[]? subcategoryGuids))
            return;

        foreach (Guid subcategoryGuid in subcategoryGuids)
        {
            string key = CreateGuidKey(subcategoryGuid);
            if (!result.ContainsKey(key))
            {
                result.Add(key, explainText);
            }
        }
    }

    private static string? LoadStringResource(IntPtr module, uint resourceId)
    {
        var builder = new StringBuilder(16384);
        int length = SecurityPolicyNativeMethods.LoadString(module, resourceId, builder, builder.Capacity);
        if (length <= 0)
            return null;

        return builder.ToString();
    }

    private static bool TrySplitExplainResource(string raw, out string displayName, out string explainText)
    {
        string normalized = NormalizeLineEndings(raw);
        int separatorIndex = normalized.IndexOf($"{Environment.NewLine}{Environment.NewLine}", StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            displayName = string.Empty;
            explainText = string.Empty;
            return false;
        }

        displayName = normalized[..separatorIndex].Trim();
        explainText = normalized[(separatorIndex + (Environment.NewLine.Length * 2))..].Trim();
        return !string.IsNullOrWhiteSpace(displayName) && !string.IsNullOrWhiteSpace(explainText);
    }

    private static string FormatExplainText(string displayName, string explainText)
    {
        return string.Concat(
            displayName.Trim(),
            Environment.NewLine,
            Environment.NewLine,
            NormalizeLineEndings(explainText).Trim());
    }

    private static void AddExplainText(IDictionary<string, string> result, string displayName, string explainText)
    {
        string key = NormalizeName(displayName);
        if (string.IsNullOrEmpty(key) || result.ContainsKey(key))
            return;

        result.Add(key, explainText);
    }

    private IEnumerable<string> GetResourceModulePaths()
    {
        var returnedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string cultureName in GetPreferredCultureNames())
        {
            string localizedPath = Path.Combine(_systemDirectory, cultureName, AuditPolicyMessageMuiName);
            if (returnedPaths.Add(localizedPath))
                yield return localizedPath;
        }

        foreach (string cultureName in GetAdditionalMuiCultureNames())
        {
            string localizedPath = Path.Combine(_systemDirectory, cultureName, AuditPolicyMessageMuiName);
            if (returnedPaths.Add(localizedPath))
                yield return localizedPath;
        }

        string neutralPath = Path.Combine(_systemDirectory, AuditPolicyMessageDllName);
        if (returnedPaths.Add(neutralPath))
            yield return neutralPath;
    }

    private static IEnumerable<string> GetPreferredCultureNames()
    {
        CultureInfo culture = CultureInfo.CurrentUICulture;
        while (!string.IsNullOrWhiteSpace(culture.Name))
        {
            yield return culture.Name;
            culture = culture.Parent;
        }
    }

    private IEnumerable<string> GetAdditionalMuiCultureNames()
    {
        if (!Directory.Exists(_systemDirectory))
            yield break;

        IEnumerable<string> directories;
        try
        {
            directories = Directory.EnumerateDirectories(_systemDirectory);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[SystemAuditExplainTextProvider] Failed to enumerate culture directories under {SystemDirectory}", _systemDirectory);
            yield break;
        }

        foreach (string directoryPath in directories)
        {
            string cultureName = Path.GetFileName(directoryPath);
            if (File.Exists(Path.Combine(directoryPath, AuditPolicyMessageMuiName)))
            {
                yield return cultureName;
            }
        }
    }

    private static string NormalizeName(string displayName)
    {
        string collapsed = CollapseWhitespace(displayName);
        collapsed = collapsed.Replace(" / ", "/", StringComparison.Ordinal);
        collapsed = collapsed.Replace(" /", "/", StringComparison.Ordinal);
        collapsed = collapsed.Replace("/ ", "/", StringComparison.Ordinal);
        return collapsed;
    }

    private static string CreateGuidKey(Guid subcategoryGuid)
    {
        return string.Concat("guid:", subcategoryGuid.ToString("D"));
    }

    private static string CollapseWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        bool sawWhitespace = false;
        foreach (char current in value.Trim())
        {
            if (char.IsWhiteSpace(current))
            {
                sawWhitespace = true;
                continue;
            }

            if (sawWhitespace && builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(current);
            sawWhitespace = false;
        }

        return builder.ToString();
    }

    private static string NormalizeLineEndings(string value)
    {
        return value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Replace("\n", Environment.NewLine, StringComparison.Ordinal);
    }
}
