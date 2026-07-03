using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using OneMMC.Core.Infrastructure.Interop;

namespace OneMMC.Core.Features.PCManagement.Services.TaskSchd.Native;

// Source-generated ([GeneratedComInterface]) dual interfaces for the minimal Task Scheduler 2.0 COM
// surface. Ported from the previous handwritten [ComImport, InterfaceIsDual] declarations for Native
// AOT (doc/NativeAotMigration.md, M3): built-in COM interop / RCW dual-interface dispatch is
// unsupported under AOT, so these interfaces derive from the source-generated IDispatch base
// (Infrastructure/Interop/IDispatch.cs) to reproduce the dual vtable layout
// (IUnknown[3] + IDispatch[4] + members) and are called by vtable with no runtime marshaller.
//
// Conversion rules applied (member ORDER is the authoritative vtable order and must not change):
//   * IDispatch properties -> explicit get_/put_ methods in their original declaration order.
//   * VARIANT ([MarshalAs(Struct)] object) params -> Variant (System.Runtime.InteropServices.Marshalling).
//   * VARIANT_BOOL ([MarshalAs(VariantBool)] bool) -> raw short (-1 = true, 0 = false); callers convert.
//   * BSTR strings -> [MarshalAs(UnmanagedType.BStr)] string (source generator marshals string<->BSTR).
//   * Interface out-params -> the specific source-generated interface type.
//   * Members OneMMC never calls (NewTask, RegisterTaskDefinition, RunEx, get_Definition, _NewEnum,
//     GetRunTimes, and the unused ITaskService/IRunningTask accessors) are kept as vtable
//     placeholders with opaque nint signatures so downstream slot offsets stay correct.
//
// The design is XML-centric: tasks are written via ITaskFolder.RegisterTask(xml) and read via
// IRegisteredTask.Xml, so the rich trigger/action/settings object model is never traversed through
// COM (it is mapped with LINQ-to-XML instead). That keeps the COM surface to these seven interfaces.

/// <summary>Provides access to the Task Scheduler service for managing registered tasks.</summary>
[GeneratedComInterface, Guid("2FABA4C7-4DA9-4013-9697-20CC3FD40F85")]
internal partial interface ITaskService : IDispatch
{
    void GetFolder([MarshalAs(UnmanagedType.BStr)] string path, out ITaskFolder ppFolder);
    void GetRunningTasks(int flags, out IRunningTaskCollection ppRunningTasks);
    void NewTask(uint flags, out nint ppDefinition); // unused (placeholder)
    void Connect(Variant serverName, Variant user, Variant domain, Variant password);
    short get_Connected(); // VARIANT_BOOL
    [return: MarshalAs(UnmanagedType.BStr)] string get_TargetServer(); // unused (placeholder)
    [return: MarshalAs(UnmanagedType.BStr)] string get_ConnectedUser(); // unused (placeholder)
    [return: MarshalAs(UnmanagedType.BStr)] string get_ConnectedDomain(); // unused (placeholder)
    uint get_HighestVersion(); // unused (placeholder)
}

/// <summary>Provides methods to register/remove tasks and manage subfolders.</summary>
[GeneratedComInterface, Guid("8CFAC062-A080-4C15-9A88-AA7C2AF80DFC")]
internal partial interface ITaskFolder : IDispatch
{
    [return: MarshalAs(UnmanagedType.BStr)] string get_Name();
    [return: MarshalAs(UnmanagedType.BStr)] string get_Path();
    void GetFolder([MarshalAs(UnmanagedType.BStr)] string path, out ITaskFolder ppFolder);
    void GetFolders(int flags, out ITaskFolderCollection ppFolders);
    void CreateFolder([MarshalAs(UnmanagedType.BStr)] string subFolderName, Variant sddl, out ITaskFolder ppFolder);
    void DeleteFolder([MarshalAs(UnmanagedType.BStr)] string subFolderName, int flags);
    void GetTask([MarshalAs(UnmanagedType.BStr)] string path, out IRegisteredTask ppTask);
    void GetTasks(int flags, out IRegisteredTaskCollection ppTasks);
    void DeleteTask([MarshalAs(UnmanagedType.BStr)] string name, int flags);
    void RegisterTask([MarshalAs(UnmanagedType.BStr)] string path, [MarshalAs(UnmanagedType.BStr)] string xmlText, int flags, Variant userId, Variant password, int logonType, Variant sddl, out IRegisteredTask ppTask);
    void RegisterTaskDefinition([MarshalAs(UnmanagedType.BStr)] string path, nint pDefinition, int flags, Variant userId, Variant password, int logonType, Variant sddl, out IRegisteredTask ppTask); // unused (placeholder)
    void GetSecurityDescriptor(int securityInformation, [MarshalAs(UnmanagedType.BStr)] out string pSddl);
    void SetSecurityDescriptor([MarshalAs(UnmanagedType.BStr)] string sddl, int flags);
}

/// <summary>A collection of task folders.</summary>
[GeneratedComInterface, Guid("79184A66-8664-423F-97F1-637356A5D812")]
internal partial interface ITaskFolderCollection : IDispatch
{
    int get_Count();
    void get_Item(Variant index, out ITaskFolder ppFolder);
    nint get__NewEnum(); // unused (placeholder, IUnknown)
}

/// <summary>A task registered with the Task Scheduler service.</summary>
[GeneratedComInterface, Guid("9C86F320-DEE3-4DD1-B972-A303F26B061E")]
internal partial interface IRegisteredTask : IDispatch
{
    [return: MarshalAs(UnmanagedType.BStr)] string get_Name();
    [return: MarshalAs(UnmanagedType.BStr)] string get_Path();
    int get_State();
    short get_Enabled(); // VARIANT_BOOL
    void put_Enabled(short enabled); // VARIANT_BOOL
    void Run(Variant @params, out IRunningTask ppRunningTask);
    void RunEx(Variant @params, int flags, int sessionID, [MarshalAs(UnmanagedType.BStr)] string user, out IRunningTask ppRunningTask); // unused (placeholder)
    void GetInstances(int flags, out IRunningTaskCollection ppRunningTasks);
    double get_LastRunTime();
    int get_LastTaskResult();
    int get_NumberOfMissedRuns();
    double get_NextRunTime();
    nint get_Definition(); // unused (placeholder, ITaskDefinition)
    [return: MarshalAs(UnmanagedType.BStr)] string get_Xml();
    void GetSecurityDescriptor(int securityInformation, [MarshalAs(UnmanagedType.BStr)] out string pSddl);
    void SetSecurityDescriptor([MarshalAs(UnmanagedType.BStr)] string sddl, int flags);
    void Stop(int flags);
    void GetRunTimes(nint pstStart, nint pstEnd, nint pCount, nint pRunTimes); // unused (placeholder)
}

/// <summary>A collection of registered tasks.</summary>
[GeneratedComInterface, Guid("86627EB4-42A7-41E4-A4D9-AC33A72F2D52")]
internal partial interface IRegisteredTaskCollection : IDispatch
{
    int get_Count();
    void get_Item(Variant index, out IRegisteredTask ppRegisteredTask);
    nint get__NewEnum(); // unused (placeholder, IUnknown)
}

/// <summary>Provides control and information for a currently running task.</summary>
[GeneratedComInterface, Guid("653758FB-7B9A-4F1E-A471-BEEB8E9B834E")]
internal partial interface IRunningTask : IDispatch
{
    // Entire surface is unused (OneMMC only holds/releases IRunningTask references); placeholders only.
    [return: MarshalAs(UnmanagedType.BStr)] string get_Name();
    [return: MarshalAs(UnmanagedType.BStr)] string get_InstanceGuid();
    [return: MarshalAs(UnmanagedType.BStr)] string get_Path();
    int get_State();
    [return: MarshalAs(UnmanagedType.BStr)] string get_CurrentAction();
    void Stop();
    void Refresh();
    uint get_EnginePID();
}

/// <summary>A collection of running tasks.</summary>
[GeneratedComInterface, Guid("6A67614B-6828-4FEC-AA54-6D52E8F1F2DB")]
internal partial interface IRunningTaskCollection : IDispatch
{
    int get_Count();
    void get_Item(Variant index, out IRunningTask ppRunningTask);
    nint get__NewEnum(); // unused (placeholder, IUnknown)
}
