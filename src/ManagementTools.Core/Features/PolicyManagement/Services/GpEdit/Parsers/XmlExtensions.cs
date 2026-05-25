using System;
using System.ComponentModel;
using System.Xml;

namespace ManagementTools.Core.Features.PolicyManagement.Services.GpEdit.Parsers
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

            string value = attr.Value;
            var converter = TypeDescriptor.GetConverter(typeof(T));
            
            if (converter.IsValid(value))
            {
                var result = converter.ConvertFromString(value);
                if (result is T typedResult)
                    return typedResult;
            }
            
            return defaultVal;
        }

        public static object AttributeOrDefault(this XmlNode node, string attribute, object defaultVal)
        {
            var attr = node.Attributes?[attribute];
            if (attr == null) return defaultVal;

            string value = attr.Value;
            var converter = TypeDescriptor.GetConverter(defaultVal.GetType());
            
            if (converter.IsValid(value))
            {
                var result = converter.ConvertFromString(value);
                if (result != null)
                    return result;
            }
            
            return defaultVal;
        }
    }
}