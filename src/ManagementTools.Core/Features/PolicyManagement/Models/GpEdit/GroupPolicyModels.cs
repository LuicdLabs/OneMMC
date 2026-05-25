using System.Collections.Generic;
using System.Collections.ObjectModel;
using ManagementTools.Core.Features.PolicyManagement.Services.GpEdit.Parsers;

namespace ManagementTools.Core.Features.PolicyManagement.Models.GpEdit
{
    #region Admx Structures (Raw Data)

    public class AdmxProduct
    {
        public string ID { get; set; } = string.Empty;
        public string DisplayCode { get; set; } = string.Empty;
        public AdmxProductType Type { get; set; }
        public int Version { get; set; }
        public AdmxProduct? Parent { get; set; }
        public AdmxFile DefinedIn { get; set; } = null!;
    }

    public enum AdmxProductType
    {
        Product,
        MajorRevision,
        MinorRevision
    }

    public class AdmxSupportDefinition
    {
        public string ID { get; set; } = string.Empty;
        public string DisplayCode { get; set; } = string.Empty;
        public AdmxSupportLogicType Logic { get; set; }
        public List<AdmxSupportEntry> Entries { get; set; } = new();
        public AdmxFile DefinedIn { get; set; } = null!;
    }

    public enum AdmxSupportLogicType
    {
        Blank,
        AllOf,
        AnyOf
    }

    public class AdmxSupportEntry
    {
        public string ProductID { get; set; } = string.Empty;
        public bool IsRange { get; set; }
        public int? MinVersion { get; set; }
        public int? MaxVersion { get; set; }
    }

    public class AdmxCategory
    {
        public string ID { get; set; } = string.Empty;
        public string DisplayCode { get; set; } = string.Empty;
        public string ExplainCode { get; set; } = string.Empty;
        public string ParentID { get; set; } = string.Empty;
        public AdmxFile DefinedIn { get; set; } = null!;
    }

    public class AdmxPolicy
    {
        public string ID { get; set; } = string.Empty;
        public AdmxPolicySection Section { get; set; }
        public string CategoryID { get; set; } = string.Empty;
        public string DisplayCode { get; set; } = string.Empty;
        public string ExplainCode { get; set; } = string.Empty;
        public string SupportedCode { get; set; } = string.Empty;
        public string PresentationID { get; set; } = string.Empty;
        public string ClientExtension { get; set; } = string.Empty;
        public string RegistryKey { get; set; } = string.Empty;
        public string RegistryValue { get; set; } = string.Empty;
        public PolicyRegistryList AffectedValues { get; set; } = new();
        public List<PolicyElement> Elements { get; set; } = new();
        public AdmxFile DefinedIn { get; set; } = null!;
    }

    public enum AdmxPolicySection
    {
        Machine = 1,
        User = 2,
        Both = 3
    }

    #endregion

    #region Policy Structures (Behavior)

    public class PolicyRegistryList
    {
        public PolicyRegistryValue? OnValue { get; set; }
        public PolicyRegistrySingleList? OnValueList { get; set; }
        public PolicyRegistryValue? OffValue { get; set; }
        public PolicyRegistrySingleList? OffValueList { get; set; }
    }

    public class PolicyRegistrySingleList
    {
        public string DefaultRegistryKey { get; set; } = string.Empty;
        public List<PolicyRegistryListEntry> AffectedValues { get; set; } = new();
    }

    public class PolicyRegistryValue // <value>
    {
        public PolicyRegistryValueType RegistryType { get; set; }
        public string StringValue { get; set; } = string.Empty;
        public uint NumberValue { get; set; }
    }

    public class PolicyRegistryListEntry // <item>
    {
        public string RegistryValue { get; set; } = string.Empty;
        public string RegistryKey { get; set; } = string.Empty;
        public PolicyRegistryValue? Value { get; set; }
    }

    public enum PolicyRegistryValueType
    {
        Delete,
        Numeric,
        Text
    }

    public abstract class PolicyElement
    {
        public string ID { get; set; } = string.Empty;
        public string ClientExtension { get; set; } = string.Empty;
        public string RegistryKey { get; set; } = string.Empty;
        public string RegistryValue { get; set; } = string.Empty;
        public string ElementType { get; set; } = string.Empty;
    }

    public class DecimalPolicyElement : PolicyElement // <decimal>
    {
        public bool Required { get; set; }
        public uint Minimum { get; set; }
        public uint Maximum { get; set; } = uint.MaxValue;
        public bool StoreAsText { get; set; }
        public bool NoOverwrite { get; set; }
    }

    public class BooleanPolicyElement : PolicyElement // <boolean>
    {
        public PolicyRegistryList AffectedRegistry { get; set; } = new();
    }

    public class TextPolicyElement : PolicyElement // <text>
    {
        public bool Required { get; set; }
        public int MaxLength { get; set; }
        public bool RegExpandSz { get; set; }
        public bool NoOverwrite { get; set; }
    }

    public class ListPolicyElement : PolicyElement // <list>
    {
        public bool HasPrefix { get; set; }
        public bool NoPurgeOthers { get; set; }
        public bool RegExpandSz { get; set; }
        public bool UserProvidesNames { get; set; }
    }

    public class EnumPolicyElement : PolicyElement // <enum>
    {
        public bool Required { get; set; }
        public List<EnumPolicyElementItem> Items { get; set; } = new();
    }

    public class EnumPolicyElementItem // <item>
    {
        public string DisplayCode { get; set; } = string.Empty;
        public PolicyRegistryValue? Value { get; set; }
        public PolicyRegistrySingleList? ValueList { get; set; } // <valueList>
    }

    public class MultiTextPolicyElement : PolicyElement // <multiText>
    {
        // This is undocumented, so it's unknown whether there can be other options for it
    }

    #endregion

    #region Compiled Structures (Object-Oriented)

    public class PolicyManagerCategory
    {
        public string UniqueID { get; set; } = string.Empty;
        public PolicyManagerCategory? Parent { get; set; }
        public List<PolicyManagerCategory> Children { get; set; } = new List<PolicyManagerCategory>();
        public string DisplayName { get; set; } = string.Empty;
        public string DisplayExplanation { get; set; } = string.Empty;
        public List<PolicyManagerPolicy> Policies { get; set; } = new List<PolicyManagerPolicy>();
        public AdmxCategory RawCategory { get; set; } = null!;
        
        // Helper to simplify binding
        public ObservableCollection<object> ChildrenAndPolicies
        {
            get
            {
                 var list = new ObservableCollection<object>();
                 foreach(var c in Children) list.Add(c);
                 foreach(var p in Policies) list.Add(p);
                 return list;
            }
        }
    }

    public class PolicyManagerProduct
    {
        public string UniqueID { get; set; } = string.Empty;
        public PolicyManagerProduct? Parent { get; set; }
        public List<PolicyManagerProduct> Children { get; set; } = new List<PolicyManagerProduct>();
        public string DisplayName { get; set; } = string.Empty;
        public AdmxProduct RawProduct { get; set; } = null!;
    }

    public class PolicyManagerSupport
    {
        public string UniqueID { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public List<PolicyManagerSupportEntry> Elements { get; set; } = new List<PolicyManagerSupportEntry>();
        public AdmxSupportDefinition RawSupport { get; set; } = null!;
    }

    public class PolicyManagerSupportEntry
    {
        public PolicyManagerProduct? Product { get; set; }
        public PolicyManagerSupport? SupportDefinition { get; set; } // Only used if this entry actually points to another support definition
        public AdmxSupportEntry RawSupportEntry { get; set; } = null!;
    }

    public class PolicyManagerPolicy
    {
        public string UniqueID { get; set; } = string.Empty;
        public PolicyManagerCategory Category { get; set; } = null!;
        public string DisplayName { get; set; } = string.Empty;
        public string DisplayExplanation { get; set; } = string.Empty;
        public PolicyManagerSupport? SupportedOn { get; set; }
        public Presentation? Presentation { get; set; }
        public AdmxPolicy RawPolicy { get; set; } = null!;
    }

    #endregion

    #region Presentation Structures (UI)

    public class Presentation
    {
        public string Name { get; set; } = string.Empty;
        public List<PresentationElement> Elements { get; set; } = new List<PresentationElement>();
    }

    public abstract class PresentationElement
    {
        public string ID { get; set; } = string.Empty; // refId
        public string ElementType { get; set; } = string.Empty;
    }

    public class LabelPresentationElement : PresentationElement // <text>
    {
        public string Text { get; set; } = string.Empty; // Inner text
    }

    public class NumericBoxPresentationElement : PresentationElement // <decimalTextBox>
    {
        public uint DefaultValue { get; set; } // defaultValue
        public bool HasSpinner { get; set; } = true; // spin
        public uint SpinnerIncrement { get; set; } // spinStep
        public string Label { get; set; } = string.Empty; // Inner text
    }

    public class TextBoxPresentationElement : PresentationElement // <textBox>
    {
        public string Label { get; set; } = string.Empty; // <label>
        public string DefaultValue { get; set; } = string.Empty; // <defaultValue>
    }

    public class CheckBoxPresentationElement : PresentationElement // <checkBox>
    {
        public bool DefaultState { get; set; } // defaultChecked
        public string Text { get; set; } = string.Empty; // Inner text
    }

    public class ComboBoxPresentationElement : PresentationElement // <comboBox>
    {
        public bool NoSort { get; set; } // noSort
        public string Label { get; set; } = string.Empty; // <label>
        public string DefaultText { get; set; } = string.Empty; // <default>
        public List<string> Suggestions { get; set; } = new List<string>(); // <suggestion>s
    }

    public class DropDownPresentationElement : PresentationElement // <dropdownList>
    {
        public bool NoSort { get; set; } // noSort
        public int? DefaultItemID { get; set; } // defaultItem
        public string Label { get; set; } = string.Empty; // Inner text
    }

    public class ListPresentationElement : PresentationElement // <listBox>
    {
        public string Label { get; set; } = string.Empty; // Inner text
    }

    public class MultiTextPresentationElement : PresentationElement // <multiTextBox>
    {
        public string Label { get; set; } = string.Empty; // Inner text
        // Undocumented, but never appears to have any other parameters
    }

    #endregion
}


