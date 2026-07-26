using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace OneMMC.Core.Infrastructure.PolicyStorage
{
    /// <summary>
    /// Represents a Windows Group Policy Registry.pol file and provides an IPolicySource implementation.
    /// See https://learn.microsoft.com/en-us/previous-versions/windows/desktop/Policy/registry-policy-file-format for format details.
    /// </summary>
    public sealed class PolFile : IPolicySource
    {
        private const uint PolSignature = 0x67655250; // "PrEG" in little-endian
        private const int PolVersion = 1;

        // Sorted to ensure key clearances (**delvals) are processed before value additions
        private readonly SortedDictionary<string, PolEntryData> _entries = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _casePreservation = new(StringComparer.OrdinalIgnoreCase);

        #region Static Factory Methods

        /// <summary>
        /// Loads a POL file from disk.
        /// </summary>
        /// <param name="filePath">Path to the POL file.</param>
        /// <returns>A new PolFile instance.</returns>
        public static PolFile Load(string filePath)
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(stream);
            return Load(reader);
        }

        /// <summary>
        /// Loads a POL file from a binary stream.
        /// </summary>
        /// <param name="reader">The binary reader to read from.</param>
        /// <returns>A new PolFile instance.</returns>
        public static PolFile Load(BinaryReader reader)
        {
            var pol = new PolFile();

            // Validate signature
            if (reader.ReadUInt32() != PolSignature)
                throw new InvalidDataException("Missing PReg signature");

            // Validate version
            if (reader.ReadUInt32() != PolVersion)
                throw new InvalidDataException("Unknown (newer) version of POL format");

            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                pol.ReadEntry(reader);
            }

            return pol;
        }

        #endregion

        #region Save Methods

        /// <summary>
        /// Saves the POL file to disk.
        /// </summary>
        /// <param name="filePath">Path to save the file to.</param>
        public void Save(string filePath)
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var stream = new FileStream(filePath, FileMode.Create);
            using var writer = new BinaryWriter(stream, Encoding.Unicode);
            Save(writer);
        }

        /// <summary>
        /// Saves the POL file to a binary stream.
        /// </summary>
        /// <param name="writer">The binary writer to write to.</param>
        public void Save(BinaryWriter writer)
        {
            writer.Write(PolSignature);
            writer.Write(PolVersion);

            foreach (var kvp in _entries)
            {
                WriteEntry(writer, kvp.Key, kvp.Value);
            }
        }

        #endregion

        #region IPolicySource Implementation

        public void DeleteValue(string key, string value)
        {
            ForgetValue(key, value);

            if (!WillDeleteValue(key, value))
            {
                var dictKey = GetDictKey(key, "**del." + value);
                _entries[dictKey] = PolEntryData.FromDword(32); // Microsoft's convention
            }
        }

        public void ForgetValue(string key, string value)
        {
            var dictKey = GetDictKey(key, value);
            _entries.Remove(dictKey);

            var deleterKey = GetDictKey(key, "**del." + value);
            _entries.Remove(deleterKey);
        }

        public void SetValue(string key, string value, object data, RegistryValueKind dataType)
        {
            var dictKey = GetDictKey(key, value);
            _entries[dictKey] = PolEntryData.FromArbitrary(data, dataType);
        }

        public bool ContainsValue(string key, string value)
        {
            if (WillDeleteValue(key, value)) return false;
            return _entries.ContainsKey(GetDictKey(key, value));
        }

        public object? GetValue(string key, string value)
        {
            if (!ContainsValue(key, value)) return null;
            return _entries[GetDictKey(key, value)].AsArbitrary();
        }

        public bool WillDeleteValue(string key, string value)
        {
            var keyRoot = GetDictKey(key, "");
            bool willDelete = false;

            foreach (var kvp in _entries)
            {
                if (!kvp.Key.StartsWith(keyRoot, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (kvp.Key.Equals(GetDictKey(key, "**del." + value), StringComparison.OrdinalIgnoreCase) ||
                    kvp.Key.StartsWith(GetDictKey(key, "**delvals"), StringComparison.OrdinalIgnoreCase))
                {
                    willDelete = true;
                }
                else if (kvp.Key.Equals(GetDictKey(key, "**deletevalues"), StringComparison.OrdinalIgnoreCase))
                {
                    var deletedValues = kvp.Value.AsString().Split(';');
                    if (deletedValues.Any(s => s.Equals(value, StringComparison.OrdinalIgnoreCase)))
                        willDelete = true;
                }
                else if (kvp.Key.Equals(GetDictKey(key, value), StringComparison.OrdinalIgnoreCase))
                {
                    willDelete = false; // Explicit value overrides deletion
                }
            }

            return willDelete;
        }

        public List<string> GetValueNames(string key)
        {
            return GetValueNames(key, onlyValues: true);
        }

        public void ClearKey(string key)
        {
            foreach (var valueName in GetValueNames(key, onlyValues: false))
            {
                ForgetValue(key, valueName);
            }

            var deleterKey = GetDictKey(key, "**delvals.");
            _entries[deleterKey] = PolEntryData.FromString(" ");
        }

        public void ForgetKeyClearance(string key)
        {
            var keyDeleter = GetDictKey(key, "**delvals");
            var keysToRemove = _entries.Keys
                .Where(k => k.StartsWith(keyDeleter, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var k in keysToRemove)
            {
                _entries.Remove(k);
            }
        }

        /// <summary>
        /// Deletes a key together with all of its values and subkeys. Unlike <see cref="ClearKey"/>, this both
        /// drops every in-snapshot entry under the subtree (including any stale deletion markers) and records a
        /// <c>**DeleteKeys</c> instruction so the Registry client-side extension removes the already-applied key
        /// and its subkeys on the next refresh. This is the correct "remove this rule" primitive; writing a
        /// <c>**delvals.</c> tombstone only clears values and leaves the key behind as a phantom.
        /// </summary>
        /// <param name="key">The full policy key path to delete.</param>
        public void DeleteKeyTree(string key)
        {
            if (string.IsNullOrEmpty(key))
                return;

            RemoveSubtreeEntries(key);

            // Record the deletion so both apply paths remove the live key: the Registry CSE (RefreshPolicyEx
            // path) and our own ApplyDifference (direct-registry path) both honour **DeleteKeys with a
            // semicolon-delimited list of full key paths in the data field.
            var parentKey = GetParentKey(key);
            var markerDictKey = GetDictKey(parentKey, "**DeleteKeys");

            var paths = new List<string>();
            if (_entries.TryGetValue(markerDictKey, out var existing) && existing.Kind is RegistryValueKind.String or RegistryValueKind.ExpandString)
            {
                paths.AddRange(existing.AsString().Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }

            if (!paths.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                paths.Add(key);
            }

            _entries[markerDictKey] = PolEntryData.FromString(string.Join(";", paths));
        }

        /// <summary>
        /// Removes stale deletion markers (<c>**del.</c>, <c>**delvals.</c>, <c>**deletevalues</c>,
        /// <c>**deletekeys</c>) anywhere within the given subtree. Used to self-heal policy files written by
        /// earlier versions that persisted tombstones which would otherwise be reapplied on every refresh.
        /// </summary>
        /// <param name="rootKey">The subtree root to sweep.</param>
        public void RemoveDeleteMarkers(string rootKey)
        {
            if (string.IsNullOrEmpty(rootKey))
                return;

            var self = rootKey.ToLowerInvariant();
            var subtreePrefix = self + "\\";

            var toRemove = _entries.Keys
                .Where(k =>
                {
                    var lastBackslash = k.LastIndexOf('\\');
                    if (lastBackslash < 0)
                        return false;

                    var ownerKey = k[..lastBackslash];
                    var valueName = k[(lastBackslash + 1)..];
                    bool inScope = ownerKey.Equals(self, StringComparison.OrdinalIgnoreCase)
                        || ownerKey.StartsWith(subtreePrefix, StringComparison.OrdinalIgnoreCase);
                    return inScope && valueName.StartsWith("**del", StringComparison.OrdinalIgnoreCase);
                })
                .ToList();

            foreach (var k in toRemove)
            {
                _entries.Remove(k);
            }
        }

        /// <summary>
        /// Removes every <c>**DeleteKeys</c> instruction from the snapshot and returns the full key paths
        /// they designate. Callers apply the deletions directly at save time, keeping the on-disk file free
        /// of standing delete instructions that the Registry CSE would otherwise re-execute on every refresh
        /// (destroying keys recreated later by other tools such as secpol.msc).
        /// </summary>
        /// <returns>The distinct full key paths scheduled for deletion.</returns>
        public List<string> TakeDeleteKeyInstructions()
        {
            var paths = new List<string>();
            var toRemove = new List<string>();

            foreach (var kvp in _entries)
            {
                var lastBackslash = kvp.Key.LastIndexOf('\\');
                if (lastBackslash < 0)
                    continue;

                var valueName = kvp.Key[(lastBackslash + 1)..];
                if (!valueName.StartsWith("**deletekeys", StringComparison.OrdinalIgnoreCase))
                    continue;

                toRemove.Add(kvp.Key);
                foreach (var path in kvp.Value.AsString().Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (!paths.Contains(path, StringComparer.OrdinalIgnoreCase))
                        paths.Add(path);
                }
            }

            foreach (var k in toRemove)
            {
                _entries.Remove(k);
            }

            return paths;
        }

        #endregion

        #region Additional Public Methods

        /// <summary>
        /// Gets value names in a key.
        /// </summary>
        /// <param name="key">The registry key path.</param>
        /// <param name="onlyValues">If true, excludes meta-entries (** prefixed).</param>
        /// <returns>List of value names.</returns>
        public List<string> GetValueNames(string key, bool onlyValues)
        {
            var valueNames = new List<string>();

            foreach (var k in _entries.Keys)
            {
                var fullPath = _casePreservation.TryGetValue(k, out var preserved) ? preserved : k;
                var lastBackslash = fullPath.LastIndexOf('\\');
                var ownerKey = lastBackslash >= 0 ? fullPath[..lastBackslash] : string.Empty;
                var valueName = lastBackslash >= 0 ? fullPath[(lastBackslash + 1)..] : fullPath;

                // Only values that belong directly to this key count; descendant keys' values must not leak in.
                if (!ownerKey.Equals(key, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!onlyValues || !valueName.StartsWith("**"))
                {
                    valueNames.Add(valueName);
                }
            }

            return valueNames;
        }

        /// <summary>
        /// Gets subkey names under a key.
        /// </summary>
        /// <param name="key">The parent key path.</param>
        /// <returns>List of subkey names.</returns>
        public List<string> GetKeyNames(string key)
        {
            var subkeyNames = new List<string>();
            var prefix = string.IsNullOrEmpty(key) ? "" : key + "\\";

            foreach (var entry in _entries.Keys)
            {
                var fullPath = _casePreservation.TryGetValue(entry, out var preserved) ? preserved : entry;
                var lastBackslash = fullPath.LastIndexOf('\\');
                if (lastBackslash < 0)
                    continue;

                var ownerKey = fullPath[..lastBackslash];
                var valueName = fullPath[(lastBackslash + 1)..];

                // Command pseudo-values (**delvals., **del., **deletekeys, ...) are instructions, not real
                // values, so a key that contains only such markers must not surface as a live subkey. This is
                // what stops deleted-rule tombstones from re-materializing as phantom rules on reload.
                if (valueName.StartsWith("**"))
                    continue;

                if (!ownerKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                var localKeyName = ownerKey[prefix.Length..];
                if (localKeyName.Length == 0)
                    continue; // value belongs directly to `key`, not to a subkey

                var firstBackslash = localKeyName.IndexOf('\\');
                if (firstBackslash >= 0)
                    localKeyName = localKeyName[..firstBackslash];

                if (!subkeyNames.Contains(localKeyName, StringComparer.OrdinalIgnoreCase))
                    subkeyNames.Add(localKeyName);
            }

            return subkeyNames;
        }

        /// <summary>
        /// Gets the registry value kind for a value.
        /// </summary>
        /// <param name="key">The registry key path.</param>
        /// <param name="value">The value name.</param>
        /// <returns>The value kind.</returns>
        public RegistryValueKind GetValueKind(string key, string value)
        {
            var dictKey = GetDictKey(key, value);
            return _entries.TryGetValue(dictKey, out var entry) ? entry.Kind : RegistryValueKind.Unknown;
        }

        /// <summary>
        /// Creates a deep copy of this PolFile.
        /// </summary>
        /// <returns>A new PolFile with the same contents.</returns>
        public PolFile Duplicate()
        {
            using var ms = new MemoryStream();
            using (var writer = new BinaryWriter(ms, Encoding.Unicode, leaveOpen: true))
            {
                Save(writer);
            }

            ms.Position = 0;
            using var reader = new BinaryReader(ms, Encoding.Unicode);
            return Load(reader);
        }

        /// <summary>
        /// Applies the difference between this POL file and an older version to a target policy source.
        /// </summary>
        /// <param name="oldVersion">The previous version to compare against (can be null).</param>
        /// <param name="target">The target policy source to apply changes to.</param>
        public void ApplyDifference(PolFile? oldVersion, IPolicySource target)
        {
            oldVersion ??= new PolFile();
            var oldEntries = oldVersion._entries.Keys
                .Where(k => !k.Contains("\\**"))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in _entries)
            {
                ProcessEntryForTarget(kvp.Key, kvp.Value, target, oldEntries);
            }

            // Remove entries that existed in the old version but not in the new
            foreach (var key in oldEntries.Where(RegistryPolicyProxy.IsPolicyKey))
            {
                var fullPath = _casePreservation.TryGetValue(key, out var preserved) ? preserved : key;
                var lastBackslash = fullPath.LastIndexOf('\\');
                if (lastBackslash >= 0)
                {
                    var keyPart = fullPath[..lastBackslash];
                    var valuePart = fullPath[(lastBackslash + 1)..];
                    target.ForgetValue(keyPart, valuePart);
                }
            }
        }

        /// <summary>
        /// Applies all entries in this POL file to a target policy source.
        /// </summary>
        /// <param name="target">The target policy source.</param>
        public void Apply(IPolicySource target)
        {
            ApplyDifference(null, target);
        }

        #endregion

        #region Private Methods

        private void RemoveSubtreeEntries(string key)
        {
            var self = key.ToLowerInvariant();
            var subtreePrefix = self + "\\";

            var toRemove = _entries.Keys
                .Where(k =>
                {
                    var lastBackslash = k.LastIndexOf('\\');
                    if (lastBackslash < 0)
                        return false;

                    var ownerKey = k[..lastBackslash];
                    return ownerKey.Equals(self, StringComparison.OrdinalIgnoreCase)
                        || ownerKey.StartsWith(subtreePrefix, StringComparison.OrdinalIgnoreCase);
                })
                .ToList();

            foreach (var k in toRemove)
            {
                _entries.Remove(k);
            }
        }

        private static string GetParentKey(string key)
        {
            var lastBackslash = key.LastIndexOf('\\');
            return lastBackslash > 0 ? key[..lastBackslash] : key;
        }

        private string GetDictKey(string key, string value)
        {
            var origCase = key + "\\" + value;
            var lowerCase = origCase.ToLowerInvariant();

            if (!_casePreservation.ContainsKey(lowerCase))
            {
                _casePreservation[lowerCase] = origCase;
            }

            return lowerCase;
        }

        private void ReadEntry(BinaryReader reader)
        {
            reader.BaseStream.Position += 2; // Skip "["

            var key = ReadNullTerminatedString(reader);
            reader.BaseStream.Position += 2; // Skip ";"

            var value = ReadNullTerminatedString(reader);

            // Handle potential extra null before ";"
            var check = reader.ReadUInt16();
            if (check != ';')
            {
                reader.BaseStream.Position += 2;
            }

            var entry = new PolEntryData
            {
                Kind = (RegistryValueKind)reader.ReadInt32()
            };

            reader.BaseStream.Position += 2; // Skip ";"

            var length = reader.ReadUInt32();
            reader.BaseStream.Position += 2; // Skip ";"

            entry.Data = new byte[length];
            reader.Read(entry.Data, 0, (int)length);

            reader.BaseStream.Position += 2; // Skip "]"

            _entries[GetDictKey(key, value)] = entry;
        }

        private static string ReadNullTerminatedString(BinaryReader reader)
        {
            var sb = new StringBuilder();
            while (true)
            {
                var charCode = reader.ReadUInt16();
                if (charCode == 0) break;
                sb.Append((char)charCode);
            }
            return sb.ToString();
        }

        private void WriteEntry(BinaryWriter writer, string dictKey, PolEntryData entry)
        {
            var fullPath = _casePreservation.TryGetValue(dictKey, out var preserved) ? preserved : dictKey;
            var lastBackslash = fullPath.LastIndexOf('\\');

            string keyPath, valueName;
            if (lastBackslash >= 0)
            {
                keyPath = fullPath[..lastBackslash];
                valueName = fullPath[(lastBackslash + 1)..];
            }
            else
            {
                keyPath = "";
                valueName = fullPath;
            }

            writer.Write('[');
            WriteNullTerminatedString(writer, keyPath);
            writer.Write(';');
            WriteNullTerminatedString(writer, valueName);
            writer.Write(';');
            writer.Write((int)entry.Kind);
            writer.Write(';');
            writer.Write((uint)entry.Data.Length);
            writer.Write(';');
            writer.Write(entry.Data);
            writer.Write(']');
        }

        private static void WriteNullTerminatedString(BinaryWriter writer, string text)
        {
            foreach (var c in text)
            {
                writer.Write(c);
            }
            writer.Write((ushort)0);
        }

        private void ProcessEntryForTarget(string key, PolEntryData entry, IPolicySource target, HashSet<string> oldEntries)
        {
            var fullPath = _casePreservation.TryGetValue(key, out var preserved) ? preserved : key;
            var lastBackslash = fullPath.LastIndexOf('\\');

            if (lastBackslash < 0) return;

            var keyPart = fullPath[..lastBackslash];
            var valuePart = fullPath[(lastBackslash + 1)..];
            var valuePartLower = valuePart.ToLowerInvariant();

            if (valuePartLower.StartsWith("**del."))
            {
                target.DeleteValue(keyPart, valuePart[6..]);
            }
            else if (valuePartLower.StartsWith("**delvals"))
            {
                target.ClearKey(keyPart);
            }
            else if (valuePartLower == "**deletevalues")
            {
                foreach (var v in entry.AsString().Split(';'))
                {
                    target.DeleteValue(keyPart, v);
                }
            }
            else if (valuePartLower.StartsWith("**deletekeys"))
            {
                // Per MS-GPREG the data field carries a semicolon-delimited list of full key paths to delete
                // (the key field is ignored); each named key is removed together with all of its subkeys.
                foreach (var fullKeyPath in entry.AsString().Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    target.DeleteKeyTree(fullKeyPath);
                }
            }
            else if (!string.IsNullOrEmpty(valuePart) && !valuePart.StartsWith("**"))
            {
                target.SetValue(keyPart, valuePart, entry.AsArbitrary(), entry.Kind);
                oldEntries.Remove(key);
            }
        }

        #endregion

        #region Nested Types

        private sealed class PolEntryData
        {
            public RegistryValueKind Kind { get; set; }
            public byte[] Data { get; set; } = Array.Empty<byte>();

            public string AsString()
            {
                var sb = new StringBuilder();
                for (int i = 0; i < Data.Length / 2; i++)
                {
                    int charCode = Data[i * 2] | (Data[(i * 2) + 1] << 8);
                    if (charCode == 0) break;
                    sb.Append((char)charCode);
                }
                return sb.ToString();
            }

            public static PolEntryData FromString(string text, bool expand = false)
            {
                var data = new byte[(text.Length + 1) * 2];
                for (int i = 0; i < text.Length; i++)
                {
                    int charCode = text[i];
                    data[i * 2] = (byte)(charCode & 0xFF);
                    data[(i * 2) + 1] = (byte)(charCode >> 8);
                }
                // Last 2 bytes are 0 (null terminator)

                return new PolEntryData
                {
                    Kind = expand ? RegistryValueKind.ExpandString : RegistryValueKind.String,
                    Data = data
                };
            }

            public uint AsDword()
            {
                if (Data.Length < 4) return 0;
                return (uint)(Data[0] | (Data[1] << 8) | (Data[2] << 16) | (Data[3] << 24));
            }

            public static PolEntryData FromDword(uint value)
            {
                return new PolEntryData
                {
                    Kind = RegistryValueKind.DWord,
                    Data = new[]
                    {
                        (byte)(value & 0xFF),
                        (byte)((value >> 8) & 0xFF),
                        (byte)((value >> 16) & 0xFF),
                        (byte)(value >> 24)
                    }
                };
            }

            public ulong AsQword()
            {
                ulong value = 0;
                for (int i = 0; i < 8 && i < Data.Length; i++)
                {
                    value |= (ulong)Data[i] << (i * 8);
                }
                return value;
            }

            public static PolEntryData FromQword(ulong value)
            {
                var data = new byte[8];
                for (int i = 0; i < 8; i++)
                {
                    data[i] = (byte)((value >> (i * 8)) & 0xFF);
                }
                return new PolEntryData { Kind = RegistryValueKind.QWord, Data = data };
            }

            public string[] AsMultiString()
            {
                var strings = new List<string>();
                var sb = new StringBuilder();

                for (int i = 0; i < Data.Length / 2; i++)
                {
                    int charCode = Data[i * 2] | (Data[(i * 2) + 1] << 8);
                    if (charCode == 0)
                    {
                        if (sb.Length == 0) break; // Double null = end
                        strings.Add(sb.ToString());
                        sb.Clear();
                    }
                    else
                    {
                        sb.Append((char)charCode);
                    }
                }

                return strings.ToArray();
            }

            public static PolEntryData FromMultiString(string[] strings)
            {
                int totalLength = strings.Sum(s => s.Length + 1) + 1; // +1 for each null, +1 for final null
                var data = new byte[totalLength * 2];

                int offset = 0;
                foreach (var s in strings)
                {
                    foreach (var c in s)
                    {
                        data[offset] = (byte)(c & 0xFF);
                        data[offset + 1] = (byte)(c >> 8);
                        offset += 2;
                    }
                    offset += 2; // Null terminator
                }
                // Final null is already 0

                return new PolEntryData { Kind = RegistryValueKind.MultiString, Data = data };
            }

            public byte[] AsBinary() => (byte[])Data.Clone();

            public static PolEntryData FromBinary(byte[] data, RegistryValueKind kind = RegistryValueKind.Binary)
            {
                return new PolEntryData { Kind = kind, Data = (byte[])data.Clone() };
            }

            public object AsArbitrary()
            {
                return Kind switch
                {
                    RegistryValueKind.String or RegistryValueKind.ExpandString => AsString(),
                    RegistryValueKind.DWord => AsDword(),
                    RegistryValueKind.QWord => AsQword(),
                    RegistryValueKind.MultiString => AsMultiString(),
                    _ => AsBinary()
                };
            }

            public static PolEntryData FromArbitrary(object data, RegistryValueKind kind)
            {
                return kind switch
                {
                    RegistryValueKind.String => FromString(data?.ToString() ?? string.Empty),
                    RegistryValueKind.ExpandString => FromString(data?.ToString() ?? string.Empty, expand: true),
                    RegistryValueKind.DWord => FromDword(Convert.ToUInt32(data)),
                    RegistryValueKind.QWord => FromQword(Convert.ToUInt64(data)),
                    RegistryValueKind.MultiString when data is string[] arr => FromMultiString(arr),
                    RegistryValueKind.MultiString when data is IEnumerable<string> enumerable => FromMultiString(enumerable.ToArray()),
                    _ => FromBinary((byte[])data, kind)
                };
            }
        }

        #endregion
    }
}


