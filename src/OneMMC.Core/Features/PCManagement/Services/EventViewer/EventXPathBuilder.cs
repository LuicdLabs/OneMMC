using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace OneMMC.Core.Features.PCManagement.Services.EventViewer;

/// <summary>One channel to query, optionally narrowed to specific providers (for a "By source" filter).</summary>
public sealed class ChannelSelection
{
    /// <summary>The channel (log) path, e.g. "Application" or "Microsoft-Windows-PowerShell/Operational".</summary>
    public required string Channel { get; init; }

    /// <summary>Providers to match within the channel; empty selects the whole channel ("By log").</summary>
    public IReadOnlyList<string> Providers { get; init; } = [];
}

/// <summary>
/// Builds an Event Log structured <c>QueryList</c> subscription from a structured filter, matching the
/// XML that Event Viewer's "Create Custom View" and the Task Scheduler "On an event" trigger
/// (IEventTrigger.Subscription) produce. Pure logic — channel resolution for "By source" happens in
/// <see cref="EventSourceChannelResolver"/> and the resolved channels are passed in via
/// <see cref="Criteria.Selections"/>.
/// </summary>
/// <remarks>
/// Per the Query Schema, a single <c>&lt;Query&gt;</c> holds one <c>&lt;Select&gt;</c> per channel
/// (events to include) plus a matching <c>&lt;Suppress&gt;</c> per channel for events to exclude — so
/// excluded event IDs are emitted as a Suppress on the same Path, not negated inside the Select.
/// </remarks>
public static class EventXPathBuilder
{
    /// <summary>The structured filter the builder turns into a query.</summary>
    public sealed class Criteria
    {
        /// <summary>The channels to query (with optional per-channel provider filters). Empty = Application.</summary>
        public IList<ChannelSelection> Selections { get; } = new List<ChannelSelection>();

        /// <summary>Event level values to include (1=Critical, 2=Error, 3=Warning, 4=Information, 5=Verbose). Empty = all.</summary>
        public IList<int> Levels { get; } = new List<int>();

        /// <summary>Event-ID expression such as "1,3,5-99,-76" (minus prefix excludes). Optional.</summary>
        public string? EventIds { get; set; }

        /// <summary>The user (SID) to filter on. Optional.</summary>
        public string? UserSid { get; set; }

        /// <summary>The computer name to filter on. Optional.</summary>
        public string? Computer { get; set; }

        /// <summary>Only include events logged within this window (relative time). Optional.</summary>
        public TimeSpan? WithinLast { get; set; }

        /// <summary>Lower bound (UTC) for a custom logged range. Optional; overrides <see cref="WithinLast"/>.</summary>
        public DateTime? FromUtc { get; set; }

        /// <summary>Upper bound (UTC) for a custom logged range. Optional; overrides <see cref="WithinLast"/>.</summary>
        public DateTime? ToUtc { get; set; }

        /// <summary>Combined keyword bitmask to match with <c>band(Keywords, mask)</c>. 0 = no keyword filter.</summary>
        public long KeywordsMask { get; set; }
    }

    /// <summary>Builds the full <c>QueryList</c> subscription string for the given criteria.</summary>
    public static string BuildQueryList(Criteria criteria)
    {
        var selections = criteria.Selections.Count > 0
            ? criteria.Selections
            : new List<ChannelSelection> { new() { Channel = "Application" } };

        var (includeId, excludeId) = BuildEventIdClauses(criteria.EventIds);
        var sharedTerms = BuildSharedSystemTerms(criteria, includeId);

        var sb = new StringBuilder();
        sb.Append("<QueryList>");
        sb.Append(CultureInfo.InvariantCulture, $"<Query Id=\"0\" Path=\"{Escape(selections[0].Channel)}\">");

        foreach (var selection in selections)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"<Select Path=\"{Escape(selection.Channel)}\">{BuildSelectBody(selection, sharedTerms)}</Select>");
        }

        // A Suppress must share a Path with a Select; emit one per selected channel when there are excludes.
        if (excludeId is not null)
        {
            foreach (var selection in selections)
            {
                sb.Append(CultureInfo.InvariantCulture,
                    $"<Suppress Path=\"{Escape(selection.Channel)}\">*[System[{excludeId}]]</Suppress>");
            }
        }

        sb.Append("</Query></QueryList>");
        return sb.ToString();
    }

    private static string BuildSelectBody(ChannelSelection selection, IReadOnlyList<string> sharedTerms)
    {
        var terms = new List<string>();
        if (selection.Providers.Count > 0)
        {
            var providers = string.Join(" or ", selection.Providers.Select(p => $"@Name='{Escape(p)}'"));
            terms.Add($"Provider[{providers}]");
        }
        terms.AddRange(sharedTerms);
        return terms.Count == 0 ? "*" : $"*[System[{string.Join(" and ", terms)}]]";
    }

    // The System[...] terms shared by every Select (everything except the per-channel Provider clause
    // and the excluded event IDs, which go in the Suppress).
    private static List<string> BuildSharedSystemTerms(Criteria criteria, string? includeId)
    {
        var terms = new List<string>();

        var levelClause = BuildLevelClause(criteria.Levels);
        if (levelClause is not null)
        {
            terms.Add(levelClause);
        }

        if (includeId is not null)
        {
            terms.Add(includeId);
        }

        if (criteria.KeywordsMask != 0)
        {
            // Keyword filtering uses the Event Log XPath band() (bitwise-and) against the selected bits.
            terms.Add($"band(Keywords,{criteria.KeywordsMask})");
        }

        var timeClause = BuildTimeClause(criteria);
        if (timeClause is not null)
        {
            terms.Add(timeClause);
        }

        if (!string.IsNullOrWhiteSpace(criteria.Computer))
        {
            terms.Add($"Computer='{Escape(criteria.Computer)}'");
        }

        if (!string.IsNullOrWhiteSpace(criteria.UserSid))
        {
            terms.Add($"Security[@UserID='{Escape(criteria.UserSid)}']");
        }

        return terms;
    }

    // A custom From/To range (absolute UTC bounds) takes precedence over a relative "within last" window.
    private static string? BuildTimeClause(Criteria criteria)
    {
        if (criteria.FromUtc is not null || criteria.ToUtc is not null)
        {
            var bounds = new List<string>();
            if (criteria.FromUtc is { } from)
            {
                bounds.Add($"@SystemTime&gt;='{FormatSystemTime(from)}'");
            }
            if (criteria.ToUtc is { } to)
            {
                bounds.Add($"@SystemTime&lt;='{FormatSystemTime(to)}'");
            }
            return $"TimeCreated[{string.Join(" and ", bounds)}]";
        }

        if (criteria.WithinLast is { } window)
        {
            var ms = (long)window.TotalMilliseconds;
            return $"TimeCreated[timediff(@SystemTime) &lt;= {ms}]";
        }

        return null;
    }

    // Event Log <System><TimeCreated SystemTime="..."> is UTC ISO-8601; match that exact shape.
    private static string FormatSystemTime(DateTime value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private static string? BuildLevelClause(IList<int> levels)
    {
        if (levels.Count == 0)
        {
            return null;
        }
        var terms = levels.Distinct().Select(l => $"Level={l}");
        return $"({string.Join(" or ", terms)})";
    }

    // Splits "1,3,5-99,-76" into the include clause (for the Select) and the exclude clause (for the
    // Suppress). Both are parenthesised; either may be null when that side has no terms.
    private static (string? Include, string? Exclude) BuildEventIdClauses(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return (null, null);
        }

        var includes = new List<string>();
        var excludes = new List<string>();

        foreach (var rawToken in expression.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var token = rawToken;
            var exclude = token.StartsWith('-');
            if (exclude)
            {
                token = token[1..].Trim();
            }

            var target = exclude ? excludes : includes;
            if (token.Contains('-', StringComparison.Ordinal))
            {
                var bounds = token.Split('-', 2);
                if (int.TryParse(bounds[0], out var lo) && int.TryParse(bounds[1], out var hi))
                {
                    target.Add($"(EventID&gt;={lo} and EventID&lt;={hi})");
                }
            }
            else if (int.TryParse(token, out var single))
            {
                target.Add($"EventID={single}");
            }
        }

        return (Parenthesize(includes), Parenthesize(excludes));
    }

    private static string? Parenthesize(IReadOnlyCollection<string> terms) =>
        terms.Count == 0 ? null : $"({string.Join(" or ", terms)})";

    private static string Escape(string value) =>
        value.Replace("&", "&amp;").Replace("'", "&apos;").Replace("<", "&lt;").Replace(">", "&gt;");
}
