using System;
using System.Globalization;

namespace OneMMC.Core.Infrastructure.Wmi;

/// <summary>
/// Converts DMTF (CIM) datetime strings (<c>yyyyMMddHHmmss.ffffff±UUU</c>) to <see cref="DateTime"/>.
/// Replaces <c>System.Management.ManagementDateTimeConverter.ToDateTime</c> for WmiLight call
/// sites — WmiLight surfaces CIM datetime properties as their raw DMTF strings. Semantics match
/// the original converter: wildcard (<c>*</c>) fields fall back to <see cref="DateTime.MinValue"/>
/// components and the embedded UTC-offset minutes are normalized to local time.
/// </summary>
internal static class DmtfDateTimeConverter
{
    private const int DmtfDateTimeLength = 25;

    /// <summary>
    /// Parses a DMTF datetime string into a local-time <see cref="DateTime"/>.
    /// </summary>
    /// <param name="dmtfDate">The 25-character DMTF datetime string.</param>
    /// <returns>The parsed value expressed in local time.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The string is not a valid DMTF datetime.</exception>
    public static DateTime ToDateTime(string dmtfDate)
    {
        ArgumentNullException.ThrowIfNull(dmtfDate);

        string dmtf = dmtfDate.Trim();
        if (dmtf.Length != DmtfDateTimeLength || dmtf[14] != '.')
        {
            throw new ArgumentOutOfRangeException(nameof(dmtfDate), dmtfDate, "Not a valid DMTF datetime.");
        }

        DateTime min = DateTime.MinValue;
        int year = ParseField(dmtf, 0, 4, min.Year);
        int month = ParseField(dmtf, 4, 2, min.Month);
        int day = ParseField(dmtf, 6, 2, min.Day);
        int hour = ParseField(dmtf, 8, 2, min.Hour);
        int minute = ParseField(dmtf, 10, 2, min.Minute);
        int second = ParseField(dmtf, 12, 2, min.Second);

        long microsecondTicks = 0;
        string microseconds = dmtf.Substring(15, 6);
        if (microseconds != "******")
        {
            if (!long.TryParse(microseconds, NumberStyles.None, CultureInfo.InvariantCulture, out long parsedMicroseconds))
            {
                throw new ArgumentOutOfRangeException(nameof(dmtfDate), dmtfDate, "Not a valid DMTF datetime.");
            }

            microsecondTicks = parsedMicroseconds * (TimeSpan.TicksPerMillisecond / 1000);
        }

        DateTime result;
        try
        {
            result = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Local).AddTicks(microsecondTicks);
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentOutOfRangeException(nameof(dmtfDate), dmtfDate, ex.Message);
        }

        // The trailing "±UUU" carries the value's UTC offset in minutes; shift by the difference
        // to the local zone's offset so the result is expressed in local time (original behavior).
        string offsetDigits = dmtf.Substring(22, 3);
        if (offsetDigits != "***")
        {
            if (!int.TryParse(dmtf.Substring(21, 4), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int utcOffsetMinutes))
            {
                throw new ArgumentOutOfRangeException(nameof(dmtfDate), dmtfDate, "Not a valid DMTF datetime.");
            }

            long localOffsetMinutes = TimeZoneInfo.Local.GetUtcOffset(result).Ticks / TimeSpan.TicksPerMinute;
            result = result.AddMinutes(localOffsetMinutes - utcOffsetMinutes);
        }

        return result;
    }

    private static int ParseField(string dmtf, int start, int length, int wildcardValue)
    {
        string field = dmtf.Substring(start, length);
        if (field == new string('*', length))
        {
            return wildcardValue;
        }

        if (!int.TryParse(field, NumberStyles.None, CultureInfo.InvariantCulture, out int value))
        {
            throw new ArgumentOutOfRangeException(nameof(dmtf), dmtf, "Not a valid DMTF datetime.");
        }

        return value;
    }
}
