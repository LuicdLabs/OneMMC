using System;
using System.Globalization;

namespace OneMMC.Core.Features.SystemManagement.Services.WF.Profiles;

internal static class ValueConverter
{
    internal static bool ReadGpoBool(object? value)
        => ConvertToUInt16(value, 0) == 1;

    internal static ushort ToGpoBool(bool value)
        => value ? (ushort)1 : (ushort)0;

    internal static ushort ConvertToUInt16(object? value, ushort defaultValue)
    {
        if (value is null)
        {
            return defaultValue;
        }

        try
        {
            return Convert.ToUInt16(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return defaultValue;
        }
    }

    internal static uint ConvertToUInt32(object? value, uint defaultValue)
    {
        if (value is null)
        {
            return defaultValue;
        }

        try
        {
            return Convert.ToUInt32(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return defaultValue;
        }
    }

    internal static bool ConvertToBoolean(object? value, bool defaultValue)
    {
        if (value is null)
        {
            return defaultValue;
        }

        try
        {
            return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return defaultValue;
        }
    }

    internal static ulong ConvertToUInt64(object? value, ulong defaultValue)
    {
        if (value is null)
        {
            return defaultValue;
        }

        try
        {
            return Convert.ToUInt64(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return defaultValue;
        }
    }
}
