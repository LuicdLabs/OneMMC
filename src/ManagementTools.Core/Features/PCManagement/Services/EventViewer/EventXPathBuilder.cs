using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ManagementTools.Core.Features.PCManagement.Services.EventViewer;

/// <summary>
/// Builds an Event Log XPath / <c>QueryList</c> subscription from a structured filter, matching the
/// query format that Event Viewer's "Create Custom View" and the Task Scheduler "On an event" trigger
/// (IEventTrigger.Subscription) produce. Pure logic, reusable by the event-filter dialog and elsewhere.
/// </summary>
public static class EventXPathBuilder
{
    /// <summary>The structured filter the builder turns into a query.</summary>
    public sealed class Criteria
    {
        /// <summary>Channels (logs) to query when filtering by log.</summary>
        public IList<string> Logs { get; } = new List<string>();

        /// <summary>Provider (source) names to filter on when filtering by source.</summary>
        public IList<string> Sources { get; } = new List<string>();

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

    /// <summary>Builds the bare XPath predicate body (the part inside <c>*[...]</c>).</summary>
    public static string BuildXPath(Criteria criteria)
    {
        var system = new List<string>();

        if (criteria.Sources.Count > 0)
        {
            var providers = string.Join(" or ", criteria.Sources.Select(s => $"@Name='{Escape(s)}'"));
            system.Add($"Provider[{providers}]");
        }

        var levelClause = BuildLevelClause(criteria.Levels);
        if (levelClause is not null)
        {
            system.Add(levelClause);
        }

        var idClause = BuildEventIdClause(criteria.EventIds);
        if (idClause is not null)
        {
            system.Add(idClause);
        }

        if (criteria.KeywordsMask != 0)
        {
            // Keyword filtering uses the Event Log XPath band() (bitwise-and) function against the
            // reserved Microsoft keyword bits the user selected.
            system.Add($"band(Keywords,{criteria.KeywordsMask})");
        }

        var timeClause = BuildTimeClause(criteria);
        if (timeClause is not null)
        {
            system.Add(timeClause);
        }

        if (!string.IsNullOrWhiteSpace(criteria.Computer))
        {
            system.Add($"Computer='{Escape(criteria.Computer)}'");
        }

        if (!string.IsNullOrWhiteSpace(criteria.UserSid))
        {
            system.Add($"Security[@UserID='{Escape(criteria.UserSid)}']");
        }

        return system.Count == 0 ? "*" : $"*[System[{string.Join(" and ", system)}]]";
    }

    /// <summary>Builds a full <c>QueryList</c> subscription string for the given criteria.</summary>
    public static string BuildQueryList(Criteria criteria)
    {
        var xpath = BuildXPath(criteria);
        var paths = criteria.Logs.Count > 0 ? criteria.Logs : new List<string> { "Application" };

        var sb = new StringBuilder();
        sb.Append("<QueryList>");
        int id = 0;
        foreach (var path in paths)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<Query Id=\"{id}\" Path=\"{Escape(path)}\">");
            sb.Append(CultureInfo.InvariantCulture, $"<Select Path=\"{Escape(path)}\">{xpath}</Select>");
            sb.Append("</Query>");
            id++;
        }
        sb.Append("</QueryList>");
        return sb.ToString();
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

    private static string? BuildEventIdClause(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return null;
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

            if (token.Contains('-', StringComparison.Ordinal))
            {
                var bounds = token.Split('-', 2);
                if (int.TryParse(bounds[0], out var lo) && int.TryParse(bounds[1], out var hi))
                {
                    var clause = $"(EventID &gt;= {lo} and EventID &lt;= {hi})";
                    (exclude ? excludes : includes).Add(exclude ? $"not{clause}" : clause);
                }
            }
            else if (int.TryParse(token, out var single))
            {
                (exclude ? excludes : includes).Add(exclude ? $"EventID!={single}" : $"EventID={single}");
            }
        }

        var parts = new List<string>();
        if (includes.Count > 0)
        {
            parts.Add($"({string.Join(" or ", includes)})");
        }
        parts.AddRange(excludes);
        return parts.Count == 0 ? null : string.Join(" and ", parts);
    }

    private static string Escape(string value) =>
        value.Replace("&", "&amp;").Replace("'", "&apos;").Replace("<", "&lt;").Replace(">", "&gt;");
}
