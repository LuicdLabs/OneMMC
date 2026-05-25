using System;
using System.Collections.Generic;
using System.Xml;
using System.Globalization;
using ManagementTools.Core.Features.PolicyManagement.Models.GpEdit;

namespace ManagementTools.Core.Features.PolicyManagement.Services.GpEdit.Parsers
{
    public class AdmxFile
    {
        public string SourceFile { get; set; } = string.Empty;
        public string AdmxNamespace { get; set; } = string.Empty;
        public string SupersededAdm { get; set; } = string.Empty;
        public decimal MinAdmlVersion { get; set; }
        public Dictionary<string, string> Prefixes { get; set; } = new Dictionary<string, string>();
        public List<AdmxProduct> Products { get; set; } = new List<AdmxProduct>();
        public List<AdmxSupportDefinition> SupportedOnDefinitions { get; set; } = new List<AdmxSupportDefinition>();
        public List<AdmxCategory> Categories { get; set; } = new List<AdmxCategory>();
        public List<AdmxPolicy> Policies { get; set; } = new List<AdmxPolicy>();

        private AdmxFile() { }

        public static AdmxFile Load(string file)
        {
            // ADMX documentation: https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-server-2008-R2-and-2008/cc772138(v=ws.10)?redirectedfrom=MSDN
            var admx = new AdmxFile();
            admx.SourceFile = file;
            var xmlDoc = new XmlDocument();
            xmlDoc.Load(file);
            var policyDefinitions = xmlDoc.GetElementsByTagName("policyDefinitions")[0];
            if (policyDefinitions == null) return admx;

            foreach (XmlNode child in policyDefinitions.ChildNodes)
            {
                switch (child.LocalName)
                {
                    case "policyNamespaces":
                        LoadPolicyNamespaces(child, admx);
                        break;
                    case "supersededAdm":
                        LoadSupersededAdm(child, admx);
                        break;
                    case "resources":
                        LoadResourceRequirements(child, admx);
                        break;
                    case "supportedOn":
                        LoadSupportedOn(child, admx);
                        break;
                    case "categories":
                        LoadCategories(child, admx);
                        break;
                    case "policies":
                        LoadPolicies(child, admx);
                        break;
                }
            }
            return admx;
        }

        private static void LoadPolicyNamespaces(XmlNode node, AdmxFile admx)
        {
            // Windows uses the target namespace to qualify policy/category IDs across separate ADMX files.
            foreach (XmlNode policyNamespace in node.ChildNodes)
            {
                var prefixAttr = policyNamespace.Attributes?["prefix"];
                var namespaceAttr = policyNamespace.Attributes?["namespace"];
                if (prefixAttr is null || namespaceAttr is null) continue;

                var fqNamespace = namespaceAttr.Value;
                if (policyNamespace.LocalName == "target") admx.AdmxNamespace = fqNamespace;
                admx.Prefixes.Add(prefixAttr.Value, fqNamespace);
            }
        }

        private static void LoadSupersededAdm(XmlNode node, AdmxFile admx)
        {
            var fileNameAttr = node.Attributes?["fileName"];
            if (fileNameAttr is not null)
                admx.SupersededAdm = fileNameAttr.Value;
        }

        private static void LoadResourceRequirements(XmlNode node, AdmxFile admx)
        {
            var minRevAttr = node.Attributes?["minRequiredRevision"];
            if (minRevAttr is not null)
                admx.MinAdmlVersion = decimal.Parse(minRevAttr.Value, CultureInfo.InvariantCulture);
        }

        private static void LoadSupportedOn(XmlNode node, AdmxFile admx)
        {
            foreach (XmlNode supportInfo in node.ChildNodes)
            {
                if (supportInfo.LocalName == "definitions")
                {
                    LoadSupportDefinitions(supportInfo, admx);
                }
                else if (supportInfo.LocalName == "products")
                {
                    LoadProducts(supportInfo, "product", null, admx);
                }
            }
        }

        private static void LoadSupportDefinitions(XmlNode node, AdmxFile admx)
        {
            foreach (XmlNode supportDef in node.ChildNodes)
            {
                var definition = LoadSupportDefinition(supportDef, admx);
                if (definition is not null)
                    admx.SupportedOnDefinitions.Add(definition);
            }
        }

        private static AdmxSupportDefinition? LoadSupportDefinition(XmlNode node, AdmxFile admx)
        {
            if (node.LocalName != "definition") return null;

            var nameAttr = node.Attributes?["name"];
            var displayNameAttr = node.Attributes?["displayName"];
            if (nameAttr is null || displayNameAttr is null) return null;

            var definition = new AdmxSupportDefinition();
            definition.ID = nameAttr.Value;
            definition.DisplayCode = displayNameAttr.Value;
            definition.Logic = AdmxSupportLogicType.Blank;
            definition.DefinedIn = admx;
            LoadSupportLogic(node, definition);
            return definition;
        }

        private static void LoadSupportLogic(XmlNode node, AdmxSupportDefinition definition)
        {
            // ADMX support entries describe which Windows product/version combinations expose a policy.
            foreach (XmlNode logicElement in node.ChildNodes)
            {
                if (!TrySetSupportLogic(logicElement, definition)) continue;

                definition.Entries = LoadSupportEntries(logicElement);
                break;
            }
        }

        private static bool TrySetSupportLogic(XmlNode node, AdmxSupportDefinition definition)
        {
            if (node.LocalName == "or")
            {
                definition.Logic = AdmxSupportLogicType.AnyOf;
                return true;
            }

            if (node.LocalName == "and")
            {
                definition.Logic = AdmxSupportLogicType.AllOf;
                return true;
            }

            return false;
        }

        private static List<AdmxSupportEntry> LoadSupportEntries(XmlNode node)
        {
            var entries = new List<AdmxSupportEntry>();
            foreach (XmlNode conditionElement in node.ChildNodes)
            {
                var entry = LoadSupportEntry(conditionElement);
                if (entry is not null)
                    entries.Add(entry);
            }

            return entries;
        }

        private static AdmxSupportEntry? LoadSupportEntry(XmlNode node)
        {
            if (node.LocalName == "reference")
            {
                var refAttr = node.Attributes?["ref"];
                return refAttr is null ? null : new AdmxSupportEntry { ProductID = refAttr.Value, IsRange = false };
            }

            if (node.LocalName != "range") return null;

            var entry = new AdmxSupportEntry { IsRange = true };
            var rangeRefAttr = node.Attributes?["ref"];
            if (rangeRefAttr is not null)
                entry.ProductID = rangeRefAttr.Value;

            var maxVerAttr = node.Attributes?["maxVersionIndex"];
            if (maxVerAttr is not null) entry.MaxVersion = int.Parse(maxVerAttr.Value, CultureInfo.InvariantCulture);

            var minVerAttr = node.Attributes?["minVersionIndex"];
            if (minVerAttr is not null) entry.MinVersion = int.Parse(minVerAttr.Value, CultureInfo.InvariantCulture);

            return entry;
        }

        private static void LoadCategories(XmlNode node, AdmxFile admx)
        {
            foreach (XmlNode categoryElement in node.ChildNodes)
            {
                var category = LoadCategory(categoryElement, admx);
                if (category is not null)
                    admx.Categories.Add(category);
            }
        }

        private static AdmxCategory? LoadCategory(XmlNode node, AdmxFile admx)
        {
            if (node.LocalName != "category") return null;

            var catNameAttr = node.Attributes?["name"];
            var catDisplayNameAttr = node.Attributes?["displayName"];
            if (catNameAttr is null || catDisplayNameAttr is null) return null;

            var category = new AdmxCategory();
            category.ID = catNameAttr.Value;
            category.DisplayCode = catDisplayNameAttr.Value;
            category.ExplainCode = node.AttributeOrNull("explainText") ?? string.Empty;
            category.ParentID = node["parentCategory"]?.Attributes?["ref"]?.Value ?? string.Empty;
            category.DefinedIn = admx;
            return category;
        }

        private static void LoadPolicies(XmlNode node, AdmxFile admx)
        {
            foreach (XmlNode polElement in node.ChildNodes)
            {
                var policy = LoadPolicy(polElement, admx);
                if (policy is not null)
                    admx.Policies.Add(policy);
            }
        }

        private static AdmxPolicy? LoadPolicy(XmlNode node, AdmxFile admx)
        {
            if (node.LocalName != "policy") return null;

            var polNameAttr = node.Attributes?["name"];
            var polDisplayNameAttr = node.Attributes?["displayName"];
            var polKeyAttr = node.Attributes?["key"];
            if (polNameAttr is null || polDisplayNameAttr is null || polKeyAttr is null) return null;

            var policy = new AdmxPolicy();
            policy.ID = polNameAttr.Value;
            policy.DefinedIn = admx;
            policy.DisplayCode = polDisplayNameAttr.Value;
            policy.RegistryKey = polKeyAttr.Value;
            policy.Section = LoadPolicySection(node.Attributes?["class"]?.Value);
            policy.ExplainCode = node.AttributeOrNull("explainText") ?? string.Empty;
            policy.PresentationID = node.AttributeOrNull("presentation") ?? string.Empty;
            policy.ClientExtension = node.AttributeOrNull("clientExtension") ?? string.Empty;
            policy.RegistryValue = node.AttributeOrNull("valueName") ?? string.Empty;
            policy.AffectedValues = LoadOnOffValList("enabledValue", "disabledValue", "enabledList", "disabledList", node);
            LoadPolicyDetails(node, policy);
            return policy;
        }

        private static AdmxPolicySection LoadPolicySection(string? policyClass)
        {
            return policyClass switch
            {
                "Machine" => AdmxPolicySection.Machine,
                "User" => AdmxPolicySection.User,
                _ => AdmxPolicySection.Both,
            };
        }

        private static void LoadPolicyDetails(XmlNode node, AdmxPolicy policy)
        {
            foreach (XmlNode polInfo in node.ChildNodes)
            {
                switch (polInfo.LocalName)
                {
                    case "parentCategory":
                        policy.CategoryID = polInfo.Attributes?["ref"]?.Value ?? policy.CategoryID;
                        break;
                    case "supportedOn":
                        policy.SupportedCode = polInfo.Attributes?["ref"]?.Value ?? policy.SupportedCode;
                        break;
                    case "elements":
                        policy.Elements = LoadPolicyElements(polInfo);
                        break;
                }
            }
        }

        private static List<PolicyElement> LoadPolicyElements(XmlNode node)
        {
            var elements = new List<PolicyElement>();
            foreach (XmlNode uiElement in node.ChildNodes)
            {
                var entry = LoadPolicyElement(uiElement);
                if (entry is not null)
                    elements.Add(entry);
            }

            return elements;
        }

        private static PolicyElement? LoadPolicyElement(XmlNode node)
        {
            var entry = CreatePolicyElement(node);
            if (entry is null) return null;

            entry.ClientExtension = node.AttributeOrNull("clientExtension") ?? string.Empty;
            entry.RegistryKey = node.AttributeOrNull("key") ?? string.Empty;
            if (string.IsNullOrEmpty(entry.RegistryValue))
                entry.RegistryValue = node.AttributeOrNull("valueName") ?? string.Empty;

            entry.ID = node.Attributes?["id"]?.Value ?? string.Empty;
            entry.ElementType = node.LocalName;
            return entry;
        }

        private static PolicyElement? CreatePolicyElement(XmlNode node)
        {
            return node.LocalName switch
            {
                "decimal" => LoadDecimalPolicyElement(node),
                "boolean" => LoadBooleanPolicyElement(node),
                "text" => LoadTextPolicyElement(node),
                "list" => LoadListPolicyElement(node),
                "enum" => LoadEnumPolicyElement(node),
                "multiText" => new MultiTextPolicyElement(),
                _ => null,
            };
        }

        private static DecimalPolicyElement LoadDecimalPolicyElement(XmlNode node)
        {
            var entry = new DecimalPolicyElement();
            entry.Minimum = node.AttributeOrDefault("minValue", (uint)0);
            entry.Maximum = node.AttributeOrDefault("maxValue", uint.MaxValue);
            entry.NoOverwrite = node.AttributeOrDefault("soft", false);
            entry.StoreAsText = node.AttributeOrDefault("storeAsText", false);
            return entry;
        }

        private static BooleanPolicyElement LoadBooleanPolicyElement(XmlNode node)
        {
            var entry = new BooleanPolicyElement();
            entry.AffectedRegistry = LoadOnOffValList("trueValue", "falseValue", "trueList", "falseList", node);
            return entry;
        }

        private static TextPolicyElement LoadTextPolicyElement(XmlNode node)
        {
            var entry = new TextPolicyElement();
            entry.MaxLength = node.AttributeOrDefault("maxLength", 255);
            entry.Required = node.AttributeOrDefault("required", false);
            entry.RegExpandSz = node.AttributeOrDefault("expandable", false);
            entry.NoOverwrite = node.AttributeOrDefault("soft", false);
            return entry;
        }

        private static ListPolicyElement LoadListPolicyElement(XmlNode node)
        {
            var entry = new ListPolicyElement();
            entry.NoPurgeOthers = node.AttributeOrDefault("additive", false);
            entry.RegExpandSz = node.AttributeOrDefault("expandable", false);
            entry.UserProvidesNames = node.AttributeOrDefault("explicitValue", false);
            entry.HasPrefix = node.Attributes?["valuePrefix"] is not null;
            entry.RegistryValue = node.AttributeOrNull("valuePrefix") ?? string.Empty;
            return entry;
        }

        private static EnumPolicyElement LoadEnumPolicyElement(XmlNode node)
        {
            var entry = new EnumPolicyElement();
            entry.Required = node.AttributeOrDefault("required", false);
            entry.Items = LoadEnumItems(node);
            return entry;
        }

        private static List<EnumPolicyElementItem> LoadEnumItems(XmlNode node)
        {
            var items = new List<EnumPolicyElementItem>();
            foreach (XmlNode itemElement in node.ChildNodes)
            {
                var item = LoadEnumItem(itemElement);
                if (item is not null)
                    items.Add(item);
            }

            return items;
        }

        private static EnumPolicyElementItem? LoadEnumItem(XmlNode node)
        {
            if (node.LocalName != "item") return null;

            var itemDisplayNameAttr = node.Attributes?["displayName"];
            if (itemDisplayNameAttr is null) return null;

            var enumItem = new EnumPolicyElementItem();
            enumItem.DisplayCode = itemDisplayNameAttr.Value;
            foreach (XmlNode valElement in node.ChildNodes)
            {
                if (valElement.LocalName == "value")
                    enumItem.Value = LoadRegItem(valElement);
                else if (valElement.LocalName == "valueList")
                    enumItem.ValueList = LoadOneRegList(valElement);
            }

            return enumItem;
        }

        private static void LoadProducts(XmlNode node, string childTagName, AdmxProduct? parent, AdmxFile admx)
        {
            foreach (XmlNode subproductElement in node.ChildNodes)
            {
                if (subproductElement.LocalName != childTagName) continue;
                var nameAttr = subproductElement.Attributes?["name"];
                var displayNameAttr = subproductElement.Attributes?["displayName"];
                if (nameAttr == null || displayNameAttr == null) continue;
                
                var product = new AdmxProduct();
                product.ID = nameAttr.Value;
                product.DisplayCode = displayNameAttr.Value;
                
                if (parent != null)
                {
                    var versionAttr = subproductElement.Attributes?["versionIndex"];
                    if (versionAttr != null)
                        product.Version = int.Parse(versionAttr.Value);
                }
                
                product.Parent = parent;
                product.DefinedIn = admx;
                admx.Products.Add(product);

                LoadProductChildren(subproductElement, product, parent, admx);
            }
        }

        private static void LoadProductChildren(XmlNode node, AdmxProduct product, AdmxProduct? parent, AdmxFile admx)
        {
            if (parent is null)
            {
                product.Type = AdmxProductType.Product;
                LoadProducts(node, "majorVersion", product, admx);
                return;
            }

            if (parent.Parent is null)
            {
                product.Type = AdmxProductType.MajorRevision;
                LoadProducts(node, "minorVersion", product, admx);
                return;
            }

            product.Type = AdmxProductType.MinorRevision;
        }

        private static PolicyRegistryValue LoadRegItem(XmlNode node)
        {
             var regItem = new PolicyRegistryValue();
             foreach (XmlNode subElement in node.ChildNodes)
             {
                 if (subElement.LocalName == "delete")
                 {
                     regItem.RegistryType = PolicyRegistryValueType.Delete;
                     break;
                 }
                 else if (subElement.LocalName == "decimal")
                 {
                     regItem.RegistryType = PolicyRegistryValueType.Numeric;
                     var valueAttr = subElement.Attributes?["value"];
                     if (valueAttr != null)
                         regItem.NumberValue = uint.Parse(valueAttr.Value);
                     break;
                 }
                 else if (subElement.LocalName == "string")
                 {
                     regItem.RegistryType = PolicyRegistryValueType.Text;
                     regItem.StringValue = subElement.InnerText;
                     break;
                 }
             }
             return regItem;
        }

        private static PolicyRegistrySingleList LoadOneRegList(XmlNode node)
        {
            var singleList = new PolicyRegistrySingleList();
            singleList.DefaultRegistryKey = node.AttributeOrNull("defaultKey") ?? string.Empty;
            singleList.AffectedValues = new List<PolicyRegistryListEntry>();

            foreach (XmlNode itemElement in node.ChildNodes)
            {
                if (itemElement.LocalName != "item") continue;
                var valueNameAttr = itemElement.Attributes?["valueName"];
                if (valueNameAttr == null) continue;
                
                var listEntry = new PolicyRegistryListEntry();
                listEntry.RegistryValue = valueNameAttr.Value;
                listEntry.RegistryKey = itemElement.AttributeOrNull("key") ?? string.Empty;
                
                foreach (XmlNode valElement in itemElement.ChildNodes)
                {
                    if (valElement.LocalName == "value")
                    {
                        listEntry.Value = LoadRegItem(valElement);
                        break;
                    }
                }
                singleList.AffectedValues.Add(listEntry);
            }
            return singleList;
        }

        private static PolicyRegistryList LoadOnOffValList(string onValueName, string offValueName, string onListName, string offListName, XmlNode node)
        {
            var regList = new PolicyRegistryList();
            foreach (XmlNode subElement in node.ChildNodes)
            {
                if (subElement.Name == onValueName)
                    regList.OnValue = LoadRegItem(subElement);
                else if (subElement.Name == offValueName)
                    regList.OffValue = LoadRegItem(subElement);
                else if (subElement.Name == onListName)
                    regList.OnValueList = LoadOneRegList(subElement);
                else if (subElement.Name == offListName)
                    regList.OffValueList = LoadOneRegList(subElement);
            }
            return regList;
        }
    }
}


