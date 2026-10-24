using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OneMMC.Core.Features.UserSecurity.Services.SecPol
{
    /// <summary>
    /// A Security Configuration Engine security template: the UTF-16 INF format the engine parses with
    /// <c>SceOpenProfile</c>. It is used to write the one-setting "changes" template behind a
    /// local-policy update. Setting a line into an existing template preserves every other section and
    /// line verbatim.
    /// </summary>
    internal sealed class SecurityTemplateDocument
    {
        private sealed class Section
        {
            internal Section(string name) => Name = name;

            internal string Name { get; }

            /// <summary>The section's lines, verbatim, excluding its own <c>[Name]</c> header.</summary>
            internal List<string> Lines { get; } = [];
        }

        private readonly List<string> _preamble = [];
        private readonly List<Section> _sections = [];

        private SecurityTemplateDocument()
        {
        }

        /// <summary>
        /// Loads a template, or returns an empty one carrying the standard header when the file does
        /// not exist.
        /// </summary>
        internal static SecurityTemplateDocument Load(string path)
        {
            var document = new SecurityTemplateDocument();

            if (!File.Exists(path))
            {
                document.EnsureHeader();
                return document;
            }

            // Security templates are UTF-16; the BOM lets the reader confirm it.
            string[] lines = File.ReadAllLines(path, Encoding.Unicode);
            Section? current = null;

            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith('[') && trimmed.EndsWith(']') && trimmed.Length > 2)
                {
                    current = new Section(trimmed.Substring(1, trimmed.Length - 2).Trim());
                    document._sections.Add(current);
                }
                else if (current is null)
                {
                    document._preamble.Add(line);
                }
                else
                {
                    current.Lines.Add(line);
                }
            }

            document.EnsureHeader();
            return document;
        }

        /// <summary>
        /// Sets a <c>key = value</c> line in a section, replacing an existing line for the same key and
        /// creating the section when absent. Registry Values lines are written without spaces around the
        /// separator, matching the engine's own export; other sections use <c>key = value</c>.
        /// </summary>
        internal void Set(string sectionName, string key, string value)
        {
            Section section = GetOrAddSection(sectionName);
            bool spaced = !sectionName.Equals(SecurityTemplateSections.RegistryValues, StringComparison.OrdinalIgnoreCase);
            string rendered = spaced ? $"{key} = {value}" : $"{key}={value}";

            for (int i = 0; i < section.Lines.Count; i++)
            {
                if (LineMatchesKey(section.Lines[i], key))
                {
                    section.Lines[i] = rendered;
                    return;
                }
            }

            section.Lines.Add(rendered);
        }

        /// <summary>Serializes the template as UTF-16 with a BOM, the encoding the engine expects.</summary>
        internal void Save(string path)
        {
            var builder = new StringBuilder();

            foreach (string line in _preamble)
                builder.Append(line).Append("\r\n");

            foreach (Section section in _sections)
            {
                builder.Append('[').Append(section.Name).Append(']').Append("\r\n");
                foreach (string line in section.Lines)
                    builder.Append(line).Append("\r\n");
            }

            File.WriteAllText(path, builder.ToString(), new UnicodeEncoding(bigEndian: false, byteOrderMark: true));
        }

        private Section GetOrAddSection(string name)
        {
            foreach (Section section in _sections)
            {
                if (section.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return section;
            }

            // The engine writes [Version] last; keep new sections ahead of it so a template this class
            // creates is laid out the way secpol.msc and secedit lay theirs out.
            var created = new Section(name);
            int versionIndex = _sections.FindIndex(s => s.Name.Equals(SecurityTemplateSections.Version, StringComparison.OrdinalIgnoreCase));
            if (versionIndex >= 0)
                _sections.Insert(versionIndex, created);
            else
                _sections.Add(created);

            return created;
        }

        /// <summary>
        /// Ensures the two sections every template must carry are present: the <c>[Unicode]</c> marker
        /// and a <c>[Version]</c> with the engine's signature. Existing content is left untouched.
        /// </summary>
        private void EnsureHeader()
        {
            if (!HasSection(SecurityTemplateSections.Unicode))
            {
                var unicode = new Section(SecurityTemplateSections.Unicode);
                unicode.Lines.Add("Unicode=yes");
                _sections.Insert(0, unicode);
            }

            if (!HasSection(SecurityTemplateSections.Version))
            {
                var version = new Section(SecurityTemplateSections.Version);
                version.Lines.Add("signature=\"$CHICAGO$\"");
                version.Lines.Add("Revision=1");
                _sections.Add(version);
            }
        }

        private bool HasSection(string name)
        {
            foreach (Section section in _sections)
            {
                if (section.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool LineMatchesKey(string line, string key)
        {
            int separator = line.IndexOf('=');
            if (separator < 0)
                return false;

            return line.AsSpan(0, separator).Trim().Equals(key.AsSpan(), StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Section names used in a Security Configuration Engine template.</summary>
    internal static class SecurityTemplateSections
    {
        internal const string Unicode = "Unicode";
        internal const string Version = "Version";
        internal const string SystemAccess = "System Access";
        internal const string EventAudit = "Event Audit";
        internal const string PrivilegeRights = "Privilege Rights";
        internal const string RegistryValues = "Registry Values";
    }
}
