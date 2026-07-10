using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.Networking.ActiveDirectory;
using Win32PInvoke = Windows.Win32.PInvoke;

namespace OneMMC.Core.Infrastructure.Interop.Adsi;

/// <summary>
/// Managed wrapper over a raw <c>IDirectorySearch*</c> — the marshal-free equivalent of
/// <c>System.DirectoryServices.DirectorySearcher</c> for the LDAP searches OneMMC performs.
/// Results are materialized eagerly (the result sets involved — GPO containers and printer
/// connection policies — are small).
/// </summary>
internal sealed unsafe class AdsiSearcher : IDisposable
{
    // ADS_SCOPEENUM values (iads.h).
    private const uint AdsScopeBase = 0;
    private const uint AdsScopeSubtree = 2;

    // Paged retrieval so result sets beyond the server's single-page limit still come back whole.
    private const uint PageSize = 1000;

    // Success codes signalling the end of row/column enumeration (adserr.h).
    private const int SAdsNoMoreRows = 0x00005012;
    private const int SAdsNoMoreColumns = 0x00005013;

    private IDirectorySearch* _search;

    internal AdsiSearcher(IDirectorySearch* search) => _search = search;

    /// <summary>One row of a search result: requested attribute name → first value as string.</summary>
    internal sealed class Row
    {
        internal Row(Dictionary<string, string> values) => Values = values;

        private Dictionary<string, string> Values { get; }

        /// <summary>The attribute's first value, or an empty string when absent (SearchResult parity).</summary>
        internal string this[string attribute] => Values.GetValueOrDefault(attribute, string.Empty);
    }

    /// <summary>
    /// Executes an LDAP search and returns one <see cref="Row"/> per result, reading only
    /// <paramref name="attributes"/> (<c>DirectorySearcher.PropertiesToLoad</c> parity).
    /// </summary>
    /// <param name="filter">LDAP filter string.</param>
    /// <param name="attributes">Attribute names to fetch per row.</param>
    /// <param name="subtree">true = subtree scope, false = base scope.</param>
    /// <param name="firstOnly">Stop after the first row (FindOne parity).</param>
    internal List<Row> Search(string filter, string[] attributes, bool subtree = true, bool firstOnly = false)
    {
        SetPreferences(subtree ? AdsScopeSubtree : AdsScopeBase);

        var rows = new List<Row>();
        ADS_SEARCH_HANDLE handle = ExecuteSearch(filter, attributes);
        try
        {
            for (HRESULT hr = _search->GetFirstRow(handle); hr.Value != SAdsNoMoreRows; hr = _search->GetNextRow(handle))
            {
                hr.ThrowOnFailure();

                var values = new Dictionary<string, string>(attributes.Length, StringComparer.OrdinalIgnoreCase);
                foreach (string attribute in attributes)
                {
                    string? value = TryGetColumnString(handle, attribute);
                    if (value is not null)
                    {
                        values[attribute] = value;
                    }
                }

                rows.Add(new Row(values));
                if (firstOnly)
                {
                    break;
                }
            }
        }
        finally
        {
            _search->CloseSearchHandle(handle);
        }

        return rows;
    }

    /// <summary>
    /// Base-scope read of every populated attribute of the bound object itself, as
    /// (name, first value) pairs in server order — the replacement for enumerating a
    /// <c>DirectoryEntry</c> property cache (used by the AzMan schema-attribute discovery).
    /// </summary>
    internal List<(string Name, string Value)> ReadAllAttributes()
    {
        SetPreferences(AdsScopeBase);

        var attributes = new List<(string, string)>();
        ADS_SEARCH_HANDLE handle = ExecuteSearch("(objectClass=*)", attributes: null);
        try
        {
            HRESULT row = _search->GetFirstRow(handle);
            row.ThrowOnFailure();
            if (row.Value == SAdsNoMoreRows)
            {
                return attributes;
            }

            while (true)
            {
                PWSTR columnName;
                HRESULT hr = _search->GetNextColumnName(handle, &columnName);
                hr.ThrowOnFailure();
                if (hr.Value == SAdsNoMoreColumns || columnName.Value is null)
                {
                    break;
                }

                string name;
                try
                {
                    name = columnName.ToString();
                }
                finally
                {
                    _ = Win32PInvoke.FreeADsMem(columnName.Value);
                }

                string? value = TryGetColumnString(handle, name);
                if (value is not null)
                {
                    attributes.Add((name, value));
                }
            }
        }
        finally
        {
            _search->CloseSearchHandle(handle);
        }

        return attributes;
    }

    /// <summary>Applies scope + paging preferences for the next ExecuteSearch.</summary>
    private void SetPreferences(uint scope)
    {
        var preferences = stackalloc ADS_SEARCHPREF_INFO[2];
        preferences[0].dwSearchPref = ADS_SEARCHPREF_ENUM.ADS_SEARCHPREF_SEARCH_SCOPE;
        preferences[0].vValue.dwType = ADSTYPE.ADSTYPE_INTEGER;
        preferences[0].vValue.Anonymous.Integer = scope;
        preferences[1].dwSearchPref = ADS_SEARCHPREF_ENUM.ADS_SEARCHPREF_PAGESIZE;
        preferences[1].vValue.dwType = ADSTYPE.ADSTYPE_INTEGER;
        preferences[1].vValue.Anonymous.Integer = PageSize;

        _search->SetSearchPreference(preferences, 2);
    }

    /// <summary>
    /// Starts a search. <paramref name="attributes"/> null means "all attributes"
    /// (ADSI's -1 sentinel). ADSI copies the filter and names, so the pinned/unmanaged copies
    /// are released before returning.
    /// </summary>
    private ADS_SEARCH_HANDLE ExecuteSearch(string filter, string[]? attributes)
    {
        nint[]? attributeCopies = null;
        try
        {
            uint count;
            PWSTR* pAttributes;
            nint* attributePointers = stackalloc nint[attributes?.Length ?? 0];
            if (attributes is null)
            {
                count = unchecked((uint)-1);
                pAttributes = null;
            }
            else
            {
                attributeCopies = new nint[attributes.Length];
                for (int i = 0; i < attributes.Length; i++)
                {
                    attributeCopies[i] = Marshal.StringToHGlobalUni(attributes[i]);
                    attributePointers[i] = attributeCopies[i];
                }
                count = (uint)attributes.Length;
                pAttributes = (PWSTR*)attributePointers;
            }

            fixed (char* pFilter = filter)
            {
                ADS_SEARCH_HANDLE handle;
                _search->ExecuteSearch(new PWSTR(pFilter), pAttributes, count, &handle);
                return handle;
            }
        }
        finally
        {
            if (attributeCopies is not null)
            {
                foreach (nint copy in attributeCopies)
                {
                    Marshal.FreeHGlobal(copy);
                }
            }
        }
    }

    /// <summary>
    /// Reads one column of the current row as a string, returning null when the attribute is not
    /// set on the row — via the raw vtable-slot call (GetColumn = slot 10, verified against the
    /// CsWin32 metadata projection) so per-row absent attributes don't raise first-chance
    /// <see cref="COMException"/>s (same PreserveSig principle as <see cref="AdsiObject.TryGet"/>).
    /// </summary>
    private string? TryGetColumnString(ADS_SEARCH_HANDLE handle, string attribute)
    {
        fixed (char* pAttribute = attribute)
        {
            ADS_SEARCH_COLUMN column = default;
            void** vtbl = *(void***)_search;
            int hr = ((delegate* unmanaged[Stdcall]<IDirectorySearch*, ADS_SEARCH_HANDLE, PWSTR, ADS_SEARCH_COLUMN*, int>)vtbl[10])(
                _search, handle, new PWSTR(pAttribute), &column);
            if (hr < 0)
            {
                return null;
            }

            try
            {
                return ColumnFirstValueToString(in column);
            }
            finally
            {
                _search->FreeColumn(&column);
            }
        }
    }

    /// <summary>Converts a column's first value to a display string (SearchResult [0].ToString() parity).</summary>
    private static string? ColumnFirstValueToString(in ADS_SEARCH_COLUMN column)
    {
        if (column.dwNumValues == 0 || column.pADsValues is null)
        {
            return null;
        }

        ADSVALUE* value = column.pADsValues;
        return value->dwType switch
        {
            ADSTYPE.ADSTYPE_DN_STRING or
            ADSTYPE.ADSTYPE_CASE_EXACT_STRING or
            ADSTYPE.ADSTYPE_CASE_IGNORE_STRING or
            ADSTYPE.ADSTYPE_PRINTABLE_STRING or
            ADSTYPE.ADSTYPE_NUMERIC_STRING or
            ADSTYPE.ADSTYPE_OBJECT_CLASS =>
                value->Anonymous.CaseIgnoreString is null ? null : new string((char*)value->Anonymous.CaseIgnoreString),
            ADSTYPE.ADSTYPE_INTEGER => value->Anonymous.Integer.ToString(CultureInfo.InvariantCulture),
            ADSTYPE.ADSTYPE_LARGE_INTEGER => value->Anonymous.LargeInteger.ToString(CultureInfo.InvariantCulture),
            ADSTYPE.ADSTYPE_BOOLEAN => value->Anonymous.Boolean != 0 ? bool.TrueString : bool.FalseString,
            _ => null,
        };
    }

    public void Dispose()
    {
        if (_search is not null)
        {
            _search->Release();
            _search = null;
        }
    }
}
