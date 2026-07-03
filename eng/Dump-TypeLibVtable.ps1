<#
.SYNOPSIS
Dumps the vtable order and member signatures of COM interfaces straight from a type library
(usually embedded in the server DLL on the running OS).

.DESCRIPTION
The Native AOT migration (doc/NativeAotMigration.md, M3) ports IDispatch/dual and [ComImport]
interfaces to [GeneratedComInterface], where the C# member declaration ORDER is the vtable order —
getting it wrong silently corrupts every downstream slot — and where every parameter must be
declared explicitly (late binding filled optional/reserved VARIANTs automatically; vtable calls do
not). The authoritative source for both is the type library registered on the machine itself, not
IDL copies found online. This script loads a typelib with LoadTypeLibEx(REGKIND_NONE) and prints,
for each requested interface GUID, every function in vtable-slot order with its invoke kind
(func / propget / propput), member ID, and decoded parameter/return types (following VT_PTR,
VT_SAFEARRAY and VT_USERDEFINED indirections).

Notes:
 - For TKIND_DISPATCH entries of dual interfaces, interface members begin at slot 7
   (IUnknown[3] + IDispatch[4]); the low slots show the inherited IUnknown/IDispatch members.
 - Parameter flags: [in]/[out]/[retval]/[opt] from PARAMDESC.

.EXAMPLE
./Dump-TypeLibVtable.ps1 -TypeLibPath C:\Windows\System32\Com\comadmin.dll -InterfaceGuids DD662187-DFC2-11D1-A2CF-00805FC79235

.EXAMPLE
./Dump-TypeLibVtable.ps1 -TypeLibPath C:\Windows\System32\azroles.dll   # list all types
#>
param(
    [Parameter(Mandatory)][string]$TypeLibPath,
    # Interface GUIDs (with or without braces). Omit to list every type in the library.
    [string[]]$InterfaceGuids
)

if (-not ([System.Management.Automation.PSTypeName]'OneMmcTlbReader').Type) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

public static class OneMmcTlbReader
{
    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void LoadTypeLibEx(string strTypeLibName, int regKind, out ITypeLib ppTLib);

    private static string VtName(short vt)
    {
        switch (vt & 0x0FFF)
        {
            case 0: return "VT_EMPTY"; case 1: return "VT_NULL"; case 2: return "I2"; case 3: return "I4";
            case 4: return "R4"; case 5: return "R8"; case 6: return "CY"; case 7: return "DATE";
            case 8: return "BSTR"; case 9: return "IDispatch*"; case 10: return "SCODE"; case 11: return "VARIANT_BOOL";
            case 12: return "VARIANT"; case 13: return "IUnknown*"; case 14: return "DECIMAL"; case 16: return "I1";
            case 17: return "UI1"; case 18: return "UI2"; case 19: return "UI4"; case 20: return "I8"; case 21: return "UI8";
            case 22: return "INT"; case 23: return "UINT"; case 24: return "void"; case 25: return "HRESULT";
            case 26: return "PTR"; case 27: return "SAFEARRAY"; case 28: return "CARRAY"; case 29: return "USERDEFINED";
            case 30: return "LPSTR"; case 31: return "LPWSTR";
            default: return "VT_" + vt;
        }
    }

    // Decode a TYPEDESC, following PTR/SAFEARRAY element and resolving USERDEFINED to its name.
    private static string DecodeTypeDesc(ITypeInfo ti, TYPEDESC td)
    {
        short vt = td.vt;
        if (vt == 26 /* VT_PTR */)
        {
            var inner = (TYPEDESC)Marshal.PtrToStructure(td.lpValue, typeof(TYPEDESC));
            return DecodeTypeDesc(ti, inner) + "*";
        }
        if (vt == 27 /* VT_SAFEARRAY */)
        {
            var inner = (TYPEDESC)Marshal.PtrToStructure(td.lpValue, typeof(TYPEDESC));
            return "SAFEARRAY(" + DecodeTypeDesc(ti, inner) + ")";
        }
        if (vt == 29 /* VT_USERDEFINED */)
        {
            try
            {
                ITypeInfo refTi;
                ti.GetRefTypeInfo((int)td.lpValue, out refTi);
                string n, d, hf; int hc;
                refTi.GetDocumentation(-1, out n, out d, out hc, out hf);
                return n;
            }
            catch { return "USERDEFINED"; }
        }
        return VtName(vt);
    }

    private static string ParamFlags(PARAMDESC pd)
    {
        var f = (int)pd.wParamFlags;
        var sb = new StringBuilder();
        if ((f & 0x1) != 0) sb.Append("in");
        if ((f & 0x2) != 0) { if (sb.Length > 0) sb.Append(','); sb.Append("out"); }
        if ((f & 0x8) != 0) { if (sb.Length > 0) sb.Append(','); sb.Append("retval"); }
        if ((f & 0x10) != 0) { if (sb.Length > 0) sb.Append(','); sb.Append("opt"); }
        return sb.Length == 0 ? "-" : sb.ToString();
    }

    public static void Dump(string path, string[] guids)
    {
        var wanted = new List<Guid>();
        if (guids != null) { foreach (var g in guids) wanted.Add(new Guid(g)); }

        ITypeLib tlb;
        LoadTypeLibEx(path, 2 /* REGKIND_NONE: inspect without registering */, out tlb);
        int n = tlb.GetTypeInfoCount();
        for (int i = 0; i < n; i++)
        {
            ITypeInfo ti;
            tlb.GetTypeInfo(i, out ti);
            IntPtr pAttr;
            ti.GetTypeAttr(out pAttr);
            var attr = (TYPEATTR)Marshal.PtrToStructure(pAttr, typeof(TYPEATTR));
            Guid g = attr.guid;
            int cFuncs = attr.cFuncs;
            TYPEKIND kind = attr.typekind;
            ti.ReleaseTypeAttr(pAttr);

            string tn, tdoc, thf; int thc;
            ti.GetDocumentation(-1, out tn, out tdoc, out thc, out thf);

            if (wanted.Count == 0)
            {
                Console.WriteLine(g.ToString("B").ToUpperInvariant() + "  " + kind + "  " + tn);
                continue;
            }
            if (!wanted.Contains(g)) continue;

            Console.WriteLine("=== " + tn + "  " + g.ToString("B").ToUpperInvariant() + "  typekind=" + kind + "  cFuncs=" + cFuncs);
            for (int f = 0; f < cFuncs; f++)
            {
                IntPtr pFd;
                ti.GetFuncDesc(f, out pFd);
                var fd = (FUNCDESC)Marshal.PtrToStructure(pFd, typeof(FUNCDESC));
                string name, docs, hf; int hc;
                ti.GetDocumentation(fd.memid, out name, out docs, out hc, out hf);
                int slot = fd.oVft / IntPtr.Size;

                var sb = new StringBuilder();
                int elemSize = Marshal.SizeOf(typeof(ELEMDESC));
                for (int p = 0; p < fd.cParams; p++)
                {
                    var ed = (ELEMDESC)Marshal.PtrToStructure(fd.lprgelemdescParam + p * elemSize, typeof(ELEMDESC));
                    if (p > 0) sb.Append(", ");
                    sb.Append('[').Append(ParamFlags(ed.desc.paramdesc)).Append("] ").Append(DecodeTypeDesc(ti, ed.tdesc));
                }
                string ret = DecodeTypeDesc(ti, fd.elemdescFunc.tdesc);

                Console.WriteLine("  slot=" + slot.ToString().PadLeft(3) + " " + fd.invkind.ToString().Replace("INVOKE_", "").PadRight(12)
                    + " " + name + "(" + sb + ") -> " + ret);
                ti.ReleaseFuncDesc(pFd);
            }
        }
    }
}
'@ | Out-Null
}

[OneMmcTlbReader]::Dump($TypeLibPath, $InterfaceGuids)
