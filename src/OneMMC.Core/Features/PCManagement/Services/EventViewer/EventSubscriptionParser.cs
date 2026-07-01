using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace OneMMC.Core.Features.PCManagement.Services.EventViewer;

/// <summary>The parts of a "Basic" event subscription: a single channel with an optional single source and event ID.</summary>
/// <param name="Channel">The channel (log) path, e.g. "System".</param>
/// <param name="Source">The single provider/source name, or <see langword="null"/> when unfiltered.</param>
/// <param name="EventId">The single event ID, or <see langword="null"/> when unfiltered.</param>
public sealed record BasicEventQuery(string Channel, string? Source, string? EventId);

/// <summary>
/// Parses an Event Log <c>QueryList</c> subscription (as stored on an event trigger) to decide whether it
/// is expressible in the Task Scheduler "Basic" form — a single log, an optional single source, and an
/// optional single event ID — matching how taskschd.msc chooses Basic vs Custom when editing a trigger.
/// Anything richer (levels, keywords, time bounds, computer/user, multiple selects/providers/IDs, excludes)
/// is treated as Custom. The recognised Basic shape mirrors <see cref="EventXPathBuilder"/>'s output so a
/// query this app generates round-trips back to Basic.
/// </summary>
public static partial class EventSubscriptionParser
{
    // A single Provider name clause, e.g. Provider[@Name='Microsoft-Windows-Kernel-General'].
    [GeneratedRegex(@"^Provider\[@Name='(?<name>[^']*)'\]$", RegexOptions.CultureInvariant)]
    private static partial Regex ProviderClauseRegex();

    // A single event-ID equality, optionally parenthesised, e.g. (EventID=127) or EventID=127.
    [GeneratedRegex(@"^\(?EventID=(?<id>\d+)\)?$", RegexOptions.CultureInvariant)]
    private static partial Regex EventIdClauseRegex();

    /// <summary>
    /// Attempts to parse <paramref name="subscription"/> as a Basic query. Returns <see langword="true"/>
    /// and the channel/source/event-ID parts when the subscription matches the Basic shape; otherwise
    /// returns <see langword="false"/> (the caller should treat it as Custom).
    /// </summary>
    public static bool TryParseBasic(string? subscription, [NotNullWhen(true)] out BasicEventQuery? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(subscription))
        {
            return false;
        }

        XDocument doc;
        try
        {
            doc = XDocument.Parse(subscription);
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }

        if (doc.Root is not { Name.LocalName: "QueryList" } root)
        {
            return false;
        }

        // Basic is exactly one Query holding exactly one Select and nothing else (no Suppress, no extra Queries).
        var queries = root.Elements().Where(e => e.Name.LocalName == "Query").ToList();
        if (queries.Count != 1)
        {
            return false;
        }

        var children = queries[0].Elements().ToList();
        if (children.Count != 1 || children[0].Name.LocalName != "Select")
        {
            return false;
        }

        var select = children[0];
        var channel = select.Attribute("Path")?.Value;
        if (string.IsNullOrWhiteSpace(channel))
        {
            return false;
        }

        if (!TryParseSelectBody(select.Value, out var source, out var eventId))
        {
            return false;
        }

        result = new BasicEventQuery(channel, source, eventId);
        return true;
    }

    // Accepts the Basic Select bodies EventXPathBuilder produces: "*" (no filter), or
    // "*[System[<terms>]]" where <terms> is at most one Provider clause and at most one EventID equality
    // joined by " and ". Any other content (levels, band(), TimeCreated, Computer, Security, ranges,
    // multiple providers/IDs) means the query is Custom.
    private static bool TryParseSelectBody(string body, out string? source, out string? eventId)
    {
        source = null;
        eventId = null;

        body = body.Trim();
        if (body is "*" or "*[System]" or "*[System[]]")
        {
            return true;
        }

        const string prefix = "*[System[";
        const string suffix = "]]";
        if (!body.StartsWith(prefix, StringComparison.Ordinal) || !body.EndsWith(suffix, StringComparison.Ordinal))
        {
            return false;
        }

        var inner = body[prefix.Length..^suffix.Length].Trim();
        if (inner.Length == 0)
        {
            return true;
        }

        // The Basic shape never uses " or " (a single provider/ID); splitting on top-level " and " is safe.
        foreach (var rawTerm in inner.Split(" and ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var providerMatch = ProviderClauseRegex().Match(rawTerm);
            if (providerMatch.Success)
            {
                if (source is not null)
                {
                    return false; // more than one source → Custom
                }
                source = Unescape(providerMatch.Groups["name"].Value);
                continue;
            }

            var idMatch = EventIdClauseRegex().Match(rawTerm);
            if (idMatch.Success)
            {
                if (eventId is not null)
                {
                    return false; // more than one event ID → Custom
                }
                eventId = idMatch.Groups["id"].Value;
                continue;
            }

            // An unrecognised term (Level, band(...), TimeCreated, Computer, Security, a range, …) → Custom.
            return false;
        }

        return true;
    }

    // Reverses the XML entity escaping EventXPathBuilder applies to provider names.
    private static string Unescape(string value) =>
        value.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&apos;", "'").Replace("&amp;", "&");
}
