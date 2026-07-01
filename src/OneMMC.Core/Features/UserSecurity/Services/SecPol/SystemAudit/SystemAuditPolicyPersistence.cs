using System.Globalization;
using System.Security.AccessControl;
using System.Text;
using Microsoft.Extensions.Logging;

namespace OneMMC.Core.Features.UserSecurity.Services.SecPol.SystemAudit;

internal sealed class SystemAuditPolicyPersistence
{
    private const int CsvFieldCount = 7;
    private static readonly string[] AuditCsvHeaderFields =
    [
        "Machine Name",
        "Policy Target",
        "Subcategory",
        "Subcategory GUID",
        "Inclusion Setting",
        "Exclusion Setting",
        "Setting Value"
    ];

    private readonly string _auditCsvPath;
    private readonly string _gptIniPath;
    private readonly ILogger _logger;
    private readonly ISystemAuditPersistenceStorage _storage;

    internal SystemAuditPolicyPersistence(
        string auditCsvPath,
        string gptIniPath,
        ILogger logger,
        ISystemAuditPersistenceStorage? storage = null)
    {
        _auditCsvPath = auditCsvPath;
        _gptIniPath = gptIniPath;
        _logger = logger;
        _storage = storage ?? new SystemAuditPersistenceStorage();
    }

    internal SystemAuditCsvDocument LoadForRead()
    {
        return LoadCore(createBaselineWhenMissing: false, persistRepair: true);
    }

    internal SystemAuditCsvDocument LoadForSave()
    {
        return LoadCore(createBaselineWhenMissing: true, persistRepair: false);
    }

    internal void Save(SystemAuditCsvDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        string content = document.ToCsvContent();
        ValidateAuditCsvContent(content);
        WriteAtomically(_auditCsvPath, content, validateAuditCsv: true);
    }

    internal void UpdateGptIni()
    {
        var lines = _storage.FileExists(_gptIniPath)
            ? _storage.ReadAllLines(_gptIniPath)
            : [];

        var updatedLines = new List<string>(lines.Length + 3);
        bool sawGeneral = false;
        bool sawVersion = false;
        bool sawMachineExtensions = false;

        foreach (string line in lines)
        {
            if (line.Equals("[General]", StringComparison.OrdinalIgnoreCase))
            {
                sawGeneral = true;
                updatedLines.Add(line);
                continue;
            }

            if (TrySplitKeyValue(line, out string key, out string value))
            {
                if (key.Equals("Version", StringComparison.OrdinalIgnoreCase))
                {
                    updatedLines.Add(FormattableString.Invariant($"Version={IncrementMachineVersion(value)}"));
                    sawVersion = true;
                    continue;
                }

                if (key.Equals("gPCMachineExtensionNames", StringComparison.OrdinalIgnoreCase))
                {
                    updatedLines.Add(FormattableString.Invariant(
                        $"gPCMachineExtensionNames={EnsureExtensionPair(value, SystemAuditPersistenceConstants.AuditMachineExtensionPair)}"));
                    sawMachineExtensions = true;
                    continue;
                }
            }

            updatedLines.Add(line);
        }

        if (!sawGeneral)
        {
            updatedLines.Insert(0, "[General]");
        }

        if (!sawMachineExtensions)
        {
            updatedLines.Add(FormattableString.Invariant(
                $"gPCMachineExtensionNames={SystemAuditPersistenceConstants.AuditMachineExtensionPair}"));
        }

        if (!sawVersion)
        {
            updatedLines.Add("Version=1");
        }

        string content = string.Join(Environment.NewLine, updatedLines) + Environment.NewLine;
        WriteAtomically(_gptIniPath, content, validateAuditCsv: false);
    }

    private SystemAuditCsvDocument LoadCore(bool createBaselineWhenMissing, bool persistRepair)
    {
        if (!_storage.FileExists(_auditCsvPath))
        {
            return createBaselineWhenMissing
                ? new SystemAuditCsvDocument()
                : new SystemAuditCsvDocument();
        }

        string content = _storage.ReadAllText(_auditCsvPath);
        SystemAuditCsvParseResult parseResult = SystemAuditCsvDocument.Parse(content, _logger);
        if (!parseResult.RequiresRepair)
        {
            return parseResult.Document;
        }

        SystemAuditCsvDocument repairedDocument = BuildRepairedDocument(parseResult);
        if (!persistRepair)
        {
            return repairedDocument;
        }

        try
        {
            Save(repairedDocument);
            return repairedDocument;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SystemAuditPolicyPersistence] Failed to rewrite malformed local policy audit.csv at {Path}", _auditCsvPath);
            return parseResult.Document;
        }
    }

    private SystemAuditCsvDocument BuildRepairedDocument(SystemAuditCsvParseResult parseResult)
    {
        var repairedDocument = new SystemAuditCsvDocument();
        repairedDocument.OverlayRows(parseResult.Document.Rows);
        return repairedDocument;
    }

    private void WriteAtomically(string path, string content, bool validateAuditCsv)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _storage.CreateDirectory(directory);
        }

        string tempPath = Path.Combine(directory ?? Path.GetTempPath(), $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        _storage.WriteAllText(tempPath, content, new UTF8Encoding(false));

        try
        {
            if (validateAuditCsv)
            {
                ValidateAuditCsvContent(_storage.ReadAllText(tempPath));
            }

            if (_storage.FileExists(path))
            {
                try
                {
                    _storage.ReplaceFile(tempPath, path);
                }
                catch
                {
                    _storage.MoveFile(tempPath, path, overwrite: true);
                }
            }
            else
            {
                _storage.MoveFile(tempPath, path, overwrite: false);
            }
        }
        finally
        {
            if (_storage.FileExists(tempPath))
            {
                _storage.DeleteFile(tempPath);
            }
        }
    }

    private void ValidateAuditCsvContent(string content)
    {
        SystemAuditCsvParseResult parseResult = SystemAuditCsvDocument.Parse(content, _logger);
        if (!parseResult.HasInvariantHeader)
        {
            throw new InvalidOperationException("audit.csv must use the invariant header for local audit policy persistence.");
        }

        if (parseResult.SawInvalidRows)
        {
            throw new InvalidOperationException("audit.csv contains malformed rows and cannot be persisted.");
        }

        if (parseResult.SawDroppedRows)
        {
            throw new InvalidOperationException("audit.csv contains rows that cannot be written back to the local policy file.");
        }

        string firstLine = ReadFirstNonEmptyLine(content);
        if (!string.Equals(firstLine, SystemAuditPersistenceConstants.AuditCsvHeader, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("audit.csv header must exactly match the required local policy header.");
        }
    }

    private static string ReadFirstNonEmptyLine(string content)
    {
        using var reader = new StringReader(content);
        while (reader.ReadLine() is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                return line;
            }
        }

        return string.Empty;
    }

    private static bool TrySplitKeyValue(string line, out string key, out string value)
    {
        int separatorIndex = line.IndexOf('=');
        if (separatorIndex <= 0)
        {
            key = string.Empty;
            value = string.Empty;
            return false;
        }

        key = line[..separatorIndex].Trim();
        value = line[(separatorIndex + 1)..].Trim();
        return true;
    }

    private static int IncrementMachineVersion(string currentValue)
    {
        if (!int.TryParse(currentValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int version) || version < 0)
        {
            version = 0;
        }

        // gpt.ini stores user and machine policy versions in separate 16-bit halves; machine edits increment the low word.
        uint current = unchecked((uint)version);
        uint userVersion = current & 0xFFFF0000U;
        uint machineVersion = (current & 0x0000FFFFU) + 1U;
        return unchecked((int)(userVersion | (machineVersion & 0x0000FFFFU)));
    }

    private static string EnsureExtensionPair(string existingValue, string requiredPair)
    {
        var pairs = ParseExtensionPairs(existingValue);
        if (pairs.Any(pair => pair.Equals(requiredPair, StringComparison.OrdinalIgnoreCase)))
        {
            return string.Concat(pairs);
        }

        pairs.Add(requiredPair);
        return string.Concat(pairs);
    }

    private static List<string> ParseExtensionPairs(string value)
    {
        var pairs = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int index = 0;
        while (index < value.Length)
        {
            int start = value.IndexOf('[', index);
            if (start < 0)
                break;

            int end = value.IndexOf(']', start + 1);
            if (end < 0)
                break;

            string pair = value[start..(end + 1)];
            if (seen.Add(pair))
            {
                pairs.Add(pair);
            }

            index = end + 1;
        }

        return pairs;
    }

    internal sealed class SystemAuditCsvDocument
    {
        private readonly List<SystemAuditCsvRow> _rows = [];

        internal IReadOnlyList<SystemAuditCsvRow> Rows => _rows;

        internal static SystemAuditCsvParseResult Parse(string content, ILogger logger)
        {
            ArgumentNullException.ThrowIfNull(logger);

            var document = new SystemAuditCsvDocument();
            bool sawNonEmptyLine = false;
            bool sawInvalidRows = false;
            bool sawDroppedRows = false;
            bool hasInvariantHeader = false;

            using var reader = new StringReader(content);
            while (reader.ReadLine() is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                sawNonEmptyLine = true;
                List<string> fields = ParseCsvLine(line);
                if (fields.Count != CsvFieldCount)
                {
                    sawInvalidRows = true;
                    logger.LogWarning("[SystemAuditPolicyPersistence] Ignoring malformed audit.csv row: {Line}", line);
                    continue;
                }

                if (IsInvariantHeader(fields))
                {
                    hasInvariantHeader = true;
                    continue;
                }

                CsvRowParseResult rowResult = TryCreateValidatedRow(fields, logger, line, out SystemAuditCsvRow? row);
                if (rowResult == CsvRowParseResult.Invalid)
                {
                    sawInvalidRows = true;
                    continue;
                }

                if (rowResult == CsvRowParseResult.Dropped)
                {
                    sawDroppedRows = true;
                    continue;
                }

                document._rows.Add(row!);
            }

            return new SystemAuditCsvParseResult(document, hasInvariantHeader, sawNonEmptyLine, sawInvalidRows, sawDroppedRows);
        }

        internal SystemAuditCsvRow? TryGetSystemRow(Guid subcategoryGuid)
        {
            string guidText = subcategoryGuid.ToString("D");
            return _rows.LastOrDefault(row =>
                row.PolicyTarget.Equals(SystemAuditPersistenceConstants.SystemPolicyTarget, StringComparison.OrdinalIgnoreCase) &&
                row.SubcategoryGuid.Trim('{', '}').Equals(guidText, StringComparison.OrdinalIgnoreCase));
        }

        internal SystemAuditCsvRow? TryGetGlobalObjectAccessRow(string subcategory)
        {
            return _rows.LastOrDefault(row =>
                IsGlobalObjectAccessSubcategory(row.Subcategory) &&
                row.Subcategory.Equals(subcategory, StringComparison.OrdinalIgnoreCase));
        }

        internal void RemoveSystemRow(Guid subcategoryGuid)
        {
            string guidText = subcategoryGuid.ToString("D");
            _rows.RemoveAll(row =>
                row.PolicyTarget.Equals(SystemAuditPersistenceConstants.SystemPolicyTarget, StringComparison.OrdinalIgnoreCase) &&
                row.SubcategoryGuid.Trim('{', '}').Equals(guidText, StringComparison.OrdinalIgnoreCase));
        }

        internal void RemoveGlobalObjectAccessRow(string subcategory)
        {
            _rows.RemoveAll(row =>
                IsGlobalObjectAccessSubcategory(row.Subcategory) &&
                row.Subcategory.Equals(subcategory, StringComparison.OrdinalIgnoreCase));
        }

        internal void UpsertSystemRow(SystemAuditCsvRow row)
        {
            RemoveSystemRow(Guid.Parse(row.SubcategoryGuid));
            _rows.Add(row);
        }

        internal void UpsertGlobalObjectAccessRow(SystemAuditCsvRow row)
        {
            RemoveGlobalObjectAccessRow(row.Subcategory);
            _rows.Add(row);
        }

        internal void OverlayRows(IEnumerable<SystemAuditCsvRow> rows)
        {
            foreach (SystemAuditCsvRow row in rows)
            {
                string identity = GetIdentity(row);
                int existingIndex = _rows.FindIndex(existing => GetIdentity(existing).Equals(identity, StringComparison.OrdinalIgnoreCase));
                if (existingIndex >= 0)
                {
                    _rows[existingIndex] = row.Clone();
                }
                else
                {
                    _rows.Add(row.Clone());
                }
            }
        }

        internal string ToCsvContent()
        {
            var builder = new StringBuilder();
            builder.AppendLine(SystemAuditPersistenceConstants.AuditCsvHeader);
            foreach (SystemAuditCsvRow row in _rows)
            {
                builder.Append(EscapeCsv(row.MachineName));
                builder.Append(',');
                builder.Append(EscapeCsv(row.PolicyTarget));
                builder.Append(',');
                builder.Append(EscapeCsv(row.Subcategory));
                builder.Append(',');
                builder.Append(EscapeCsv(FormatSubcategoryGuid(row)));
                builder.Append(',');
                builder.Append(EscapeCsv(row.InclusionSetting));
                builder.Append(',');
                builder.Append(EscapeCsv(row.ExclusionSetting));
                builder.Append(',');
                builder.Append(EscapeCsv(row.SettingValue));
                builder.AppendLine();
            }

            return builder.ToString();
        }

        private static bool IsInvariantHeader(IReadOnlyList<string> fields)
        {
            for (int index = 0; index < AuditCsvHeaderFields.Length; index++)
            {
                if (!fields[index].Trim().Equals(AuditCsvHeaderFields[index], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private enum CsvRowParseResult
        {
            Accepted,
            Dropped,
            Invalid
        }

        private static CsvRowParseResult TryCreateValidatedRow(
            IReadOnlyList<string> fields,
            ILogger logger,
            string line,
            out SystemAuditCsvRow? row)
        {
            string policyTarget = fields[1].Trim();
            string subcategory = fields[2].Trim();
            string subcategoryGuid = fields[3].Trim();
            string settingValue = fields[6].Trim();

            if (string.IsNullOrWhiteSpace(subcategory))
            {
                logger.LogWarning("[SystemAuditPolicyPersistence] Ignoring audit.csv row with no subcategory: {Line}", line);
                row = null;
                return CsvRowParseResult.Invalid;
            }

            bool hasSystemGuid = Guid.TryParse(subcategoryGuid.Trim('{', '}'), out Guid parsedSystemGuid);
            if (hasSystemGuid)
            {
                if (!uint.TryParse(settingValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint numericSettingValue))
                {
                    logger.LogWarning("[SystemAuditPolicyPersistence] Ignoring audit.csv row with non-numeric setting value: {Line}", line);
                    row = null;
                    return CsvRowParseResult.Invalid;
                }

                if (numericSettingValue == 0)
                {
                    row = null;
                    return CsvRowParseResult.Dropped;
                }

                if (numericSettingValue > 4)
                {
                    logger.LogWarning("[SystemAuditPolicyPersistence] Ignoring audit.csv row with out-of-range setting value: {Line}", line);
                    row = null;
                    return CsvRowParseResult.Invalid;
                }

                row = CreateSystemRow(fields, parsedSystemGuid, numericSettingValue);
                return CsvRowParseResult.Accepted;
            }

            if (!string.IsNullOrWhiteSpace(subcategoryGuid))
            {
                logger.LogWarning("[SystemAuditPolicyPersistence] Ignoring audit.csv row with invalid subcategory GUID: {Line}", line);
                row = null;
                return CsvRowParseResult.Invalid;
            }

            if (IsGlobalObjectAccessSubcategory(subcategory))
            {
                if (string.IsNullOrWhiteSpace(settingValue))
                {
                    row = null;
                    return CsvRowParseResult.Dropped;
                }

                try
                {
                    _ = new RawSecurityDescriptor(settingValue);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "[SystemAuditPolicyPersistence] Ignoring audit.csv row with invalid Global Object Access SDDL: {Line}", line);
                    row = null;
                    return CsvRowParseResult.Invalid;
                }

                row = CreateRow(fields);
                return CsvRowParseResult.Accepted;
            }

            if (!uint.TryParse(settingValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                logger.LogWarning("[SystemAuditPolicyPersistence] Ignoring audit.csv row with non-numeric setting value: {Line}", line);
                row = null;
                return CsvRowParseResult.Invalid;
            }

            row = CreateRow(fields);
            return CsvRowParseResult.Accepted;
        }

        private static SystemAuditCsvRow CreateRow(IReadOnlyList<string> fields)
        {
            return new SystemAuditCsvRow
            {
                MachineName = fields[0],
                PolicyTarget = fields[1],
                Subcategory = fields[2],
                SubcategoryGuid = fields[3].Trim().Trim('{', '}'),
                InclusionSetting = fields[4],
                ExclusionSetting = fields[5],
                SettingValue = fields[6]
            };
        }

        private static SystemAuditCsvRow CreateSystemRow(IReadOnlyList<string> fields, Guid subcategoryGuid, uint settingValue)
        {
            SystemAuditCsvRow row = CreateRow(fields);
            row.PolicyTarget = SystemAuditPersistenceConstants.SystemPolicyTarget;
            row.SubcategoryGuid = subcategoryGuid.ToString("D");
            row.SettingValue = settingValue.ToString(CultureInfo.InvariantCulture);
            return row;
        }

        private static string FormatSubcategoryGuid(SystemAuditCsvRow row)
        {
            if (row.PolicyTarget.Equals(SystemAuditPersistenceConstants.SystemPolicyTarget, StringComparison.OrdinalIgnoreCase) &&
                Guid.TryParse(row.SubcategoryGuid.Trim('{', '}'), out Guid subcategoryGuid))
            {
                return FormattableString.Invariant($"{{{subcategoryGuid:D}}}");
            }

            return row.SubcategoryGuid;
        }

        private static string GetIdentity(SystemAuditCsvRow row)
        {
            if (row.PolicyTarget.Equals(SystemAuditPersistenceConstants.SystemPolicyTarget, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(row.SubcategoryGuid))
            {
                return string.Concat("system:", row.SubcategoryGuid.Trim().Trim('{', '}').ToUpperInvariant());
            }

            if (IsGlobalObjectAccessSubcategory(row.Subcategory))
            {
                return string.Concat("goaa:", row.Subcategory.Trim().ToUpperInvariant());
            }

            return string.Concat(
                "misc:",
                row.PolicyTarget.Trim().ToUpperInvariant(),
                "|",
                row.Subcategory.Trim().ToUpperInvariant(),
                "|",
                row.SubcategoryGuid.Trim().ToUpperInvariant());
        }

        private static bool IsGlobalObjectAccessSubcategory(string subcategory)
        {
            return subcategory.Equals(SystemAuditPersistenceConstants.FileGlobalSaclName, StringComparison.OrdinalIgnoreCase) ||
                subcategory.Equals(SystemAuditPersistenceConstants.RegistryGlobalSaclName, StringComparison.OrdinalIgnoreCase);
        }

        private static List<string> ParseCsvLine(string line)
        {
            var fields = new List<string>(CsvFieldCount);
            var builder = new StringBuilder();
            bool inQuotes = false;

            for (int index = 0; index < line.Length; index++)
            {
                char current = line[index];
                if (current == '"')
                {
                    if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                    {
                        builder.Append('"');
                        index++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }

                    continue;
                }

                if (current == ',' && !inQuotes)
                {
                    fields.Add(builder.ToString());
                    builder.Clear();
                    continue;
                }

                builder.Append(current);
            }

            fields.Add(builder.ToString());
            return fields;
        }

        private static string EscapeCsv(string value)
        {
            value ??= string.Empty;
            if (!value.Contains(',', StringComparison.Ordinal) &&
                !value.Contains('"', StringComparison.Ordinal) &&
                !value.Contains('\r', StringComparison.Ordinal) &&
                !value.Contains('\n', StringComparison.Ordinal))
            {
                return value;
            }

            return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        }
    }

    internal sealed class SystemAuditCsvParseResult
    {
        internal SystemAuditCsvParseResult(
            SystemAuditCsvDocument document,
            bool hasInvariantHeader,
            bool sawNonEmptyLine,
            bool sawInvalidRows,
            bool sawDroppedRows)
        {
            Document = document;
            HasInvariantHeader = hasInvariantHeader;
            SawNonEmptyLine = sawNonEmptyLine;
            SawInvalidRows = sawInvalidRows;
            SawDroppedRows = sawDroppedRows;
        }

        internal SystemAuditCsvDocument Document { get; }

        internal bool HasInvariantHeader { get; }

        internal bool SawNonEmptyLine { get; }

        internal bool SawInvalidRows { get; }

        internal bool SawDroppedRows { get; }

        internal bool RequiresRepair => !HasInvariantHeader || SawInvalidRows || SawDroppedRows;
    }

    internal sealed class SystemAuditCsvRow
    {
        internal string MachineName { get; set; } = string.Empty;

        internal string PolicyTarget { get; set; } = string.Empty;

        internal string Subcategory { get; set; } = string.Empty;

        internal string SubcategoryGuid { get; set; } = string.Empty;

        internal string InclusionSetting { get; set; } = string.Empty;

        internal string ExclusionSetting { get; set; } = string.Empty;

        internal string SettingValue { get; set; } = string.Empty;

        internal SystemAuditCsvRow Clone()
        {
            return new SystemAuditCsvRow
            {
                MachineName = MachineName,
                PolicyTarget = PolicyTarget,
                Subcategory = Subcategory,
                SubcategoryGuid = SubcategoryGuid,
                InclusionSetting = InclusionSetting,
                ExclusionSetting = ExclusionSetting,
                SettingValue = SettingValue
            };
        }
    }
}

internal interface ISystemAuditPersistenceStorage
{
    bool FileExists(string path);

    string ReadAllText(string path);

    string[] ReadAllLines(string path);

    void WriteAllText(string path, string content, Encoding encoding);

    void CreateDirectory(string path);

    void ReplaceFile(string sourcePath, string destinationPath);

    void MoveFile(string sourcePath, string destinationPath, bool overwrite);

    void DeleteFile(string path);
}

internal sealed class SystemAuditPersistenceStorage : ISystemAuditPersistenceStorage
{
    public bool FileExists(string path) => File.Exists(path);

    public string ReadAllText(string path) => File.ReadAllText(path);

    public string[] ReadAllLines(string path) => File.ReadAllLines(path);

    public void WriteAllText(string path, string content, Encoding encoding) => File.WriteAllText(path, content, encoding);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void ReplaceFile(string sourcePath, string destinationPath) => File.Replace(sourcePath, destinationPath, null);

    public void MoveFile(string sourcePath, string destinationPath, bool overwrite) => File.Move(sourcePath, destinationPath, overwrite);

    public void DeleteFile(string path) => File.Delete(path);
}
