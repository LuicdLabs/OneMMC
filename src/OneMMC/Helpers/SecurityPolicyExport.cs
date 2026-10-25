using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OneMMC.Core.Abstractions.Services;
using OneMMC.Core.Features.UserSecurity.Models.SecPol;
using OneMMC.Core.Features.UserSecurity.Models.SecPol.SystemAudit;
using OneMMC.Localization;

namespace OneMMC.Helpers
{
    /// <summary>
    /// Exports the rows of a Local Security Policy page, as the tab-separated text file that
    /// secpol.msc's Export List command produces.
    /// </summary>
    internal static class SecurityPolicyExport
    {
        /// <summary>Exports the Account or Local Policies rows the page currently shows.</summary>
        /// <param name="suggestedFileName">The name proposed in the save dialog.</param>
        /// <param name="policies">The rows to write.</param>
        /// <param name="logger">The calling page's logger.</param>
        public static Task ExportPoliciesAsync(
            string? suggestedFileName,
            IReadOnlyList<SecurityPolicyValue> policies,
            ILogger logger)
        {
            ArgumentNullException.ThrowIfNull(policies);

            return ExportAsync(
                suggestedFileName,
                policies.Select(policy => (policy.Definition.DisplayName, policy.DisplaySetting)),
                logger);
        }

        /// <summary>Exports the System Audit rows the page currently shows.</summary>
        /// <param name="suggestedFileName">The name proposed in the save dialog.</param>
        /// <param name="subcategories">The rows to write.</param>
        /// <param name="logger">The calling page's logger.</param>
        public static Task ExportAuditSubcategoriesAsync(
            string? suggestedFileName,
            IReadOnlyList<AuditSubcategoryValue> subcategories,
            ILogger logger)
        {
            ArgumentNullException.ThrowIfNull(subcategories);

            return ExportAsync(
                suggestedFileName,
                subcategories.Select(item => (item.DisplayName, item.DisplaySetting)),
                logger);
        }

        private static async Task ExportAsync(
            string? suggestedFileName,
            IEnumerable<(string Name, string Setting)> rows,
            ILogger logger)
        {
            LocalizedStrings localized = LocalizedStrings.Instance;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
            string? path = await App.GetRequiredService<IFileDialogService>().SaveFileAsync(
                hwnd,
                $"{localized.SecPol_ExportFilter}\0*.txt\0",
                localized.SecPol_ExportButton,
                null,
                "txt",
                suggestedFileName);

            if (string.IsNullOrEmpty(path))
                return;

            var builder = new StringBuilder();
            builder.AppendLine($"{localized.SecPol_Header_Policy}\t{localized.SecPol_Header_SecuritySetting}");
            builder.AppendLine(new string('-', 80));

            int rowCount = 0;
            foreach ((string name, string setting) in rows)
            {
                builder.AppendLine($"{name}\t{setting}");
                ++rowCount;
            }

            File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
            logger.LogInformation("[SecurityPolicyExport] Exported {RowCount} rows to {ExportPath}", rowCount, path);
        }
    }
}
