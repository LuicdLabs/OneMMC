using System;
using System.Collections.Generic;
using System.Linq;
using OneMMC.Core.Features.PolicyManagement.Models.GpEdit;
using OneMMC.Core.Infrastructure.PolicyStorage;
using Microsoft.Win32;

namespace OneMMC.Core.Features.PolicyManagement.Services.GpEdit
{
    /// <summary>
    /// Represents the state of a Group Policy setting.
    /// </summary>
    public enum PolicyState
    {
        /// <summary>The policy is not configured.</summary>
        NotConfigured = 0,
        /// <summary>The policy is explicitly disabled.</summary>
        Disabled = 1,
        /// <summary>The policy is enabled.</summary>
        Enabled = 2,
        /// <summary>The policy state is ambiguous or conflicting.</summary>
        Unknown = 3
    }

    /// <summary>
    /// Represents a registry key and value pair for policy tracking.
    /// </summary>
    public sealed class RegistryKeyValuePair : IEquatable<RegistryKeyValuePair>
    {
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;

        public bool Equals(RegistryKeyValuePair? other)
        {
            if (other is null) return false;
            return string.Equals(Key, other.Key, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object? obj) => obj is RegistryKeyValuePair other && Equals(other);

        public override int GetHashCode()
        {
            return HashCode.Combine(
                Key?.ToLowerInvariant() ?? string.Empty,
                Value?.ToLowerInvariant() ?? string.Empty);
        }
    }

    /// <summary>
    /// Provides methods for reading and writing Group Policy settings to policy sources.
    /// </summary>
    public static class PolicyProcessing
    {
        #region Read Policy State

        /// <summary>
        /// Determines the current state of a policy.
        /// </summary>
        /// <param name="policySource">The policy source to read from.</param>
        /// <param name="policy">The policy to check.</param>
        /// <returns>The current state of the policy.</returns>
        public static PolicyState GetPolicyState(IPolicySource policySource, PolicyManagerPolicy policy)
        {
            if (policySource is null) throw new ArgumentNullException(nameof(policySource));
            if (policy is null) throw new ArgumentNullException(nameof(policy));

            var rawPolicy = policy.RawPolicy;
            decimal enabledEvidence = 0;
            decimal disabledEvidence = 0;

            // Check the policy's standard Registry values
            if (!string.IsNullOrEmpty(rawPolicy.RegistryValue))
            {
                var onValue = rawPolicy.AffectedValues.OnValue ?? CreateDefaultOnValue();
                var offValue = rawPolicy.AffectedValues.OffValue;

                // Check for enabled state
                CheckValue(onValue, policySource, rawPolicy.RegistryKey, rawPolicy.RegistryValue, ref enabledEvidence);
                
                // Check for disabled state
                if (offValue is not null)
                {
                    // Policy has explicit off value - check if it's present
                    CheckValue(offValue, policySource, rawPolicy.RegistryKey, rawPolicy.RegistryValue, ref disabledEvidence);
                }
                else
                {
                    // Default behavior: disabled = value is 0 or not present
                    // If enabled evidence is 0 and the key exists but value is 0, it's disabled
                    if (enabledEvidence == 0 && policySource.ContainsValue(rawPolicy.RegistryKey, rawPolicy.RegistryValue))
                    {
                        var val = policySource.GetValue(rawPolicy.RegistryKey, rawPolicy.RegistryValue);
                        if (val is uint uval && uval == 0)
                        {
                            disabledEvidence += 1;
                        }
                        else if (val is int ival && ival == 0)
                        {
                            disabledEvidence += 1;
                        }
                    }
                }
            }

            CheckValueList(rawPolicy.AffectedValues.OnValueList, policySource, rawPolicy.RegistryKey, ref enabledEvidence);
            CheckValueList(rawPolicy.AffectedValues.OffValueList, policySource, rawPolicy.RegistryKey, ref disabledEvidence);

            // Check the policy's elements
            if (rawPolicy.Elements is { Count: > 0 })
            {
                CheckElements(rawPolicy, policySource, ref enabledEvidence, ref disabledEvidence);
            }

            // Judge the evidence collected
            return DetermineState(enabledEvidence, disabledEvidence);
        }

        private static void CheckValue(PolicyRegistryValue? value, IPolicySource source, string key, string? valueName, ref decimal evidenceVar)
        {
            if (value is null || valueName is null) return;
            if (IsValuePresent(value, source, key, valueName))
            {
                evidenceVar += 1;
            }
        }

        private static void CheckValueList(PolicyRegistrySingleList? valueList, IPolicySource source, string defaultKey, ref decimal evidenceVar)
        {
            if (valueList is null) return;

            var listKey = string.IsNullOrEmpty(valueList.DefaultRegistryKey) ? defaultKey : valueList.DefaultRegistryKey;
            foreach (var entry in valueList.AffectedValues)
            {
                var entryKey = string.IsNullOrEmpty(entry.RegistryKey) ? listKey : entry.RegistryKey;
                CheckValue(entry.Value, source, entryKey, entry.RegistryValue, ref evidenceVar);
            }
        }

        private static void CheckElements(AdmxPolicy rawPolicy, IPolicySource source, ref decimal enabledEvidence, ref decimal disabledEvidence)
        {
            decimal deletedElements = 0;
            decimal presentElements = 0;

            foreach (var elem in rawPolicy.Elements)
            {
                var elemKey = GetEffectiveKey(elem.RegistryKey, rawPolicy.RegistryKey);

                switch (elem.ElementType)
                {
                    case "list":
                        CheckListElement(elemKey, source, ref deletedElements, ref presentElements);
                        break;
                    case "boolean":
                        CheckBooleanElement((BooleanPolicyElement)elem, elemKey, source, ref deletedElements, ref presentElements);
                        break;
                    default:
                        CheckGenericElement(elemKey, elem.RegistryValue, source, ref deletedElements, ref presentElements);
                        break;
                }
            }

            if (presentElements > 0)
                enabledEvidence += presentElements;
            else if (deletedElements > 0)
                disabledEvidence += deletedElements;
        }

        private static void CheckListElement(string elemKey, IPolicySource source, ref decimal deletedElements, ref decimal presentElements)
        {
            int neededValues = 0;
            if (source.WillDeleteValue(elemKey, ""))
            {
                deletedElements += 1;
                neededValues = 1;
            }
            if (source.GetValueNames(elemKey).Count > 0)
            {
                deletedElements -= neededValues;
                presentElements += 1;
            }
        }

        private static void CheckBooleanElement(BooleanPolicyElement elem, string elemKey, IPolicySource source, ref decimal deletedElements, ref decimal presentElements)
        {
            if (source.WillDeleteValue(elemKey, elem.RegistryValue))
            {
                deletedElements += 1;
            }
            else
            {
                decimal checkboxDisabled = 0;
                CheckValue(elem.AffectedRegistry.OffValue, source, elemKey, elem.RegistryValue, ref checkboxDisabled);
                CheckValueList(elem.AffectedRegistry.OffValueList, source, elemKey, ref checkboxDisabled);
                deletedElements += checkboxDisabled * 0.1m; // Weak evidence

                CheckValue(elem.AffectedRegistry.OnValue, source, elemKey, elem.RegistryValue, ref presentElements);
                CheckValueList(elem.AffectedRegistry.OnValueList, source, elemKey, ref presentElements);
            }
        }

        private static void CheckGenericElement(string elemKey, string? registryValue, IPolicySource source, ref decimal deletedElements, ref decimal presentElements)
        {
            if (source.WillDeleteValue(elemKey, registryValue ?? string.Empty))
            {
                deletedElements += 1;
            }
            else if (source.ContainsValue(elemKey, registryValue ?? string.Empty))
            {
                presentElements += 1;
            }
        }

        private static PolicyState DetermineState(decimal enabledEvidence, decimal disabledEvidence)
        {
            if (enabledEvidence > disabledEvidence)
                return PolicyState.Enabled;
            if (disabledEvidence > enabledEvidence)
                return PolicyState.Disabled;
            if (enabledEvidence == 0)
                return PolicyState.NotConfigured;
            return PolicyState.Unknown;
        }

        private static bool IsValuePresent(PolicyRegistryValue? value, IPolicySource source, string key, string? valueName)
        {
            if (value is null || valueName is null) return false;

            return value.RegistryType switch
            {
                PolicyRegistryValueType.Delete => source.WillDeleteValue(key, valueName),
                PolicyRegistryValueType.Numeric => IsNumericValueMatch(source, key, valueName, value.NumberValue),
                PolicyRegistryValueType.Text => IsTextValueMatch(source, key, valueName, value.StringValue),
                _ => throw new InvalidOperationException($"Illegal value type: {value.RegistryType}")
            };
        }

        private static bool IsNumericValueMatch(IPolicySource source, string key, string valueName, uint expectedValue)
        {
            if (!source.ContainsValue(key, valueName)) return false;

            var sourceVal = source.GetValue(key, valueName);
            if (sourceVal is not (uint or int)) return false;

            return Convert.ToInt64(sourceVal) == expectedValue;
        }

        private static bool IsTextValueMatch(IPolicySource source, string key, string valueName, string expectedValue)
        {
            if (!source.ContainsValue(key, valueName)) return false;

            var sourceVal = source.GetValue(key, valueName);
            return sourceVal is string str && str == expectedValue;
        }

        private static bool IsValueListPresent(PolicyRegistrySingleList? valueList, IPolicySource source, string key, string? valueName)
        {
            if (valueList is null) return false;

            var sublistKey = string.IsNullOrEmpty(valueList.DefaultRegistryKey) ? key : valueList.DefaultRegistryKey;
            return valueList.AffectedValues.All(entry =>
            {
                var entryKey = string.IsNullOrEmpty(entry.RegistryKey) ? sublistKey : entry.RegistryKey;
                return IsValuePresent(entry.Value, source, entryKey, entry.RegistryValue);
            });
        }

        #endregion

        #region Read Policy Options

        /// <summary>
        /// Gets the current values of all options for a policy.
        /// </summary>
        /// <param name="policySource">The policy source to read from.</param>
        /// <param name="policy">The policy to get options for.</param>
        /// <returns>A dictionary mapping option ID to their current values.</returns>
        public static Dictionary<string, object> GetPolicyOptionStates(IPolicySource policySource, PolicyManagerPolicy policy)
        {
            if (policySource is null) throw new ArgumentNullException(nameof(policySource));
            if (policy is null) throw new ArgumentNullException(nameof(policy));

            var state = new Dictionary<string, object>();
            var elements = policy.RawPolicy.Elements;

            if (elements is null || elements.Count == 0)
                return state;

            foreach (var elem in elements)
            {
                var elemKey = GetEffectiveKey(elem.RegistryKey, policy.RawPolicy.RegistryKey);
                var value = GetElementValue(elem, elemKey, policySource);
                state[elem.ID] = value;
            }

            return state;
        }

        private static object GetElementValue(PolicyElement elem, string elemKey, IPolicySource source)
        {
            return elem.ElementType switch
            {
                "decimal" => GetDecimalValue(source, elemKey, elem.RegistryValue),
                "boolean" => GetBooleanValue((BooleanPolicyElement)elem, elemKey, source),
                "text" => source.GetValue(elemKey, elem.RegistryValue ?? string.Empty) ?? string.Empty,
                "list" => GetListValue((ListPolicyElement)elem, elemKey, source),
                "enum" => GetEnumValue((EnumPolicyElement)elem, elemKey, source),
                "multiText" => source.GetValue(elemKey, elem.RegistryValue ?? string.Empty) ?? string.Empty,
                _ => string.Empty
            };
        }

        private static uint GetDecimalValue(IPolicySource source, string elemKey, string? registryValue)
        {
            var value = source.GetValue(elemKey, registryValue ?? string.Empty);
            return value is not null ? Convert.ToUInt32(value) : 0u;
        }

        private static bool GetBooleanValue(BooleanPolicyElement elem, string elemKey, IPolicySource source)
        {
            var regList = elem.AffectedRegistry;

            if (regList.OnValue is not null)
            {
                if (IsValuePresent(regList.OnValue, source, elemKey, elem.RegistryValue)) return true;
            }
            else if (regList.OnValueList is not null)
            {
                if (IsValueListPresent(regList.OnValueList, source, elemKey, elem.RegistryValue)) return true;
            }
            else
            {
                var val = source.GetValue(elemKey, elem.RegistryValue ?? string.Empty);
                if (val is not null && Convert.ToUInt32(val) == 1u) return true;
            }

            if (regList.OffValue is not null)
            {
                if (IsValuePresent(regList.OffValue, source, elemKey, elem.RegistryValue)) return false;
            }
            else if (regList.OffValueList is not null)
            {
                if (IsValueListPresent(regList.OffValueList, source, elemKey, elem.RegistryValue)) return false;
            }

            return false;
        }

        private static object GetListValue(ListPolicyElement elem, string elemKey, IPolicySource source)
        {
            if (elem.UserProvidesNames)
            {
                var entries = new Dictionary<string, string>();
                foreach (var valueName in source.GetValueNames(elemKey))
                {
                    var entryVal = source.GetValue(elemKey, valueName);
                    entries[valueName] = entryVal as string ?? string.Empty;
                }
                return entries;
            }
            else
            {
                var entries = new List<string>();
                if (elem.HasPrefix)
                {
                    int n = 1;
                    while (source.ContainsValue(elemKey, (elem.RegistryValue ?? string.Empty) + n))
                    {
                        var entryVal = source.GetValue(elemKey, (elem.RegistryValue ?? string.Empty) + n);
                        entries.Add(entryVal as string ?? string.Empty);
                        n++;
                    }
                }
                else
                {
                    foreach (var valueName in source.GetValueNames(elemKey))
                    {
                        entries.Add(valueName);
                    }
                }
                return entries;
            }
        }

        private static int GetEnumValue(EnumPolicyElement elem, string elemKey, IPolicySource source)
        {
            for (int i = 0; i < elem.Items.Count; i++)
            {
                var item = elem.Items[i];
                if (IsValuePresent(item.Value, source, elemKey, elem.RegistryValue))
                {
                    if (item.ValueList is null || IsValueListPresent(item.ValueList, source, elemKey, elem.RegistryValue))
                    {
                        return i;
                    }
                }
            }
            return -1;
        }

        #endregion

        #region Write Policy State

        /// <summary>
        /// Sets the state and options for a policy.
        /// </summary>
        /// <param name="policySource">The policy source to write to.</param>
        /// <param name="policy">The policy to modify.</param>
        /// <param name="state">The new state to set.</param>
        /// <param name="options">The options to apply (used when state is Enabled).</param>
        public static void SetPolicyState(IPolicySource policySource, PolicyManagerPolicy policy, PolicyState state, Dictionary<string, object>? options)
        {
            if (policySource is null) throw new ArgumentNullException(nameof(policySource));
            if (policy is null) throw new ArgumentNullException(nameof(policy));

            var rawPolicy = policy.RawPolicy;

            switch (state)
            {
                case PolicyState.Enabled:
                    EnablePolicy(policySource, rawPolicy, options);
                    break;
                case PolicyState.Disabled:
                    DisablePolicy(policySource, rawPolicy);
                    break;
                case PolicyState.NotConfigured:
                    ForgetPolicy(policySource, policy);
                    break;
            }
        }

        private static void EnablePolicy(IPolicySource source, AdmxPolicy rawPolicy, Dictionary<string, object>? options)
        {
            // Set the main policy value
            if (!string.IsNullOrEmpty(rawPolicy.RegistryValue))
            {
                if (rawPolicy.AffectedValues.OnValue is null)
                {
                    source.SetValue(rawPolicy.RegistryKey, rawPolicy.RegistryValue, 1u, RegistryValueKind.DWord);
                }
            }

            ApplyRegistryList(source, rawPolicy.AffectedValues, rawPolicy.RegistryKey, rawPolicy.RegistryValue, isOn: true);

            // Set element values
            if (rawPolicy.Elements is { Count: > 0 } && options is not null)
            {
                foreach (var elem in rawPolicy.Elements)
                {
                    if (!options.TryGetValue(elem.ID, out var optionData))
                        continue;

                    var elemKey = GetEffectiveKey(elem.RegistryKey, rawPolicy.RegistryKey);
                    SetElementValue(source, elem, elemKey, optionData);
                }
            }
        }

        private static void SetElementValue(IPolicySource source, PolicyElement elem, string elemKey, object optionData)
        {
            switch (elem.ElementType)
            {
                case "decimal":
                    SetDecimalElement(source, (DecimalPolicyElement)elem, elemKey, optionData);
                    break;
                case "boolean":
                    SetBooleanElement(source, (BooleanPolicyElement)elem, elemKey, optionData);
                    break;
                case "text":
                    SetTextElement(source, (TextPolicyElement)elem, elemKey, optionData);
                    break;
                case "list":
                    SetListElement(source, (ListPolicyElement)elem, elemKey, optionData);
                    break;
                case "enum":
                    SetEnumElement(source, (EnumPolicyElement)elem, elemKey, optionData);
                    break;
                case "multiText":
                    source.SetValue(elemKey, elem.RegistryValue ?? string.Empty, optionData ?? string.Empty, RegistryValueKind.MultiString);
                    break;
            }
        }

        private static void SetDecimalElement(IPolicySource source, DecimalPolicyElement elem, string elemKey, object optionData)
        {
            if (elem.StoreAsText)
            {
                source.SetValue(elemKey, elem.RegistryValue ?? string.Empty, Convert.ToString(optionData) ?? string.Empty, RegistryValueKind.String);
            }
            else
            {
                source.SetValue(elemKey, elem.RegistryValue ?? string.Empty, Convert.ToUInt32(optionData), RegistryValueKind.DWord);
            }
        }

        private static void SetBooleanElement(IPolicySource source, BooleanPolicyElement elem, string elemKey, object optionData)
        {
            bool checkState = (bool)optionData;

            if (checkState)
            {
                if (elem.AffectedRegistry.OnValue is null)
                {
                    source.SetValue(elemKey, elem.RegistryValue ?? string.Empty, 1u, RegistryValueKind.DWord);
                }
            }
            else
            {
                if (elem.AffectedRegistry.OffValue is null)
                {
                    source.DeleteValue(elemKey, elem.RegistryValue ?? string.Empty);
                }
            }

            ApplyRegistryList(source, elem.AffectedRegistry, elemKey, elem.RegistryValue, checkState);
        }

        private static void SetTextElement(IPolicySource source, TextPolicyElement elem, string elemKey, object optionData)
        {
            var regType = elem.RegExpandSz ? RegistryValueKind.ExpandString : RegistryValueKind.String;
            source.SetValue(elemKey, elem.RegistryValue ?? string.Empty, optionData ?? string.Empty, regType);
        }

        private static void SetListElement(IPolicySource source, ListPolicyElement elem, string elemKey, object? optionData)
        {
            if (!elem.NoPurgeOthers)
            {
                source.ClearKey(elemKey);
            }

            if (optionData is null) return;

            var regType = elem.RegExpandSz ? RegistryValueKind.ExpandString : RegistryValueKind.String;

            if (elem.UserProvidesNames && optionData is Dictionary<string, string> dict)
            {
                foreach (var kvp in dict)
                {
                    source.SetValue(elemKey, kvp.Key, kvp.Value, regType);
                }
            }
            else if (optionData is List<string> list)
            {
                int n = 1;
                foreach (var item in list)
                {
                    var valueName = elem.HasPrefix ? (elem.RegistryValue ?? string.Empty) + n : item;
                    source.SetValue(elemKey, valueName, item, regType);
                    n++;
                }
            }
        }

        private static void SetEnumElement(IPolicySource source, EnumPolicyElement elem, string elemKey, object optionData)
        {
            var index = (int)optionData;
            if (index < 0 || index >= elem.Items.Count) return;

            var selectedItem = elem.Items[index];
            ApplyValue(source, elemKey, elem.RegistryValue, selectedItem.Value);
            ApplySingleList(source, selectedItem.ValueList, elemKey);
        }

        private static void DisablePolicy(IPolicySource source, AdmxPolicy rawPolicy)
        {
            // Set the main policy off value
            if (!string.IsNullOrEmpty(rawPolicy.RegistryValue))
            {
                if (rawPolicy.AffectedValues.OffValue is not null)
                {
                    // Use the explicit off value defined in ADMX
                    ApplyValue(source, rawPolicy.RegistryKey, rawPolicy.RegistryValue, rawPolicy.AffectedValues.OffValue);
                }
                else
                {
                    // Default behavior for Disabled: set value to 0
                    // This distinguishes Disabled (value = 0) from Not Configured (value doesn't exist)
                    source.SetValue(rawPolicy.RegistryKey, rawPolicy.RegistryValue, 0u, RegistryValueKind.DWord);
                }
            }

            ApplyRegistryList(source, rawPolicy.AffectedValues, rawPolicy.RegistryKey, rawPolicy.RegistryValue, isOn: false);

            // Clear element values
            if (rawPolicy.Elements is { Count: > 0 })
            {
                foreach (var elem in rawPolicy.Elements)
                {
                    var elemKey = GetEffectiveKey(elem.RegistryKey, rawPolicy.RegistryKey);
                    ClearElementValue(source, elem, elemKey);
                }
            }
        }

        private static void ClearElementValue(IPolicySource source, PolicyElement elem, string elemKey)
        {
            switch (elem.ElementType)
            {
                case "list":
                    source.ClearKey(elemKey);
                    break;
                case "boolean":
                    var booleanElem = (BooleanPolicyElement)elem;
                    if (booleanElem.AffectedRegistry.OffValue is not null || booleanElem.AffectedRegistry.OffValueList is not null)
                    {
                        ApplyRegistryList(source, booleanElem.AffectedRegistry, elemKey, elem.RegistryValue, isOn: false);
                    }
                    else
                    {
                        source.DeleteValue(elemKey, elem.RegistryValue ?? string.Empty);
                    }
                    break;
                default:
                    source.DeleteValue(elemKey, elem.RegistryValue ?? string.Empty);
                    break;
            }
        }

        #endregion

        #region Forget Policy

        /// <summary>
        /// Removes all registry entries associated with a policy (sets it to Not Configured).
        /// </summary>
        /// <param name="policySource">The policy source to modify.</param>
        /// <param name="policy">The policy to forget.</param>
        public static void ForgetPolicy(IPolicySource policySource, PolicyManagerPolicy policy)
        {
            if (policySource is null) throw new ArgumentNullException(nameof(policySource));
            if (policy is null) throw new ArgumentNullException(nameof(policy));

            var entries = CollectPolicyEntries(policy);
            var rawPolicy = policy.RawPolicy;

            foreach (var entry in entries)
            {
                policySource.ForgetValue(entry.Key, entry.Value);
            }

            // Handle list elements specially
            if (rawPolicy.Elements is { Count: > 0 })
            {
                foreach (var elem in rawPolicy.Elements.Where(e => e.ElementType == "list"))
                {
                    var elemKey = GetEffectiveKey(elem.RegistryKey, rawPolicy.RegistryKey);
                    policySource.ClearKey(elemKey);
                    policySource.ForgetKeyClearance(elemKey);
                }
            }
        }

        private static List<RegistryKeyValuePair> CollectPolicyEntries(PolicyManagerPolicy policy)
        {
            var entries = new HashSet<RegistryKeyValuePair>();
            var rawPolicy = policy.RawPolicy;

            void AddEntry(string key, string? value)
            {
                if (!string.IsNullOrEmpty(value))
                {
                    entries.Add(new RegistryKeyValuePair { Key = key, Value = value });
                }
            }

            void AddSingleList(PolicyRegistrySingleList? list, string overrideKey)
            {
                if (list is null) return;

                var defaultKey = string.IsNullOrEmpty(overrideKey) ? rawPolicy.RegistryKey : overrideKey;
                var listKey = string.IsNullOrEmpty(list.DefaultRegistryKey) ? defaultKey : list.DefaultRegistryKey;

                foreach (var entry in list.AffectedValues)
                {
                    var entryKey = string.IsNullOrEmpty(entry.RegistryKey) ? listKey : entry.RegistryKey;
                    AddEntry(entryKey, entry.RegistryValue);
                }
            }

            // Main policy value
            if (!string.IsNullOrEmpty(rawPolicy.RegistryValue))
            {
                AddEntry(rawPolicy.RegistryKey, rawPolicy.RegistryValue);
            }

            AddSingleList(rawPolicy.AffectedValues.OnValueList, string.Empty);
            AddSingleList(rawPolicy.AffectedValues.OffValueList, string.Empty);

            // Element values
            if (rawPolicy.Elements is { Count: > 0 })
            {
                foreach (var elem in rawPolicy.Elements)
                {
                    var elemKey = GetEffectiveKey(elem.RegistryKey, rawPolicy.RegistryKey);

                    if (elem.ElementType != "list")
                    {
                        AddEntry(elemKey, elem.RegistryValue);
                    }

                    switch (elem.ElementType)
                    {
                        case "boolean":
                            var booleanElem = (BooleanPolicyElement)elem;
                            AddSingleList(booleanElem.AffectedRegistry.OnValueList, elemKey);
                            AddSingleList(booleanElem.AffectedRegistry.OffValueList, elemKey);
                            break;
                        case "enum":
                            var enumElem = (EnumPolicyElement)elem;
                            foreach (var item in enumElem.Items)
                            {
                                AddSingleList(item.ValueList, elemKey);
                            }
                            break;
                    }
                }
            }

            return entries.ToList();
        }

        #endregion

        #region Helper Methods

        private static void ApplyValue(IPolicySource source, string key, string? valueName, PolicyRegistryValue? value)
        {
            if (value is null || valueName is null) return;

            switch (value.RegistryType)
            {
                case PolicyRegistryValueType.Delete:
                    source.DeleteValue(key, valueName);
                    break;
                case PolicyRegistryValueType.Numeric:
                    source.SetValue(key, valueName, value.NumberValue, RegistryValueKind.DWord);
                    break;
                case PolicyRegistryValueType.Text:
                    source.SetValue(key, valueName, value.StringValue ?? string.Empty, RegistryValueKind.String);
                    break;
            }
        }

        private static void ApplySingleList(IPolicySource source, PolicyRegistrySingleList? list, string defaultKey)
        {
            if (list is null) return;

            var listKey = string.IsNullOrEmpty(list.DefaultRegistryKey) ? defaultKey : list.DefaultRegistryKey;
            foreach (var entry in list.AffectedValues)
            {
                var itemKey = string.IsNullOrEmpty(entry.RegistryKey) ? listKey : entry.RegistryKey;
                ApplyValue(source, itemKey, entry.RegistryValue, entry.Value);
            }
        }

        private static void ApplyRegistryList(IPolicySource source, PolicyRegistryList? list, string defaultKey, string? defaultValue, bool isOn)
        {
            if (list is null) return;

            if (isOn)
            {
                ApplyValue(source, defaultKey, defaultValue, list.OnValue);
                ApplySingleList(source, list.OnValueList, defaultKey);
            }
            else
            {
                ApplyValue(source, defaultKey, defaultValue, list.OffValue);
                ApplySingleList(source, list.OffValueList, defaultKey);
            }
        }

        private static string GetEffectiveKey(string? elementKey, string policyKey)
        {
            return string.IsNullOrEmpty(elementKey) ? policyKey : elementKey;
        }

        private static PolicyRegistryValue CreateDefaultOnValue()
        {
            return new PolicyRegistryValue { NumberValue = 1u, RegistryType = PolicyRegistryValueType.Numeric };
        }

        private static PolicyRegistryValue CreateDefaultOffValue()
        {
            return new PolicyRegistryValue { RegistryType = PolicyRegistryValueType.Delete };
        }

        #endregion
    }
}


