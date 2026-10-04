using System;
using System.Collections.Generic;
using WmiLight;

namespace OneMMC.Core.Infrastructure.Wmi;

/// <summary>
/// Tolerant property accessors for <see cref="WmiObject"/>. Consolidates the per-file
/// <c>GetWmiPropertySafe</c> helpers that existed for <c>System.Management</c> objects
/// before the repository standardized on WmiLight for Native AOT-safe WMI access
/// (see <c>doc/NativeAot.md</c>, "WMI and CIM").
/// </summary>
internal static class WmiPropertyExtensions
{
    /// <summary>
    /// Reads a property and converts it to <typeparamref name="T"/>, returning
    /// <paramref name="defaultValue"/> when the property is missing, null, or unconvertible.
    /// </summary>
    public static T GetPropertySafe<T>(this WmiObject obj, string propertyName, T defaultValue = default!)
    {
        try
        {
            var value = obj[propertyName];
            if (value == null) return defaultValue;

            var targetType = typeof(T);
            var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            if (underlyingType == typeof(string))
                return (T)(object)(value.ToString()?.Trim() ?? "");

            if (underlyingType == typeof(bool))
                return (T)(object)Convert.ToBoolean(value);

            if (underlyingType == typeof(uint))
                return (T)(object)Convert.ToUInt32(value);

            if (underlyingType == typeof(ushort))
                return (T)(object)Convert.ToUInt16(value);

            if (underlyingType == typeof(ulong))
                return (T)(object)Convert.ToUInt64(value);

            if (underlyingType == typeof(int))
                return (T)(object)Convert.ToInt32(value);

            if (underlyingType == typeof(char))
            {
                var str = value.ToString();
                return (T)(object)(!string.IsNullOrEmpty(str) ? str[0] : '\0');
            }

            return (T)Convert.ChangeType(value, underlyingType);
        }
        catch
        {
            return defaultValue;
        }
    }

    /// <summary>
    /// Reads a WMI string-array property and returns an empty array when the property is
    /// missing, null, or cannot be converted. WMI providers can expose SAFEARRAY values
    /// either as <see cref="string"/> arrays or as a general <see cref="Array"/>.
    /// </summary>
    public static string[] GetStringArrayPropertySafe(this WmiObject obj, string propertyName)
    {
        try
        {
            var value = obj.GetPropertyValue(propertyName);
            if (value is string[] strings)
                return strings;

            if (value is not Array values)
                return [];

            var result = new List<string>(values.Length);
            foreach (var item in values)
            {
                var text = item?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(text))
                    result.Add(text);
            }

            return result.ToArray();
        }
        catch
        {
            return [];
        }
    }
}
