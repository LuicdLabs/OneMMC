using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OneMMC.Core.Features.UserSecurity.Services.SecPol
{
    /// <summary>
    /// A Security Configuration Engine security template: the UTF-16 INF text the engine parses with
    /// <c>SceOpenProfile</c> and produces with <c>SceWriteSecurityProfileInfo</c>. Each section holds
    /// <c>key = value</c> lines; <c>[Registry Values]</c> lines carry no spaces around the separator.
    /// </summary>
    internal sealed class SecurityTemplate
    {
        private readonly Dictionary<string, Dictionary<string, string>> _sections = new(StringComparer.OrdinalIgnoreCase);

        private SecurityTemplate()
        {
        }

        /// <summary>Parses a template written by the engine.</summary>
        internal static SecurityTemplate Load(string path)
        {
            var template = new SecurityTemplate();
            Dictionary<string, string>? current = null;

            // Security templates are UTF-16; the BOM lets the reader confirm it.
            foreach (string line in File.ReadAllLines(path, Encoding.Unicode))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith('[') && trimmed.EndsWith(']') && trimmed.Length > 2)
                {
                    string name = trimmed[1..^1].Trim();
                    if (!template._sections.TryGetValue(name, out current))
                    {
                        current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        template._sections[name] = current;
                    }

                    continue;
                }

                int separator = trimmed.IndexOf('=');
                if (current is null || separator <= 0)
                    continue;

                current[trimmed[..separator].Trim()] = trimmed[(separator + 1)..].Trim();
            }

            return template;
        }

        /// <summary>Gets the value of a key, or false when the template does not configure it.</summary>
        internal bool TryGetValue(string section, string key, out string value)
        {
            value = string.Empty;
            return _sections.TryGetValue(section, out Dictionary<string, string>? lines)
                && lines.TryGetValue(key, out value!);
        }

        /// <summary>
        /// Writes a template configuring a single setting, with the <c>[Unicode]</c> and <c>[Version]</c>
        /// sections every template carries, as UTF-16 with a BOM.
        /// </summary>
        internal static void WriteSingleSetting(string path, SecurityTemplateEdit edit)
        {
            bool spaced = !edit.Section.Equals(SecurityTemplateSections.RegistryValues, StringComparison.OrdinalIgnoreCase);
            string line = spaced ? $"{edit.Key} = {edit.Value}" : $"{edit.Key}={edit.Value}";

            string text =
                "[Unicode]\r\nUnicode=yes\r\n" +
                $"[{edit.Section}]\r\n{line}\r\n" +
                "[Version]\r\nsignature=\"$CHICAGO$\"\r\nRevision=1\r\n";

            File.WriteAllText(path, text, new UnicodeEncoding(bigEndian: false, byteOrderMark: true));
        }
    }

    /// <summary>Section names used in a Security Configuration Engine template.</summary>
    internal static class SecurityTemplateSections
    {
        internal const string SystemAccess = "System Access";
        internal const string EventAudit = "Event Audit";
        internal const string PrivilegeRights = "Privilege Rights";
        internal const string RegistryValues = "Registry Values";
    }
}
