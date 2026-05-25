using System;
using System.Collections.Generic;
using System.Xml;
using System.Globalization;
using ManagementTools.Core.Features.PolicyManagement.Models.GpEdit;

namespace ManagementTools.Core.Features.PolicyManagement.Services.GpEdit.Parsers
{
    public class AdmlFile
    {
        public string SourceFile { get; set; } = string.Empty;
        public decimal Revision { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public Dictionary<string, string> StringTable { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, Presentation> PresentationTable { get; set; } = new Dictionary<string, Presentation>();

        private AdmlFile() { }

        public static AdmlFile Load(string file)
        {
            // ADML documentation: https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-server-2008-R2-and-2008/cc772050(v=ws.10)?redirectedfrom=MSDN
            var adml = new AdmlFile();
            adml.SourceFile = file;
            var xmlDoc = new XmlDocument();
            xmlDoc.Load(file);

            LoadMetadata(xmlDoc, adml);
            LoadStringTable(xmlDoc, adml);
            LoadPresentationTable(xmlDoc, adml);

            return adml;
        }

        private static void LoadMetadata(XmlDocument xmlDoc, AdmlFile adml)
        {
            var policyDefinitionResources = xmlDoc.GetElementsByTagName("policyDefinitionResources")[0];
            if (policyDefinitionResources?.Attributes?["revision"]?.Value is string revValue)
                adml.Revision = decimal.Parse(revValue, CultureInfo.InvariantCulture);

            if (policyDefinitionResources is null) return;

            foreach (XmlNode child in policyDefinitionResources.ChildNodes)
            {
                switch (child.LocalName)
                {
                    case "displayName":
                        adml.DisplayName = child.InnerText;
                        break;
                    case "description":
                        adml.Description = child.InnerText;
                        break;
                }
            }
        }

        private static void LoadStringTable(XmlDocument xmlDoc, AdmlFile adml)
        {
            var stringTable = xmlDoc.GetElementsByTagName("stringTable")[0];
            if (stringTable is null) return;

            // ADML files hold the language-specific strings Windows pairs with neutral ADMX definitions.
            foreach (XmlNode stringElement in stringTable.ChildNodes)
            {
                if (stringElement.LocalName != "string") continue;

                var key = stringElement.Attributes?["id"]?.Value;
                if (key is null) continue;

                adml.StringTable.Add(key, stringElement.InnerText);
            }
        }

        private static void LoadPresentationTable(XmlDocument xmlDoc, AdmlFile adml)
        {
            var presTable = xmlDoc.GetElementsByTagName("presentationTable")[0];
            if (presTable is null) return;

            foreach (XmlNode presElement in presTable.ChildNodes)
            {
                var presentation = LoadPresentation(presElement);
                if (presentation is not null)
                    adml.PresentationTable.Add(presentation.Name, presentation);
            }
        }

        private static Presentation? LoadPresentation(XmlNode node)
        {
            if (node.LocalName != "presentation") return null;

            var presentation = new Presentation();
            presentation.Name = node.Attributes?["id"]?.Value ?? string.Empty;

            foreach (XmlNode uiElement in node.ChildNodes)
            {
                var presPart = LoadPresentationElement(uiElement);
                if (presPart is not null)
                    presentation.Elements.Add(presPart);
            }

            return presentation;
        }

        private static PresentationElement? LoadPresentationElement(XmlNode node)
        {
            // Presentation nodes mirror the controls gpedit.msc shows for editable policy elements.
            var presPart = CreatePresentationElement(node);
            if (presPart is null) return null;

            presPart.ID = node.Attributes?["refId"]?.Value ?? string.Empty;
            presPart.ElementType = node.LocalName;
            return presPart;
        }

        private static PresentationElement? CreatePresentationElement(XmlNode node)
        {
            return node.LocalName switch
            {
                "text" => new LabelPresentationElement { Text = node.InnerText },
                "decimalTextBox" => LoadNumericBoxPresentationElement(node),
                "textBox" => LoadTextBoxPresentationElement(node),
                "checkBox" => LoadCheckBoxPresentationElement(node),
                "comboBox" => LoadComboBoxPresentationElement(node),
                "dropdownList" => LoadDropDownPresentationElement(node),
                "listBox" => new ListPresentationElement { Label = node.InnerText },
                "multiTextBox" => new MultiTextPresentationElement { Label = node.InnerText },
                _ => null,
            };
        }

        private static NumericBoxPresentationElement LoadNumericBoxPresentationElement(XmlNode node)
        {
            var part = new NumericBoxPresentationElement();
            part.DefaultValue = node.AttributeOrDefault("defaultValue", (uint)1);
            part.HasSpinner = node.AttributeOrDefault("spin", true);
            part.SpinnerIncrement = node.AttributeOrDefault("spinStep", (uint)1);
            part.Label = node.InnerText;
            return part;
        }

        private static TextBoxPresentationElement LoadTextBoxPresentationElement(XmlNode node)
        {
            var part = new TextBoxPresentationElement();
            foreach (XmlNode textboxInfo in node.ChildNodes)
            {
                switch (textboxInfo.LocalName)
                {
                    case "label":
                        part.Label = textboxInfo.InnerText;
                        break;
                    case "defaultValue":
                        part.DefaultValue = textboxInfo.InnerText;
                        break;
                }
            }

            return part;
        }

        private static CheckBoxPresentationElement LoadCheckBoxPresentationElement(XmlNode node)
        {
            var part = new CheckBoxPresentationElement();
            part.DefaultState = node.AttributeOrDefault("defaultChecked", false);
            part.Text = node.InnerText;
            return part;
        }

        private static ComboBoxPresentationElement LoadComboBoxPresentationElement(XmlNode node)
        {
            var part = new ComboBoxPresentationElement();
            part.NoSort = node.AttributeOrDefault("noSort", false);
            foreach (XmlNode comboInfo in node.ChildNodes)
            {
                switch (comboInfo.LocalName)
                {
                    case "label":
                        part.Label = comboInfo.InnerText;
                        break;
                    case "default":
                        part.DefaultText = comboInfo.InnerText;
                        break;
                    case "suggestion":
                        part.Suggestions.Add(comboInfo.InnerText);
                        break;
                }
            }

            return part;
        }

        private static DropDownPresentationElement LoadDropDownPresentationElement(XmlNode node)
        {
            var part = new DropDownPresentationElement();
            part.NoSort = node.AttributeOrDefault("noSort", false);
            part.Label = node.InnerText;

            var defaultItem = node.AttributeOrNull("defaultItem");
            if (defaultItem is not null && int.TryParse(defaultItem, NumberStyles.Integer, CultureInfo.InvariantCulture, out var defItem))
                part.DefaultItemID = defItem;

            return part;
        }
    }
}


