using System;
using System.Runtime.InteropServices;

namespace OneMMC.Core.Features.PCManagement.Services.TaskSchd.Native;

// Handwritten [ComImport] dual interfaces for the minimal Task Scheduler 2.0 COM surface.
//
// Why handwritten [ComImport] rather than CsWin32 (per CLAUDE.md "keep exceptions centralized"):
// CsWin32 *can* project these interfaces (validated), but it projects every string parameter as a
// raw Windows.Win32.Foundation.BSTR with no friendly string overload, and every VARIANT as a
// Windows.Win32.System.Variant.VARIANT struct, forcing manual SysAllocString/SysFreeString lifetime
// management and manual VARIANT construction at every call site. For this string-heavy automation
// (IDispatch dual) API that is error-prone and verbose. Handwritten [InterfaceType(InterfaceIsDual)]
// interfaces let the CLR auto-marshal string<->BSTR and object<->VARIANT, matching the existing
// Windows Firewall precedent (Features/SystemManagement/Interop/WF/ComInterfaces.cs).
//
// The member order, IIDs and signatures below were transcribed from the CsWin32-generated metadata
// (the authoritative vtable layout) and validated end-to-end against the live Task Scheduler service
// (register-from-XML -> run -> read state/result -> SDDL -> delete, plus remote Connect).
//
// The design is XML-centric: tasks are written via ITaskFolder.RegisterTask(xml) and read via
// IRegisteredTask.Xml, so the rich trigger/action/settings object model is never traversed through
// COM (it is mapped with LINQ-to-XML instead). That keeps the COM surface to these seven interfaces.
//
// VARIANT argument convention (validated):
//   * Truly-omitted optional VARIANT params (ITaskService.Connect) -> Type.Missing (VT_ERROR sentinel).
//   * Explicit "no value" VARIANT params (IRegisteredTask.Run params) -> null (VT_EMPTY).

/// <summary>Provides access to the Task Scheduler service for managing registered tasks.</summary>
[ComImport, Guid("2FABA4C7-4DA9-4013-9697-20CC3FD40F85"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
internal interface ITaskService
{
    void GetFolder([In, MarshalAs(UnmanagedType.BStr)] string path, [MarshalAs(UnmanagedType.Interface)] out ITaskFolder ppFolder);
    void GetRunningTasks(int flags, [MarshalAs(UnmanagedType.Interface)] out IRunningTaskCollection ppRunningTasks);
    void NewTask(uint flags, [MarshalAs(UnmanagedType.Interface)] out object ppDefinition);
    void Connect([In, MarshalAs(UnmanagedType.Struct)] object serverName, [In, MarshalAs(UnmanagedType.Struct)] object user, [In, MarshalAs(UnmanagedType.Struct)] object domain, [In, MarshalAs(UnmanagedType.Struct)] object password);
    bool Connected { [return: MarshalAs(UnmanagedType.VariantBool)] get; }
    string TargetServer { [return: MarshalAs(UnmanagedType.BStr)] get; }
    string ConnectedUser { [return: MarshalAs(UnmanagedType.BStr)] get; }
    string ConnectedDomain { [return: MarshalAs(UnmanagedType.BStr)] get; }
    uint HighestVersion { get; }
}

/// <summary>Provides methods to register/remove tasks and manage subfolders.</summary>
[ComImport, Guid("8CFAC062-A080-4C15-9A88-AA7C2AF80DFC"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
internal interface ITaskFolder
{
    string Name { [return: MarshalAs(UnmanagedType.BStr)] get; }
    string Path { [return: MarshalAs(UnmanagedType.BStr)] get; }
    void GetFolder([In, MarshalAs(UnmanagedType.BStr)] string path, [MarshalAs(UnmanagedType.Interface)] out ITaskFolder ppFolder);
    void GetFolders(int flags, [MarshalAs(UnmanagedType.Interface)] out ITaskFolderCollection ppFolders);
    void CreateFolder([In, MarshalAs(UnmanagedType.BStr)] string subFolderName, [In, MarshalAs(UnmanagedType.Struct)] object sddl, [MarshalAs(UnmanagedType.Interface)] out ITaskFolder ppFolder);
    void DeleteFolder([In, MarshalAs(UnmanagedType.BStr)] string subFolderName, int flags);
    void GetTask([In, MarshalAs(UnmanagedType.BStr)] string path, [MarshalAs(UnmanagedType.Interface)] out IRegisteredTask ppTask);
    void GetTasks(int flags, [MarshalAs(UnmanagedType.Interface)] out IRegisteredTaskCollection ppTasks);
    void DeleteTask([In, MarshalAs(UnmanagedType.BStr)] string name, int flags);
    void RegisterTask([In, MarshalAs(UnmanagedType.BStr)] string path, [In, MarshalAs(UnmanagedType.BStr)] string xmlText, int flags, [In, MarshalAs(UnmanagedType.Struct)] object userId, [In, MarshalAs(UnmanagedType.Struct)] object password, int logonType, [In, MarshalAs(UnmanagedType.Struct)] object sddl, [MarshalAs(UnmanagedType.Interface)] out IRegisteredTask ppTask);
    void RegisterTaskDefinition([In, MarshalAs(UnmanagedType.BStr)] string path, [In, MarshalAs(UnmanagedType.Interface)] object pDefinition, int flags, [In, MarshalAs(UnmanagedType.Struct)] object userId, [In, MarshalAs(UnmanagedType.Struct)] object password, int logonType, [In, MarshalAs(UnmanagedType.Struct)] object sddl, [MarshalAs(UnmanagedType.Interface)] out IRegisteredTask ppTask);
    void GetSecurityDescriptor(int securityInformation, [MarshalAs(UnmanagedType.BStr)] out string pSddl);
    void SetSecurityDescriptor([In, MarshalAs(UnmanagedType.BStr)] string sddl, int flags);
}

/// <summary>A collection of task folders.</summary>
[ComImport, Guid("79184A66-8664-423F-97F1-637356A5D812"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
internal interface ITaskFolderCollection
{
    int Count { get; }
    void get_Item([In, MarshalAs(UnmanagedType.Struct)] object index, [MarshalAs(UnmanagedType.Interface)] out ITaskFolder ppFolder);
    object _NewEnum { [return: MarshalAs(UnmanagedType.IUnknown)] get; }
}

/// <summary>A task registered with the Task Scheduler service.</summary>
[ComImport, Guid("9C86F320-DEE3-4DD1-B972-A303F26B061E"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
internal interface IRegisteredTask
{
    string Name { [return: MarshalAs(UnmanagedType.BStr)] get; }
    string Path { [return: MarshalAs(UnmanagedType.BStr)] get; }
    int State { get; }
    bool Enabled { [return: MarshalAs(UnmanagedType.VariantBool)] get; [param: MarshalAs(UnmanagedType.VariantBool)] set; }
    void Run([In, MarshalAs(UnmanagedType.Struct)] object @params, [MarshalAs(UnmanagedType.Interface)] out IRunningTask ppRunningTask);
    void RunEx([In, MarshalAs(UnmanagedType.Struct)] object @params, int flags, int sessionID, [In, MarshalAs(UnmanagedType.BStr)] string user, [MarshalAs(UnmanagedType.Interface)] out IRunningTask ppRunningTask);
    void GetInstances(int flags, [MarshalAs(UnmanagedType.Interface)] out IRunningTaskCollection ppRunningTasks);
    double LastRunTime { get; }
    int LastTaskResult { get; }
    int NumberOfMissedRuns { get; }
    double NextRunTime { get; }
    [return: MarshalAs(UnmanagedType.Interface)] object get_Definition();
    string Xml { [return: MarshalAs(UnmanagedType.BStr)] get; }
    void GetSecurityDescriptor(int securityInformation, [MarshalAs(UnmanagedType.BStr)] out string pSddl);
    void SetSecurityDescriptor([In, MarshalAs(UnmanagedType.BStr)] string sddl, int flags);
    void Stop(int flags);
    void GetRunTimes(IntPtr pstStart, IntPtr pstEnd, ref uint pCount, out IntPtr pRunTimes);
}

/// <summary>A collection of registered tasks.</summary>
[ComImport, Guid("86627EB4-42A7-41E4-A4D9-AC33A72F2D52"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
internal interface IRegisteredTaskCollection
{
    int Count { get; }
    void get_Item([In, MarshalAs(UnmanagedType.Struct)] object index, [MarshalAs(UnmanagedType.Interface)] out IRegisteredTask ppRegisteredTask);
    object _NewEnum { [return: MarshalAs(UnmanagedType.IUnknown)] get; }
}

/// <summary>Provides control and information for a currently running task.</summary>
[ComImport, Guid("653758FB-7B9A-4F1E-A471-BEEB8E9B834E"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
internal interface IRunningTask
{
    string Name { [return: MarshalAs(UnmanagedType.BStr)] get; }
    string InstanceGuid { [return: MarshalAs(UnmanagedType.BStr)] get; }
    string Path { [return: MarshalAs(UnmanagedType.BStr)] get; }
    int State { get; }
    string CurrentAction { [return: MarshalAs(UnmanagedType.BStr)] get; }
    void Stop();
    void Refresh();
    uint EnginePID { get; }
}

/// <summary>A collection of running tasks.</summary>
[ComImport, Guid("6A67614B-6828-4FEC-AA54-6D52E8F1F2DB"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
internal interface IRunningTaskCollection
{
    int Count { get; }
    void get_Item([In, MarshalAs(UnmanagedType.Struct)] object index, [MarshalAs(UnmanagedType.Interface)] out IRunningTask ppRunningTask);
    object _NewEnum { [return: MarshalAs(UnmanagedType.IUnknown)] get; }
}
