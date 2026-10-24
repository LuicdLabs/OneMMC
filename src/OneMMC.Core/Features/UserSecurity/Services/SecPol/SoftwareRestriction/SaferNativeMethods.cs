using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.Security.AppLocker;
using Win32PInvoke = Windows.Win32.PInvoke;

namespace OneMMC.Core.Features.UserSecurity.Services.SecPol.SoftwareRestriction;

/// <summary>
/// Native SAFER (Software Restriction Policies) entry points used by <see cref="SaferPolicySession"/>.
/// </summary>
/// <remarks>
/// The documented <c>Safer*</c> functions come from CsWin32. <c>SaferiChangeRegistryScope</c> and
/// <c>SaferiPopulateDefaultsInRegistry</c> are undocumented advapi32 exports that are absent from the
/// Windows SDK metadata, so CsWin32 cannot emit them; they are bound once through
/// <see cref="NativeLibrary"/> as unmanaged function pointers (no marshalling, Native AOT safe).
/// Both are what secpol.msc's Software Restriction Policies node (certmgr.dll) calls to point SAFER at
/// a Group Policy Object's registry buffer and to create a new policy.
/// </remarks>
internal static unsafe class SaferNativeMethods
{
    /// <summary>SAFER_SCOPEID_REGISTRY: SAFER reads and writes under the root set by <see cref="ChangeRegistryScope"/>.</summary>
    public const uint ScopeRegistry = 3;

    /// <summary>SAFER_LEVEL_OPEN.</summary>
    private const uint LevelOpen = 1;

    private const string Advapi32LibraryName = "advapi32.dll";

    private static readonly delegate* unmanaged[Stdcall]<nint, uint, int> s_changeRegistryScope;
    private static readonly delegate* unmanaged[Stdcall]<nint, int*, int> s_populateDefaultsInRegistry;

    static SaferNativeMethods()
    {
        // advapi32 is always mapped in a Windows process, so the handle is intentionally never freed.
        nint library = NativeLibrary.Load(Advapi32LibraryName);
        s_changeRegistryScope = (delegate* unmanaged[Stdcall]<nint, uint, int>)NativeLibrary.GetExport(library, "SaferiChangeRegistryScope");
        s_populateDefaultsInRegistry = (delegate* unmanaged[Stdcall]<nint, int*, int>)NativeLibrary.GetExport(library, "SaferiPopulateDefaultsInRegistry");
    }

    /// <summary>
    /// Redirects every <see cref="ScopeRegistry"/> SAFER call in this process to <paramref name="saferKey"/>
    /// (the <c>Software\Policies\Microsoft\Windows\Safer</c> key of the target hive), or restores the
    /// default machine scope when <paramref name="saferKey"/> is <see langword="null"/>.
    /// </summary>
    public static void ChangeRegistryScope(SafeRegistryHandle? saferKey)
    {
        bool addedRef = false;
        try
        {
            saferKey?.DangerousAddRef(ref addedRef);
            nint handle = saferKey?.DangerousGetHandle() ?? 0;
            if (s_changeRegistryScope(handle, 0) == 0)
            {
                throw new Win32Exception(Marshal.GetLastSystemError());
            }
        }
        finally
        {
            if (addedRef)
            {
                saferKey!.DangerousRelease();
            }
        }
    }

    /// <summary>
    /// Writes the default Software Restriction Policies (levels, designated file types, enforcement
    /// values and the SystemRoot/ProgramFilesDir Unrestricted path rules) under <paramref name="hiveRoot"/>.
    /// </summary>
    /// <returns><see langword="true"/> when defaults were written; <see langword="false"/> when a policy already existed.</returns>
    public static bool PopulateDefaultsInRegistry(SafeRegistryHandle hiveRoot)
    {
        bool addedRef = false;
        try
        {
            hiveRoot.DangerousAddRef(ref addedRef);
            int setDefaults = 0;
            if (s_populateDefaultsInRegistry(hiveRoot.DangerousGetHandle(), &setDefaults) != 0)
            {
                return setDefaults != 0;
            }

            // When a policy already exists, advapi32 clears *pbSetDefaults but still fails with the
            // STATUS_BUFFER_OVERFLOW from its fixed-size ExecutableTypes probe; certmgr treats that as success.
            if (setDefaults == 0)
            {
                return false;
            }

            throw new Win32Exception(Marshal.GetLastSystemError());
        }
        finally
        {
            if (addedRef)
            {
                hiveRoot.DangerousRelease();
            }
        }
    }

    /// <summary>Opens a security level in the current registry scope.</summary>
    public static SAFER_LEVEL_HANDLE OpenLevel(uint levelId)
    {
        SAFER_LEVEL_HANDLE level;
        if (!Win32PInvoke.SaferCreateLevel(ScopeRegistry, levelId, LevelOpen, &level, null))
        {
            throw new Win32Exception(Marshal.GetLastSystemError());
        }

        return level;
    }

    /// <summary>Closes a level handle returned by <see cref="OpenLevel"/>.</summary>
    public static void CloseLevel(SAFER_LEVEL_HANDLE level)
    {
        _ = Win32PInvoke.SaferCloseLevel(level);
    }

    /// <summary>Lists the identification GUIDs of every rule stored under <paramref name="level"/>.</summary>
    public static List<Guid> GetIdentificationGuids(SAFER_LEVEL_HANDLE level)
    {
        uint required = 0;
        if (Win32PInvoke.SaferGetLevelInformation(level, SAFER_OBJECT_INFO_CLASS.SaferObjectAllIdentificationGuids, null, 0, &required))
        {
            return [];
        }

        int error = Marshal.GetLastSystemError();
        if (error is (int)WIN32_ERROR.ERROR_FILE_NOT_FOUND or (int)WIN32_ERROR.ERROR_NOT_FOUND || required == 0)
        {
            return [];
        }

        if (error != (int)WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER)
        {
            throw new Win32Exception(error);
        }

        Guid[] guids = new Guid[required / (uint)sizeof(Guid)];
        fixed (Guid* buffer = guids)
        {
            if (!Win32PInvoke.SaferGetLevelInformation(level, SAFER_OBJECT_INFO_CLASS.SaferObjectAllIdentificationGuids, buffer, required, &required))
            {
                throw new Win32Exception(Marshal.GetLastSystemError());
            }
        }

        return [.. guids];
    }

    /// <summary>
    /// Reads one rule. Mirrors certmgr: a header-only query with the GUID reports the required size, then
    /// the full identification structure is fetched (path rules carry their image path in the same buffer).
    /// </summary>
    public static SaferIdentification? GetIdentification(SAFER_LEVEL_HANDLE level, Guid id)
    {
        byte[] header = SaferIdentification.CreateHeader(SaferIdentificationType.Default, id, SaferIdentification.HeaderSize);
        uint required = (uint)header.Length;
        fixed (byte* headerBuffer = header)
        {
            if (Win32PInvoke.SaferGetLevelInformation(level, SAFER_OBJECT_INFO_CLASS.SaferObjectSingleIdentification, headerBuffer, (uint)header.Length, &required))
            {
                return SaferIdentification.Parse(header);
            }
        }

        int error = Marshal.GetLastSystemError();
        if (error is (int)WIN32_ERROR.ERROR_FILE_NOT_FOUND or (int)WIN32_ERROR.ERROR_NOT_FOUND)
        {
            return null;
        }

        if (error != (int)WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER)
        {
            throw new Win32Exception(error);
        }

        // NativeMemory keeps the buffer at a fixed address: the returned ImageName pointer refers into it.
        void* buffer = NativeMemory.AllocZeroed(required);
        try
        {
            Span<byte> span = new(buffer, (int)required);
            SaferIdentification.CreateHeader(SaferIdentificationType.Default, id, required).CopyTo(span);
            if (!Win32PInvoke.SaferGetLevelInformation(level, SAFER_OBJECT_INFO_CLASS.SaferObjectSingleIdentification, buffer, required, &required))
            {
                throw new Win32Exception(Marshal.GetLastSystemError());
            }

            return SaferIdentification.Parse(span);
        }
        finally
        {
            NativeMemory.Free(buffer);
        }
    }

    /// <summary>
    /// Creates or replaces one rule under <paramref name="level"/>, or deletes it when
    /// <paramref name="delete"/> is set; SAFER writes the CodeIdentifiers registry values itself.
    /// </summary>
    /// <remarks>
    /// certmgr deletes a rule by passing its full identification buffer back with
    /// <c>dwIdentificationType</c> set to 0 (<c>SaferIdentityDefault</c>); the same is done here.
    /// </remarks>
    public static void SetIdentification(SAFER_LEVEL_HANDLE level, SaferIdentification identification, bool delete = false)
    {
        byte[] payload = identification.Serialize(out string? imagePath);
        if (delete)
        {
            SaferIdentification.MarkAsDelete(payload);
        }

        fixed (byte* buffer = payload)
        fixed (char* imagePathBuffer = imagePath)
        {
            if (imagePath is not null)
            {
                SaferIdentification.SetImagePathPointer(payload, (nint)imagePathBuffer);
            }

            if (!Win32PInvoke.SaferSetLevelInformation(level, SAFER_OBJECT_INFO_CLASS.SaferObjectSingleIdentification, buffer, (uint)payload.Length))
            {
                throw new Win32Exception(Marshal.GetLastSystemError());
            }
        }
    }

    /// <summary>Reads a DWORD policy value in the current registry scope.</summary>
    public static uint? GetPolicyDword(SAFER_POLICY_INFO_CLASS infoClass)
    {
        uint value = 0;
        uint returned = 0;
        return Win32PInvoke.SaferGetPolicyInformation(ScopeRegistry, infoClass, sizeof(uint), &value, &returned, null)
            ? value
            : null;
    }

    /// <summary>Writes a DWORD policy value in the current registry scope.</summary>
    public static void SetPolicyDword(SAFER_POLICY_INFO_CLASS infoClass, uint value)
    {
        if (!Win32PInvoke.SaferSetPolicyInformation(ScopeRegistry, infoClass, sizeof(uint), &value, null))
        {
            throw new Win32Exception(Marshal.GetLastSystemError());
        }
    }
}
