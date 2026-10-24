using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OneMMC.Core.Features.UserSecurity.Models.SecPol;

namespace OneMMC.Core.Features.UserSecurity.Services.SecPol
{
    /// <summary>
    /// Pairs a policy with its Explain text from the Windows security modules.
    /// <para>
    /// Every explain string in <c>wsecedit.dll</c> and <c>scecli.dll</c> has the shape
    /// <c>DisplayName\r\n\r\nExplainText</c>. The snap-in reaches its explain string
    /// through an identifier private to the binary, so this index pairs the two by
    /// display name instead and stores no resource IDs ??which is what keeps policies
    /// added by a Windows update working with no change here.
    /// </para>
    /// <para>
    /// The pairing is made in the invariant (<c>en-US</c>) resources and only then read
    /// back in the user's language. A policy's short name and the title of its explain
    /// text are separate resources that Windows does not keep identical ??punctuation,
    /// apostrophe style and trailing periods differ, and a few titles are a longer or
    /// reworded form of the name ??so the match has to tolerate small differences, and
    /// doing that in one fixed language keeps the result independent of how a particular
    /// translation worded either string. The localized explain text is then loaded from
    /// the resource ID the invariant match identified.
    /// </para>
    /// <para>
    /// Where the invariant resources are unavailable, the same tolerant match is applied
    /// directly to the localized strings. A policy with no sufficiently similar title
    /// simply has no explain text, which the UI shows as an empty Explain tab.
    /// </para>
    /// </summary>
    internal sealed class SecurityPolicyExplainIndex
    {
        /// <summary>
        /// Shortest normalized prefix accepted as a match. Policy names open with a
        /// category ("Network security: ..."), so a short shared prefix means nothing.
        /// </summary>
        private const int MinimumPrefixLength = 12;

        /// <summary>
        /// Smallest share of character pairs two texts must have in common to be treated
        /// as the same policy. Character pairs are used rather than words because not
        /// every language separates words, and the threshold is deliberately high: an
        /// unmatched policy loses its explain text, but a wrong match would show another
        /// policy's.
        /// </summary>
        private const double MinimumSimilarity = 0.55;

        private static ILogger _logger = NullLogger.Instance;

        private readonly Lazy<Dictionary<string, PolicyExplainSource>> _invariantIndex;
        private readonly Lazy<Dictionary<string, string>> _localizedIndex;

        internal static SecurityPolicyExplainIndex Instance { get; } = new();

        private SecurityPolicyExplainIndex()
        {
            _invariantIndex = new Lazy<Dictionary<string, PolicyExplainSource>>(
                BuildInvariantIndex,
                LazyThreadSafetyMode.ExecutionAndPublication);

            _localizedIndex = new Lazy<Dictionary<string, string>>(
                BuildLocalizedIndex,
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        internal static void ConfigureLogger(ILogger logger)
        {
            _logger = logger ?? NullLogger.Instance;
        }

        /// <summary>
        /// Returns the localized explain text for a policy, or an empty string when the
        /// modules carry no matching text.
        /// </summary>
        internal string GetExplainText(SecurityPolicyDefinition definition)
        {
            if (definition is null)
                return string.Empty;

            string? invariantName = GetInvariantDisplayName(definition);
            if (invariantName is not null &&
                TryMatch(_invariantIndex.Value, invariantName, out PolicyExplainSource source) &&
                TryLoadLocalizedExplainText(source, out string localized))
            {
                return localized;
            }

            if (TryMatch(_localizedIndex.Value, definition.DisplayName, out string? fallback))
                return fallback!;

            _logger.LogDebug("[SecurityPolicyExplainIndex] No explain text for '{DisplayName}'", definition.DisplayName);
            return string.Empty;
        }

        /// <summary>
        /// Resolves the policy's name in the invariant language, which is the key the
        /// pairing is made on. Item strings and the registry catalog both expose a
        /// resource that can be re-read in another language; a name Windows produced some
        /// other way, such as a privilege display name, has no invariant form.
        /// </summary>
        private static string? GetInvariantDisplayName(SecurityPolicyDefinition definition)
        {
            SecurityPolicyResourceLoader loader = SecurityPolicyResourceLoader.Instance;

            if (definition.ItemId > 0)
            {
                PolicyItemText itemText = loader.LoadInvariantItemText(definition.ItemId);
                if (itemText.HasName)
                    return itemText.Name;
            }

            if (!string.IsNullOrEmpty(definition.DisplayNameReference) &&
                SecurityPolicyResourceLoader.TryParseResourceReference(definition.DisplayNameReference, out string moduleName, out int resourceId))
            {
                string? name = loader.LoadInvariantString(moduleName, resourceId);
                if (!string.IsNullOrWhiteSpace(name))
                    return name.Trim();
            }

            if (!string.IsNullOrEmpty(definition.PrivilegeConstant))
            {
                string? name = loader.ResolveInvariantPrivilegeDisplayName(definition.PrivilegeConstant);
                if (!string.IsNullOrWhiteSpace(name))
                    return name.Trim();
            }

            return null;
        }

        private static bool TryLoadLocalizedExplainText(PolicyExplainSource source, out string explainText)
        {
            explainText = string.Empty;

            string? raw = SecurityPolicyResourceLoader.Instance.LoadString(source.ModuleName, source.ResourceId);
            if (raw is null)
                return false;

            return SecurityPolicyResourceLoader.TrySplitExplainString(raw, out _, out explainText);
        }

        #region Matching

        private static bool TryMatch<TValue>(Dictionary<string, TValue> index, string displayName, out TValue? match)
        {
            match = default;

            if (string.IsNullOrWhiteSpace(displayName))
                return false;

            string normalized = Normalize(displayName);
            if (normalized.Length == 0)
                return false;

            if (index.TryGetValue(normalized, out TValue? exact))
            {
                match = exact;
                return true;
            }

            return TryMatchByPrefix(index, normalized, out match)
                || TryMatchBySimilarity(index, normalized, out match);
        }

        private static bool TryMatchByPrefix<TValue>(Dictionary<string, TValue> index, string normalized, out TValue? match)
        {
            match = default;
            int bestLength = 0;

            foreach ((string candidate, TValue value) in index)
            {
                if (!candidate.StartsWith(normalized, StringComparison.Ordinal) &&
                    !normalized.StartsWith(candidate, StringComparison.Ordinal))
                {
                    continue;
                }

                int sharedLength = Math.Min(candidate.Length, normalized.Length);
                if (sharedLength > bestLength)
                {
                    bestLength = sharedLength;
                    match = value;
                }
            }

            if (bestLength >= MinimumPrefixLength)
                return true;

            match = default;
            return false;
        }

        private static bool TryMatchBySimilarity<TValue>(Dictionary<string, TValue> index, string normalized, out TValue? match)
        {
            match = default;

            HashSet<string> pairs = GetCharacterPairs(normalized);
            if (pairs.Count == 0)
                return false;

            double bestScore = 0;
            int bestCount = 0;

            foreach ((string candidate, TValue value) in index)
            {
                double score = Similarity(pairs, candidate);
                if (score > bestScore + double.Epsilon)
                {
                    bestScore = score;
                    bestCount = 1;
                    match = value;
                }
                else if (Math.Abs(score - bestScore) <= double.Epsilon)
                {
                    bestCount++;
                }
            }

            // An ambiguous best match is worse than none: it would attach another
            // policy's explain text to this one.
            if (bestScore < MinimumSimilarity || bestCount != 1)
            {
                match = default;
                return false;
            }

            return true;
        }

        private static double Similarity(HashSet<string> pairs, string candidate)
        {
            HashSet<string> candidatePairs = GetCharacterPairs(candidate);
            if (candidatePairs.Count == 0)
                return 0;

            int shared = 0;
            foreach (string pair in candidatePairs)
            {
                if (pairs.Contains(pair))
                    shared++;
            }

            int union = pairs.Count + candidatePairs.Count - shared;
            return union == 0 ? 0 : shared / (double)union;
        }

        /// <summary>
        /// Splits text into overlapping character pairs, a measure of similarity that
        /// works whether or not the language separates words.
        /// </summary>
        private static HashSet<string> GetCharacterPairs(string value)
        {
            var pairs = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index + 1 < value.Length; index++)
            {
                pairs.Add(value.Substring(index, 2));
            }

            return pairs;
        }

        /// <summary>
        /// Reduces a display name to lower-case words so that punctuation, hyphenation
        /// and apostrophe style cannot keep two spellings of the same name apart.
        /// </summary>
        private static string Normalize(string value)
        {
            var builder = new StringBuilder(value.Length);
            bool pendingSeparator = false;

            foreach (char current in value)
            {
                if (char.IsLetterOrDigit(current))
                {
                    if (pendingSeparator && builder.Length > 0)
                        builder.Append(' ');

                    builder.Append(char.ToLowerInvariant(current));
                    pendingSeparator = false;
                }
                else
                {
                    pendingSeparator = true;
                }
            }

            return builder.ToString();
        }

        #endregion

        #region Index construction

        private static readonly string[] ExplainModules =
        [
            SecurityPolicyResourceLoader.SnapInModuleName,
            SecurityPolicyResourceLoader.EngineModuleName
        ];

        private static Dictionary<string, PolicyExplainSource> BuildInvariantIndex()
        {
            var index = new Dictionary<string, PolicyExplainSource>(StringComparer.Ordinal);
            SecurityPolicyResourceLoader loader = SecurityPolicyResourceLoader.Instance;

            foreach (string moduleName in ExplainModules)
            {
                int added = 0;
                foreach ((int resourceId, string raw) in loader.EnumerateStrings(moduleName, invariant: true))
                {
                    if (!SecurityPolicyResourceLoader.TrySplitExplainString(raw, out string displayName, out _))
                        continue;

                    string key = Normalize(displayName);
                    if (key.Length == 0 || index.ContainsKey(key))
                        continue;

                    index[key] = new PolicyExplainSource(moduleName, resourceId);
                    added++;
                }

                _logger.LogDebug("[SecurityPolicyExplainIndex] Indexed {Count} invariant explain strings from {ModuleName}", added, moduleName);
            }

            return index;
        }

        private static Dictionary<string, string> BuildLocalizedIndex()
        {
            var index = new Dictionary<string, string>(StringComparer.Ordinal);
            SecurityPolicyResourceLoader loader = SecurityPolicyResourceLoader.Instance;

            foreach (string moduleName in ExplainModules)
            {
                foreach ((int _, string raw) in loader.EnumerateStrings(moduleName))
                {
                    if (!SecurityPolicyResourceLoader.TrySplitExplainString(raw, out string displayName, out string explainText))
                        continue;

                    string key = Normalize(displayName);
                    if (key.Length == 0 || index.ContainsKey(key))
                        continue;

                    index[key] = explainText;
                }
            }

            _logger.LogDebug("[SecurityPolicyExplainIndex] Indexed {Count} localized explain strings", index.Count);
            return index;
        }

        #endregion

        /// <summary>The module and string resource that hold a policy's explain text.</summary>
        private readonly struct PolicyExplainSource
        {
            internal PolicyExplainSource(string moduleName, int resourceId)
            {
                ModuleName = moduleName;
                ResourceId = resourceId;
            }

            internal string ModuleName { get; }

            internal int ResourceId { get; }
        }
    }
}
