<#
.SYNOPSIS
Dumps the vtable order of COM interfaces straight from a type library (usually embedded in the
server DLL on the running OS).

.DESCRIPTION
The Native AOT migration (doc/NativeAotMigration.md, M3) ports IDispatch/dual and [ComImport]
interfaces to [GeneratedComInterface], where the C# member declaration ORDER is the vtable order —
getting it wrong silently corrupts every downstream slot. The authoritative source for that order
is the type library registered on the machine itself, not IDL copies found online. This script
loads a typelib with LoadTypeLibEx(REGKIND_NONE) and prints, for each requested interface GUID,
every function in vtable-slot order with its invoke kind (func / propget / propput), member ID and
parameter count.

Notes:
 - For TKIND_DISPATCH entries of dual interfaces, interface members begin at slot 7
   (IUnknown[3] + IDispatch[4]); the low slots show the inherited IUnknown/IDispatch members.
 - "cParams" excludes the [out, retval] parameter for property getters.

.EXAMPLE
./Dump-TypeLibVtable.ps1 -TypeLibPath C:\Windows\System32\Com\comadmin.dll -InterfaceGuids DD662187-DFC2-11D1-A2CF-00805FC79235

.EXAMPLE
./Dump-TypeLibVtable.ps1 -TypeLibPath C:\Windows\System32\azroles.dll -InterfaceGuids @('...IAzAuthorizationStore...', '...IAzApplication...')
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

public static class OneMmcTlbReader
{
    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void LoadTypeLibEx(string strTypeLibName, int regKind, out ITypeLib ppTLib);

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

            string tn, td, thf; int thc;
            ti.GetDocumentation(-1, out tn, out td, out thc, out thf);

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
                Console.WriteLine("  slot=" + slot.ToString().PadLeft(3) + " " + fd.invkind.ToString().PadRight(22) + " memid=0x" + fd.memid.ToString("X8") + " cParams=" + fd.cParams + " name=" + name);
                ti.ReleaseFuncDesc(pFd);
            }
        }
    }
}
'@ | Out-Null
}

[OneMmcTlbReader]::Dump($TypeLibPath, $InterfaceGuids)
