using System;
using System.Collections.Generic;
using System.Globalization;
using OneMMC.Core.Localization;
using Microsoft.UI.Xaml.Controls;

namespace OneMMC.Views.PCManagement;

/// <summary>
/// Formats and parses the human-readable durations shown in the Task Scheduler editors' editable
/// duration combos ("15 minutes", "1 hour", …) in the current UI language. Parsing also accepts the
/// English unit words, so a value typed in either language round-trips to the same <see cref="TimeSpan"/>.
/// </summary>
internal static class TaskDurationText
{
    private static string L(string key) => LocalizationProvider.Current.GetString(ResourceFileNames.TaskSchd, key);

    /// <summary>Localized "Indefinitely" (an unbounded repetition duration).</summary>
    public static string Indefinitely => L(TaskSchdKeys.DurationIndefinitely);

    /// <summary>Localized "Do not wait" (no idle wait timeout).</summary>
    public static string DoNotWait => L(TaskSchdKeys.DurationDoNotWait);

    /// <summary>Localized "Immediately" (delete an expired task without delay).</summary>
    public static string Immediately => L(TaskSchdKeys.DurationImmediately);

    public static string Minutes(int count) => FormatCount(count, TaskSchdKeys.DurationMinute, TaskSchdKeys.DurationMinutes);

    public static string Hours(int count) => FormatCount(count, TaskSchdKeys.DurationHour, TaskSchdKeys.DurationHours);

    public static string Days(int count) => FormatCount(count, TaskSchdKeys.DurationDay, TaskSchdKeys.DurationDays);

    /// <summary>
    /// Formats <paramref name="span"/> in the largest whole unit (days, hours, then minutes);
    /// <see langword="null"/> for a missing or non-positive span.
    /// </summary>
    public static string? Format(TimeSpan? span)
    {
        if (span is not { } v || v <= TimeSpan.Zero)
        {
            return null;
        }
        if (v.TotalDays >= 1 && v.TotalDays == Math.Floor(v.TotalDays))
        {
            return Days((int)v.TotalDays);
        }
        if (v.TotalHours >= 1 && v.TotalHours == Math.Floor(v.TotalHours))
        {
            return Hours((int)v.TotalHours);
        }
        return Minutes((int)v.TotalMinutes);
    }

    /// <summary>
    /// Parses "&lt;number&gt; &lt;unit&gt;" text back into a <see cref="TimeSpan"/>. Blank text, the
    /// Indefinitely / Do not wait / Immediately sentinels, and unrecognized text yield <see langword="null"/>.
    /// </summary>
    public static TimeSpan? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        var numberLength = 0;
        while (numberLength < trimmed.Length && (char.IsAsciiDigit(trimmed[numberLength]) || trimmed[numberLength] == '.'))
        {
            numberLength++;
        }
        if (numberLength == 0 ||
            !double.TryParse(trimmed[..numberLength], NumberStyles.Float, CultureInfo.InvariantCulture, out var count))
        {
            return null;
        }

        var unit = trimmed[numberLength..].Trim();
        if (MatchesUnit(unit, "minute", TaskSchdKeys.DurationMinute, TaskSchdKeys.DurationMinutes))
        {
            return TimeSpan.FromMinutes(count);
        }
        if (MatchesUnit(unit, "hour", TaskSchdKeys.DurationHour, TaskSchdKeys.DurationHours))
        {
            return TimeSpan.FromHours(count);
        }
        if (MatchesUnit(unit, "day", TaskSchdKeys.DurationDay, TaskSchdKeys.DurationDays))
        {
            return TimeSpan.FromDays(count);
        }
        return null;
    }

    /// <summary>Replaces the items of an editable duration combo with <paramref name="choices"/>.</summary>
    public static void Fill(ComboBox combo, IEnumerable<string> choices)
    {
        combo.Items.Clear();
        foreach (var choice in choices)
        {
            combo.Items.Add(choice);
        }
    }

    private static string FormatCount(int count, string singularKey, string pluralKey) =>
        string.Format(CultureInfo.CurrentCulture, L(count == 1 ? singularKey : pluralKey), count);

    // The localized unit word is the format string with its "{0}" placeholder removed.
    private static string UnitWord(string key) => L(key).Replace("{0}", string.Empty, StringComparison.Ordinal).Trim();

    private static bool MatchesUnit(string unit, string english, string singularKey, string pluralKey) =>
        unit.Equals(english, StringComparison.OrdinalIgnoreCase) ||
        unit.Equals(english + "s", StringComparison.OrdinalIgnoreCase) ||
        unit.Equals(UnitWord(singularKey), StringComparison.OrdinalIgnoreCase) ||
        unit.Equals(UnitWord(pluralKey), StringComparison.OrdinalIgnoreCase);
}
