using System;
using System.Collections.Generic;
using OneMMC.Core.Infrastructure.Interop;

namespace OneMMC.Core.Features.UserSecurity.Services.AzMan.Native;

/// <summary>
/// Activation and marshalling helpers for the AzRoles COM surface declared in
/// <see cref="IAzAuthorizationStore3"/> and friends: coclass creation, LONG-typed boolean
/// conversion, one-based collection enumeration, and safe release. See <c>AzRolesNative.cs</c>
/// for the rationale behind the source-generated interfaces.
/// </summary>
internal static class AzRolesCom
{
    /// <summary>CLSID of the <c>AzRoles.AzAuthorizationStore</c> coclass
    /// ({B2BCFF59-A757-4B0B-A1BC-EA69981DA69E}, ThreadingModel=Both). Verified as the target of the
    /// version-independent ProgID; activated via <see cref="ComActivator"/> (AOT-safe) rather than a
    /// reflection-activated ProgID.</summary>
    private static readonly Guid ClsidAzAuthorizationStore = new("B2BCFF59-A757-4B0B-A1BC-EA69981DA69E");

    /// <summary>
    /// Creates a fresh authorization-store coclass instance. The caller owns the returned reference
    /// (release with <see cref="Release"/>) and must call
    /// <see cref="IAzAuthorizationStore3.Initialize"/> before any other member.
    /// </summary>
    internal static IAzAuthorizationStore3 CreateStore() =>
        ComActivator.CreateInstance<IAzAuthorizationStore3>(ClsidAzAuthorizationStore);

    /// <summary>Converts an AzRoles LONG-typed boolean property value to <see cref="bool"/>.</summary>
    internal static bool ToBool(int value) => value != 0;

    /// <summary>Converts a <see cref="bool"/> to an AzRoles LONG-typed boolean property value.</summary>
    internal static int FromBool(bool value) => value ? 1 : 0;

    /// <summary>Releases a source-generated COM wrapper; ignores managed objects and nulls.</summary>
    internal static void Release(object? comObject) => ComActivator.Release(comObject);

    /// <summary>Releases several source-generated COM wrappers (nulls ignored).</summary>
    internal static void Release(params object?[] comObjects)
    {
        foreach (object? comObject in comObjects)
        {
            ComActivator.Release(comObject);
        }
    }

    // AzMan collections are ONE-based: Item(0) fails with E_INVALIDARG, Item(1) is the first element
    // (verified live against the OS coclass). Each item VARIANT carries an IDispatch reference that is
    // wrapped as the typed interface; callers own the returned wrappers and must Release each one.

    /// <summary>Materializes an <see cref="IAzApplications"/> collection; caller releases the items.</summary>
    internal static List<IAzApplication> Items(this IAzApplications collection) =>
        ReadItems<IAzApplications, IAzApplication>(collection, static c => c.get_Count(), static (IAzApplications c, int i, out Variant v) => c.get_Item(i, out v));

    /// <summary>Materializes an <see cref="IAzApplicationGroups"/> collection; caller releases the items.</summary>
    internal static List<IAzApplicationGroup2> Items(this IAzApplicationGroups collection) =>
        ReadItems<IAzApplicationGroups, IAzApplicationGroup2>(collection, static c => c.get_Count(), static (IAzApplicationGroups c, int i, out Variant v) => c.get_Item(i, out v));

    /// <summary>Materializes an <see cref="IAzRoles"/> collection; caller releases the items.</summary>
    internal static List<IAzRole> Items(this IAzRoles collection) =>
        ReadItems<IAzRoles, IAzRole>(collection, static c => c.get_Count(), static (IAzRoles c, int i, out Variant v) => c.get_Item(i, out v));

    /// <summary>Materializes an <see cref="IAzTasks"/> collection; caller releases the items.</summary>
    internal static List<IAzTask> Items(this IAzTasks collection) =>
        ReadItems<IAzTasks, IAzTask>(collection, static c => c.get_Count(), static (IAzTasks c, int i, out Variant v) => c.get_Item(i, out v));

    /// <summary>Materializes an <see cref="IAzOperations"/> collection; caller releases the items.</summary>
    internal static List<IAzOperation> Items(this IAzOperations collection) =>
        ReadItems<IAzOperations, IAzOperation>(collection, static c => c.get_Count(), static (IAzOperations c, int i, out Variant v) => c.get_Item(i, out v));

    /// <summary>Materializes an <see cref="IAzScopes"/> collection; caller releases the items.</summary>
    internal static List<IAzScope> Items(this IAzScopes collection) =>
        ReadItems<IAzScopes, IAzScope>(collection, static c => c.get_Count(), static (IAzScopes c, int i, out Variant v) => c.get_Item(i, out v));

    private delegate void ItemGetter<in TCollection>(TCollection collection, int index, out Variant item);

    private static List<TItem> ReadItems<TCollection, TItem>(
        TCollection collection,
        Func<TCollection, int> count,
        ItemGetter<TCollection> item)
        where TItem : class
    {
        int n = count(collection);
        var result = new List<TItem>(n);
        for (int i = 1; i <= n; i++)
        {
            item(collection, i, out Variant variant);
            try
            {
                // The wrapper takes its own reference before the item VARIANT's is released.
                if (variant.ToComInterface<TItem>() is { } wrapped)
                {
                    result.Add(wrapped);
                }
            }
            finally
            {
                variant.Clear();
            }
        }

        return result;
    }
}
