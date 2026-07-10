using System;
using System.Globalization;
using System.Xml;

namespace OneMMC.Core.Features.PolicyManagement.Services.GpEdit.Parsers
{
    public static class XmlExtensions
    {
        public static string? AttributeOrNull(this XmlNode node, string attribute)
        {
            var attr = node.Attributes?[attribute];
            if (attr == null) return null;
            return attr.Value;
        }

        public static T AttributeOrDefault<T>(this XmlNode node, string attribute, T defaultVal)
        {
            var attr = node.Attributes?[attribute];
            if (attr == null) return defaultVal;

            return ConvertOrDefault(attr.Value, defaultVal);
        }

        public static object AttributeOrDefault(this XmlNode node, string attribute, object defaultVal)
        {
            var attr = node.Attributes?[attribute];
            if (attr == null) return defaultVal;

            string value = attr.Value;
            return defaultVal switch
            {
                bool boolDefault => ConvertOrDefault(value, boolDefault),
                int intDefault => ConvertOrDefault(value, intDefault),
                uint uintDefault => ConvertOrDefault(value, uintDefault),
                string => value,
                _ => defaultVal
            };
        }

        /// <summary>
        /// Typed attribute-value conversion. ADMX/ADML attributes only ever carry booleans,
        /// integers, and strings (see the parser call sites), so explicit parsing replaces the
        /// reflection-based <c>TypeDescriptor.GetConverter</c> path (trim/AOT-unsafe,
        /// IL2026/IL2072/IL2087). Unparsable values fall back to the default, matching the old
        /// converter's IsValid gate (e.g. BooleanConverter also rejected "1"/"0").
        /// </summary>
        private static T ConvertOrDefault<T>(string value, T defaultVal)
        {
            if (typeof(T) == typeof(bool))
                return bool.TryParse(value, out bool boolValue) ? (T)(object)boolValue : defaultVal;
            if (typeof(T) == typeof(int))
                return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue) ? (T)(object)intValue : defaultVal;
            if (typeof(T) == typeof(uint))
                return uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint uintValue) ? (T)(object)uintValue : defaultVal;
            if (typeof(T) == typeof(string))
                return (T)(object)value;

            return defaultVal;
        }
    }
}
