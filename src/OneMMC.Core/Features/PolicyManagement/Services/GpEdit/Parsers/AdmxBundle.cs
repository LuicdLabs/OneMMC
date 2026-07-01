using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using OneMMC.Core.Features.PolicyManagement.Models.GpEdit;

namespace OneMMC.Core.Features.PolicyManagement.Services.GpEdit.Parsers
{
    public class AdmxBundle
    {
        private Dictionary<AdmxFile, AdmlFile> SourceFiles { get; set; } = new Dictionary<AdmxFile, AdmlFile>();
        private Dictionary<string, AdmxFile> Namespaces { get; set; } = new Dictionary<string, AdmxFile>();

        // Temporary lists from ADMX files that haven't been integrated yet
        private List<AdmxCategory> RawCategories { get; set; } = new List<AdmxCategory>();
        private List<AdmxProduct> RawProducts { get; set; } = new List<AdmxProduct>();
        private List<AdmxPolicy> RawPolicies { get; set; } = new List<AdmxPolicy>();
        private List<AdmxSupportDefinition> RawSupport { get; set; } = new List<AdmxSupportDefinition>();

        // Lists that include all items, even those that are children of others
        public Dictionary<string, PolicyManagerCategory> FlatCategories { get; set; } = new Dictionary<string, PolicyManagerCategory>();
        public Dictionary<string, PolicyManagerProduct> FlatProducts { get; set; } = new Dictionary<string, PolicyManagerProduct>();

        // Lists of top-level items only
        public Dictionary<string, PolicyManagerCategory> Categories { get; set; } = new Dictionary<string, PolicyManagerCategory>();
        public Dictionary<string, PolicyManagerProduct> Products { get; set; } = new Dictionary<string, PolicyManagerProduct>();
        public Dictionary<string, PolicyManagerPolicy> Policies { get; set; } = new Dictionary<string, PolicyManagerPolicy>();
        public Dictionary<string, PolicyManagerSupport> SupportDefinitions { get; set; } = new Dictionary<string, PolicyManagerSupport>();

        public Dictionary<AdmxFile, AdmlFile> Sources => SourceFiles;

        public IEnumerable<AdmxLoadFailure> LoadFolder(string path, string languageCode)
        {
            var fails = new List<AdmxLoadFailure>();
            foreach (var file in Directory.EnumerateFiles(path))
            {
                if (file.ToLowerInvariant().EndsWith(".admx"))
                {
                    var fail = AddSingleAdmx(file, languageCode);
                    if (fail != null) fails.Add(fail);
                }
            }
            BuildStructures();
            return fails;
        }

        public IEnumerable<AdmxLoadFailure> LoadFile(string path, string languageCode)
        {
            var fail = AddSingleAdmx(path, languageCode);
            BuildStructures();
            if (fail != null) return new[] { fail };
            return new AdmxLoadFailure[0];
        }

        private AdmxLoadFailure? AddSingleAdmx(string admxPath, string languageCode)
        {
            var admxFailure = TryLoadAdmx(admxPath, out var admx);
            if (admxFailure is not null || admx is null) return admxFailure;

            if (Namespaces.ContainsKey(admx.AdmxNamespace))
                return new AdmxLoadFailure(AdmxLoadFailType.DuplicateNamespace, admxPath, admx.AdmxNamespace);

            var admlPath = ResolveAdmlPath(admxPath, languageCode);
            if (admlPath is null)
                return new AdmxLoadFailure(AdmxLoadFailType.NoAdml, admxPath);

            var admlFailure = TryLoadAdml(admlPath, admxPath, out var adml);
            if (admlFailure is not null || adml is null) return admlFailure;

            StageAdmx(admx, adml);
            return null;
        }

        private static AdmxLoadFailure? TryLoadAdmx(string admxPath, out AdmxFile? admx)
        {
            try
            {
                admx = AdmxFile.Load(admxPath);
                return null;
            }
            catch (XmlException ex)
            {
                admx = null;
                return new AdmxLoadFailure(AdmxLoadFailType.BadAdmxParse, admxPath, ex.Message);
            }
            catch (Exception ex)
            {
                admx = null;
                return new AdmxLoadFailure(AdmxLoadFailType.BadAdmx, admxPath, ex.Message);
            }
        }

        private static string? ResolveAdmlPath(string admxPath, string languageCode)
        {
            var fileTitle = Path.GetFileName(admxPath);
            var baseDir = Path.GetDirectoryName(admxPath) ?? string.Empty;
            var admlPath = Path.Combine(baseDir, languageCode, Path.ChangeExtension(fileTitle, "adml"));
            if (File.Exists(admlPath)) return admlPath;

            var similarLanguagePath = FindSimilarLanguageAdmlPath(baseDir, fileTitle, languageCode);
            if (similarLanguagePath is not null) return similarLanguagePath;

            var fallbackPath = Path.Combine(baseDir, "en-US", Path.ChangeExtension(fileTitle, "adml"));
            return File.Exists(fallbackPath) ? fallbackPath : null;
        }

        private static string? FindSimilarLanguageAdmlPath(string baseDir, string fileTitle, string languageCode)
        {
            // Windows stores ADML resources below PolicyDefinitions language folders, such as en-US or zh-TW.
            var language = languageCode.Split('-')[0];
            foreach (var langSubdir in Directory.EnumerateDirectories(baseDir))
            {
                var langSubdirTitle = Path.GetFileName(langSubdir);
                if (langSubdirTitle.Split('-')[0] != language) continue;

                var candidate = Path.Combine(langSubdir, Path.ChangeExtension(fileTitle, "adml"));
                if (File.Exists(candidate)) return candidate;
            }

            return null;
        }

        private static AdmxLoadFailure? TryLoadAdml(string admlPath, string admxPath, out AdmlFile? adml)
        {
            try
            {
                adml = AdmlFile.Load(admlPath);
                return null;
            }
            catch (XmlException ex)
            {
                adml = null;
                return new AdmxLoadFailure(AdmxLoadFailType.BadAdmlParse, admxPath, ex.Message);
            }
            catch (Exception ex)
            {
                adml = null;
                return new AdmxLoadFailure(AdmxLoadFailType.BadAdml, admxPath, ex.Message);
            }
        }

        private void StageAdmx(AdmxFile admx, AdmlFile adml)
        {
            RawCategories.AddRange(admx.Categories);
            RawProducts.AddRange(admx.Products);
            RawPolicies.AddRange(admx.Policies);
            RawSupport.AddRange(admx.SupportedOnDefinitions);
            SourceFiles.Add(admx, adml);
            Namespaces.Add(admx.AdmxNamespace, admx);
        }

        private void BuildStructures()
        {
            var catIds = new Dictionary<string, PolicyManagerCategory>();
            var productIds = new Dictionary<string, PolicyManagerProduct>();
            var supIds = new Dictionary<string, PolicyManagerSupport>();
            var polIds = new Dictionary<string, PolicyManagerPolicy>();

            BuildUnresolvedItems(catIds, productIds, supIds, polIds);
            ResolveReferences(catIds, productIds, supIds, polIds);
            PublishItems(catIds, productIds, supIds, polIds);
            RawCategories.Clear();
            RawProducts.Clear();
            RawSupport.Clear();
            RawPolicies.Clear();
        }

        private void BuildUnresolvedItems(
            Dictionary<string, PolicyManagerCategory> catIds,
            Dictionary<string, PolicyManagerProduct> productIds,
            Dictionary<string, PolicyManagerSupport> supIds,
            Dictionary<string, PolicyManagerPolicy> polIds)
        {
            AddRawCategories(catIds);
            AddRawProducts(productIds);
            AddRawSupportDefinitions(supIds);
            AddRawPolicies(polIds);
        }

        private void AddRawCategories(Dictionary<string, PolicyManagerCategory> catIds)
        {
            foreach (var rawCat in RawCategories)
            {
                var cat = new PolicyManagerCategory();
                cat.DisplayName = ResolveString(rawCat.DisplayCode, rawCat.DefinedIn);
                cat.DisplayExplanation = ResolveString(rawCat.ExplainCode, rawCat.DefinedIn);
                cat.UniqueID = QualifyName(rawCat.ID, rawCat.DefinedIn);
                cat.RawCategory = rawCat;
                AddIfMissing(catIds, cat.UniqueID, cat);
            }
        }

        private void AddRawProducts(Dictionary<string, PolicyManagerProduct> productIds)
        {
            foreach (var rawProduct in RawProducts)
            {
                var product = new PolicyManagerProduct();
                product.DisplayName = ResolveString(rawProduct.DisplayCode, rawProduct.DefinedIn);
                product.UniqueID = QualifyName(rawProduct.ID, rawProduct.DefinedIn);
                product.RawProduct = rawProduct;
                AddIfMissing(productIds, product.UniqueID, product);
            }
        }

        private void AddRawSupportDefinitions(Dictionary<string, PolicyManagerSupport> supIds)
        {
            foreach (var rawSup in RawSupport)
            {
                var sup = new PolicyManagerSupport();
                sup.DisplayName = ResolveString(rawSup.DisplayCode, rawSup.DefinedIn);
                sup.UniqueID = QualifyName(rawSup.ID, rawSup.DefinedIn);
                sup.RawSupport = rawSup;
                AddSupportEntries(rawSup, sup);
                AddIfMissing(supIds, sup.UniqueID, sup);
            }
        }

        private static void AddSupportEntries(AdmxSupportDefinition rawSup, PolicyManagerSupport sup)
        {
            if (rawSup.Entries is null) return;

            foreach (var rawSupEntry in rawSup.Entries)
            {
                var supEntry = new PolicyManagerSupportEntry();
                supEntry.RawSupportEntry = rawSupEntry;
                sup.Elements.Add(supEntry);
            }
        }

        private void AddRawPolicies(Dictionary<string, PolicyManagerPolicy> polIds)
        {
            foreach (var rawPol in RawPolicies)
            {
                LocalizeEnumItems(rawPol);

                var pol = new PolicyManagerPolicy();
                pol.DisplayExplanation = ResolveString(rawPol.ExplainCode, rawPol.DefinedIn);
                pol.DisplayName = ResolveString(rawPol.DisplayCode, rawPol.DefinedIn);
                pol.Presentation = ResolvePolicyPresentation(rawPol);
                pol.UniqueID = QualifyName(rawPol.ID, rawPol.DefinedIn);
                pol.RawPolicy = rawPol;
                AddIfMissing(polIds, pol.UniqueID, pol);
            }
        }

        private Presentation? ResolvePolicyPresentation(AdmxPolicy rawPol)
        {
            return string.IsNullOrEmpty(rawPol.PresentationID)
                ? null
                : ResolvePresentation(rawPol.PresentationID, rawPol.DefinedIn);
        }

        private void ResolveReferences(
            Dictionary<string, PolicyManagerCategory> catIds,
            Dictionary<string, PolicyManagerProduct> productIds,
            Dictionary<string, PolicyManagerSupport> supIds,
            Dictionary<string, PolicyManagerPolicy> polIds)
        {
            ResolveCategoryParents(catIds);
            ResolveProductParents(productIds);
            ResolveSupportTargets(supIds, productIds);
            ResolvePolicyLinks(polIds, supIds, catIds);
        }

        private void ResolveCategoryParents(Dictionary<string, PolicyManagerCategory> catIds)
        {
            foreach (var cat in catIds.Values)
            {
                if (string.IsNullOrEmpty(cat.RawCategory.ParentID)) continue;

                var parentCatName = ResolveRef(cat.RawCategory.ParentID, cat.RawCategory.DefinedIn);
                var parentCat = FindInTempOrFlat(parentCatName, catIds, FlatCategories);
                if (parentCat is null) continue;

                parentCat.Children.Add(cat);
                cat.Parent = parentCat;
            }
        }

        private void ResolveProductParents(Dictionary<string, PolicyManagerProduct> productIds)
        {
            foreach (var product in productIds.Values)
            {
                if (product.RawProduct.Parent is null) continue;

                var parentProductId = QualifyName(product.RawProduct.Parent.ID, product.RawProduct.DefinedIn);
                var parentProduct = FindInTempOrFlat(parentProductId, productIds, FlatProducts);
                if (parentProduct is null) continue;

                parentProduct.Children.Add(product);
                product.Parent = parentProduct;
            }
        }

        private void ResolveSupportTargets(
            Dictionary<string, PolicyManagerSupport> supIds,
            Dictionary<string, PolicyManagerProduct> productIds)
        {
            foreach (var sup in supIds.Values)
            {
                foreach (var supEntry in sup.Elements)
                {
                    var targetId = ResolveRef(supEntry.RawSupportEntry.ProductID, sup.RawSupport.DefinedIn);
                    supEntry.Product = FindInTempOrFlat(targetId, productIds, FlatProducts);
                    if (supEntry.Product is null)
                        supEntry.SupportDefinition = FindInTempOrFlat(targetId, supIds, SupportDefinitions);
                }
            }
        }

        private void ResolvePolicyLinks(
            Dictionary<string, PolicyManagerPolicy> polIds,
            Dictionary<string, PolicyManagerSupport> supIds,
            Dictionary<string, PolicyManagerCategory> catIds)
        {
            foreach (var pol in polIds.Values)
            {
                ResolvePolicyCategory(pol, catIds);
                var supportId = ResolveRef(pol.RawPolicy.SupportedCode, pol.RawPolicy.DefinedIn);
                pol.SupportedOn = FindInTempOrFlat(supportId, supIds, SupportDefinitions);
            }
        }

        private void ResolvePolicyCategory(PolicyManagerPolicy pol, Dictionary<string, PolicyManagerCategory> catIds)
        {
            var catId = ResolveRef(pol.RawPolicy.CategoryID, pol.RawPolicy.DefinedIn);
            var ownerCat = FindInTempOrFlat(catId, catIds, FlatCategories);
            if (ownerCat is null) return;

            ownerCat.Policies.Add(pol);
            pol.Category = ownerCat;
        }

        private void PublishItems(
            Dictionary<string, PolicyManagerCategory> catIds,
            Dictionary<string, PolicyManagerProduct> productIds,
            Dictionary<string, PolicyManagerSupport> supIds,
            Dictionary<string, PolicyManagerPolicy> polIds)
        {
            AddPublishedCategories(catIds);
            AddPublishedProducts(productIds);
            AddAllMissing(Policies, polIds);
            AddAllMissing(SupportDefinitions, supIds);
        }

        private void AddPublishedCategories(Dictionary<string, PolicyManagerCategory> catIds)
        {
            AddAllMissing(FlatCategories, catIds);
            AddTopLevelItems(Categories, catIds);
        }

        private void AddPublishedProducts(Dictionary<string, PolicyManagerProduct> productIds)
        {
            AddAllMissing(FlatProducts, productIds);
            AddTopLevelItems(Products, productIds);
        }

        private static void AddTopLevelItems<T>(Dictionary<string, T> destination, Dictionary<string, T> source)
            where T : class
        {
            foreach (var kvp in source)
            {
                if (GetParent(kvp.Value) is null && !destination.ContainsKey(kvp.Key))
                    destination.Add(kvp.Key, kvp.Value);
            }
        }

        private static object? GetParent<T>(T item)
            where T : class
        {
            return item switch
            {
                PolicyManagerCategory category => category.Parent,
                PolicyManagerProduct product => product.Parent,
                _ => null,
            };
        }

        private static void AddAllMissing<T>(Dictionary<string, T> destination, Dictionary<string, T> source)
        {
            foreach (var kvp in source)
            {
                AddIfMissing(destination, kvp.Key, kvp.Value);
            }
        }

        private static void AddIfMissing<T>(Dictionary<string, T> dictionary, string key, T value)
        {
            if (!dictionary.ContainsKey(key))
                dictionary.Add(key, value);
        }

        private static T? FindInTempOrFlat<T>(string uid, Dictionary<string, T> tempDict, Dictionary<string, T>? flatDict)
            where T : class
        {
            if (tempDict.ContainsKey(uid)) return tempDict[uid];
            if (flatDict != null && flatDict.ContainsKey(uid)) return flatDict[uid];
            return null;
        }

        public string ResolveString(string displayCode, AdmxFile admx)
        {
            if (string.IsNullOrEmpty(displayCode)) return "";
            if (!displayCode.StartsWith("$(string.")) return displayCode;
            var stringId = displayCode.Substring(9, displayCode.Length - 10);
            var dict = SourceFiles[admx].StringTable;
            if (dict.ContainsKey(stringId)) return dict[stringId];
            return displayCode;
        }

        public Presentation? ResolvePresentation(string displayCode, AdmxFile admx)
        {
            if (!displayCode.StartsWith("$(presentation.")) return null;
            var presId = displayCode.Substring(15, displayCode.Length - 16);
            var dict = SourceFiles[admx].PresentationTable;
            if (dict.ContainsKey(presId)) return dict[presId];
            return null;
        }

        private string QualifyName(string id, AdmxFile admx)
        {
            return admx.AdmxNamespace + ":" + id;
        }

        private string ResolveRef(string refStr, AdmxFile admx)
        {
            // Get a fully qualified name from a code and the current scope
            if (refStr.Contains(":"))
            {
                var parts = refStr.Split(new[] { ':' }, 2);
                if (admx.Prefixes.ContainsKey(parts[0]))
                {
                    var srcNamespace = admx.Prefixes[parts[0]];
                    return srcNamespace + ":" + parts[1];
                }
                else
                {
                    return refStr; // Assume literal
                }
            }
            else
            {
                return QualifyName(refStr, admx);
            }
        }

        private void LocalizeEnumItems(AdmxPolicy rawPol)
        {
            if (rawPol.Elements == null) return;

            foreach (var elem in rawPol.Elements)
            {
                if (elem is EnumPolicyElement enumElem)
                {
                    foreach (var item in enumElem.Items)
                    {
                        item.DisplayCode = ResolveString(item.DisplayCode, rawPol.DefinedIn);
                    }
                }
            }
        }
    }

    public enum AdmxLoadFailType
    {
        BadAdmxParse,
        BadAdmx,
        NoAdml,
        BadAdmlParse,
        BadAdml,
        DuplicateNamespace
    }

    public class AdmxLoadFailure
    {
        public AdmxLoadFailType FailType { get; set; }
        public string AdmxPath { get; set; }
        public string Info { get; set; }

        public AdmxLoadFailure(AdmxLoadFailType failType, string admxPath, string info = "")
        {
            FailType = failType;
            AdmxPath = admxPath;
            Info = info;
        }

        public override string ToString()
        {
            var failMsg = "Couldn't load " + AdmxPath + ": " + GetFailMessage(FailType, Info);
            if (!failMsg.EndsWith(".")) failMsg += ".";
            return failMsg;
        }

        private static string GetFailMessage(AdmxLoadFailType failType, string info)
        {
            switch (failType)
            {
                case AdmxLoadFailType.BadAdmxParse:
                    return "The ADMX XML couldn't be parsed: " + info;
                case AdmxLoadFailType.BadAdmx:
                    return "The ADMX is invalid: " + info;
                case AdmxLoadFailType.NoAdml:
                    return "The corresponding ADML is missing";
                case AdmxLoadFailType.BadAdmlParse:
                    return "The ADML XML couldn't be parsed: " + info;
                case AdmxLoadFailType.BadAdml:
                    return "The ADML is invalid: " + info;
                case AdmxLoadFailType.DuplicateNamespace:
                    return "The " + info + " namespace is already owned by a different ADMX file";
            }
            if (string.IsNullOrEmpty(info)) return "An unknown error occurred";
            return info;
        }
    }
}


