# Unified Administrator Detection System

## Overview

OneMMC uses a **Unified Administrator Detection System** to ensure that all features requiring elevated privileges maintain consistent **UI behavior** and **user experience**. This document describes the overall architecture of this system, integration with localization, and the implementation patterns developers should follow when adding features that require administrator privileges.

---

## Architecture

```

┌─────────────────────────────────────┐
│         UI Layer (WinUI 3)          │
│                                     │
│  ┌─────────────────────────────┐    │
│  │    AdminDialogHelper        │    │  Static helper class for all admin UI
│  │  ┌────────────────────────┐ │    │
│  │  │ ShowAdminRequired      │ │    │  Info-only dialog (OK only)
│  │  │ DialogAsync()          │ │    │
│  │  ├────────────────────────┤ │    │
│  │  │ RunasAdmin()           │ │    │  Restarts elevated; returns bool
│  │  ├────────────────────────┤ │    │
│  │  │ ConfigureAdminInfoBar  │ │    │  Persistent InfoBar warning
│  │  └────────────────────────┘ │    │
│  └─────────────────────────────┘    │
│         ▲                           │
│         │ View subscribes to
│         │ ViewModel events
├─────────┼───────────────────────────┤
│         │  Core Layer               │
│  ┌──────┴──────────────────────┐    │
│  │  ViewModel                  │    │
│  │  event AdminPermission      │    │  Fired when permission error
│  │  Required                   │    │  is caught during execution
│  └─────────────────────────────┘    │
│                                     │
│  ┌─────────────────────────────┐    │
│  │  IAdminService              │    │  Singleton service
│  │  ┌────────────────────────┐ │    │
│  │  │ IsRunningAsAdmin       │ │    │  Cached permission check
│  │  ├────────────────────────┤ │    │
│  │  │ IsPermissionError(ex)  │ │    │  Exception analysis
│  │  ├────────────────────────┤ │    │
│  │  │ RestartAsAdmin()       │ │    │  Triggers UAC elevation and restart
│  │  └────────────────────────┘ │    │
│  └─────────────────────────────┘    │
│                                     │
│  ┌─────────────────────────────┐    │
│  │  LocalizationProvider       │    │  Admin-related
│  │  ResourceKeys (CommonKeys,  │    │  localized strings
│  │  PolicyKeys, TPMKeys, etc.) │    │
│  └─────────────────────────────┘    │
└─────────────────────────────────────┘

````

---

## Key Components

### `IAdminService` / `AdminService`

**Location:** `OneMMC.Core/Abstractions/Services/IAdminService.cs`, `OneMMC.Core/Infrastructure/Admin/AdminService.cs`  
**Lifecycle:** Singleton (because administrator status does not change within the same process)

| Member | Description |
|---|---|
| `IsRunningAsAdmin` | `bool` — Checks `WindowsPrincipal.IsInRole(WindowsBuiltInRole.Administrator)` once and caches the result |
| `IsPermissionError(Exception ex)` | Checks exception types (`UnauthorizedAccessException`, `Win32Exception` error code 5) and error message patterns ("Access denied", "Insufficient priv", etc.), recursively analyzing InnerException |
| `RestartAsAdmin()` | Starts a new process with `Verb = "runas"`, then raises the `RestartRequested` (`Action?`) event so the UI can exit the current process |

---

### `AdminDialogHelper`

**Location:** `OneMMC/Helpers/AdminDialogHelper.cs`  
**Type:** Static class with `_isDialogOpen` guard to prevent multiple dialogs from opening simultaneously

| Method | Purpose |
|---|---|
| `ShowAdminRequiredDialogAsync(XamlRoot)` | **Info-only** dialog (OK only). Used when operation cannot continue |
| `RunasAdmin()` | Restarts the application elevated by calling `IAdminService.RestartAsAdmin()`. Synchronous; returns `bool` (`true` if the restart was initiated) |
| `ConfigureAdminInfoBar(InfoBar, string?)` | Configures `InfoBar` with standard "Administrator Required" warning style |

All methods use `Common_AdminRequired_*` localization string keys.

---

### `OperationResult.IsAccessDenied`

**Location:** `OneMMC.Core/Features/PCManagement/Services/DiskMgmt/Common/OperationResult.cs`  
**Purpose:** Disk management operations return `OperationResult`; when permissions are insufficient, `IsAccessDenied = true` is set. The UI layer checks this flag and displays `AdminDialogHelper`.

| Factory | Description |
|---|---|
| `OperationResult.AccessDenied(operationName)` | Creates a failed result with `IsAccessDenied = true` and a localized message |
| `QueryResult<T>.AccessDenied(defaultValue, operationName)` | Generic query version |

---

## Implementation Patterns

For scenarios requiring administrator privileges, there are **three patterns** — choose based on your situation.

---

### Pattern 1: Pre-flight Admin Check

Use when the operation **always** requires administrator privileges and this can be determined before execution.

```csharp
// Page code-behind
private async Task PerformAdminOperationAsync()
{
    var adminService = App.GetRequiredService<IAdminService>();
    if (!adminService.IsRunningAsAdmin)
    {
        await AdminDialogHelper.ShowAdminRequiredDialogAsync(this.XamlRoot);
        return;
    }

    // Continue with operation...
    await ViewModel.DoSomethingAsync();
}
```

**Used in:**
`AccountPoliciesPage`, `LocalPoliciesPage` (before opening policy editor),
`DeviceManagerPage`, `LusrMgrPage`, `TPMPage`, `DiskManagementPage`

---

### Pattern 2: Event-driven (ViewModel → View)

Use when the operation **might succeed** without administrator privileges but **may fail** due to insufficient permissions during execution.

**ViewModel** — Catches permission errors and fires event:

```csharp
public class MyFeatureViewModel : ObservableObject
{
    public event EventHandler? AdminPermissionRequired;

    public async Task SaveAsync()
    {
        try
        {
            await _service.SaveAsync();
        }
        catch (UnauthorizedAccessException)
        {
            AdminPermissionRequired?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (adminService.IsPermissionError(ex))
        {
            AdminPermissionRequired?.Invoke(this, EventArgs.Empty);
        }
    }
}
```

**View** — Subscribes to event in constructor and shows dialog:

```csharp
public MyFeaturePage()
{
    ViewModel = App.GetRequiredService<MyFeatureViewModel>();
    InitializeComponent();
    DataContext = ViewModel;

    ViewModel.AdminPermissionRequired += async (_, _) =>
    {
        await AdminDialogHelper.ShowAdminRequiredDialogAsync(this.XamlRoot);
    };
}
```

**Used in:**
`PerformanceMonitorPage`, `ServicesPage`, `GroupPolicyEditorPage`,
`AccountPoliciesPage`, `LocalPoliciesPage`

---

### Pattern 3: OperationResult Flag Check

Specifically designed for disk management (WMI) operations.

```csharp
var result = await ViewModel.FormatVolumeAsync(driveLetter, fileSystem, label);
if (result.IsAccessDenied)
{
    await AdminDialogHelper.ShowAdminRequiredDialogAsync(this.XamlRoot);
    return;
}
```

---

## Localization

### Core Layer (ViewModel / Services)

Use `LocalizationProvider.Current.GetString()` with constants from `ResourceKeys.cs`:

```csharp
using OneMMC.Core.Localization;

// Generic access denied message
var msg = LocalizationProvider.Current.GetString(
    ResourceFileNames.Common, CommonKeys.AccessDenied_Generic);

// Policy-related access denied
var msg = LocalizationProvider.Current.GetString(
    ResourceFileNames.Policy, PolicyKeys.AccessDenied_Machine);

// TPM access denied
var msg = LocalizationProvider.Current.GetString(
    ResourceFileNames.TPM, TPMKeys.AccessDenied);

// Disk management (with {0} operation name)
var msg = string.Format(
    LocalizationProvider.Current.GetString(
        ResourceFileNames.DiskManagement, DiskMgmtKeys.AccessDenied_Operation),
    operationName);
```

---

### UI Layer (Pages / Dialogs)

**Always use `AdminDialogHelper`** — it internally reads `Common_AdminRequired_*` via `LocalizedStrings`.  
**Do not create custom admin dialogs.**

---

### Resource Key Reference

| Category | Key Constant | Resource File | Description |
|-------------- | ---------------------------- | -------------- | ------------------------ |
| `CommonKeys` | `AdminRequired_Title` | Common | Dialog title: "Administrator Required" |
| `CommonKeys` | `AdminRequired_Message` | Common | Dialog content: "This operation requires administrator privileges..." |
| `CommonKeys` | `AccessDenied_Generic` | Common | Generic message: "Insufficient permissions. Please run as administrator." |
| `PolicyKeys` | `AccessDenied_Title` | Policy | Policy-specific title |
| `PolicyKeys` | `AccessDenied_Machine` | Policy | Computer policy access denied |
| `PolicyKeys` | `AccessDenied_User` | Policy | User policy access denied |
| `TPMKeys` | `AccessDenied` | TPM | TPM access denied |
| `TPMKeys` | `WmiAccessDenied` | TPM | TPM WMI access denied |
| `DiskMgmtKeys` | `AccessDenied_Operation` | DiskManagement | "{0}: Access denied..." format string |
| `DiskMgmtKeys` | `AccessDenied_AdminRequired` | DiskManagement | "Access denied (administrator required)" |
| `AzManKeys` | `AccessDenied` | AzMan | AzMan COM access denied |

---

## Adding Admin Checks to New Features

### Checklist

1. **Define permission boundaries**: Does this operation always require administrator privileges?
2. **Choose a pattern**:

   * Always required → Pattern 1
   * Might not be required → Pattern 2
3. **Add localization strings** (if service layer has custom error messages)

   * Add both `en-US` and `zh-TW` `.resw` files
   * Add constants in `ResourceKeys.cs`
   * Use `LocalizationProvider.Current.GetString()` in Core layer
4. **No custom admin dialogs** — must use `AdminDialogHelper`
5. **Use `IAdminService.IsPermissionError(ex)`** to determine if an unknown exception is a permission issue
6. **Use `OperationResult.AccessDenied()`** (if using OperationResult pattern)

---

### Step-by-step Example

**Scenario:** Adding a "Firewall Rules" feature.

**1. Service layer**

```csharp
// Option A: Throw exception (event-driven)
public async Task SaveRuleAsync(FirewallRule rule)
{
    try
    {
        // ... WMI / COM operations
    }
    catch (UnauthorizedAccessException)
    {
        throw;
    }
}

// Option B: Return OperationResult
public OperationResult ApplyRule(FirewallRule rule)
{
    try { /* ... */ }
    catch (UnauthorizedAccessException)
    {
        return OperationResult.AccessDenied(nameof(ApplyRule));
    }
}
```

**2. ViewModel**

```csharp
public event EventHandler? AdminPermissionRequired;

[RelayCommand]
private async Task SaveRuleAsync()
{
    try
    {
        await _firewallService.SaveRuleAsync(SelectedRule);
    }
    catch (UnauthorizedAccessException)
    {
        AdminPermissionRequired?.Invoke(this, EventArgs.Empty);
        ErrorMessage = LocalizationProvider.Current.GetString(
            ResourceFileNames.Common, CommonKeys.AccessDenied_Generic);
    }
}
```

**3. Page**

```csharp
public FirewallRulesPage()
{
    ViewModel = App.GetRequiredService<FirewallRulesViewModel>();
    InitializeComponent();
    DataContext = ViewModel;

    ViewModel.AdminPermissionRequired += async (_, _) =>
    {
        await AdminDialogHelper.ShowAdminRequiredDialogAsync(this.XamlRoot);
    };
}
```

**4. Resource files**

If needed, add feature-specific strings; otherwise, you can directly use `CommonKeys.AccessDenied_Generic`.

---

## Common Mistakes

| Mistake | Correct Approach |
|---------------------------------------- | ----------------------------------------------------- |
| Creating custom ContentDialog | Use `AdminDialogHelper.ShowAdminRequiredDialogAsync()` |
| Hardcoding English error messages in Service | Use `LocalizationProvider.Current.GetString()` |
| Using `Debug.WriteLine` for permission errors | Use `ILogger` |
| Catching `UnauthorizedAccessException` without notifying user | Fire event or show `AdminDialogHelper` |
| Re-checking if admin in Service | Inject `IAdminService` via DI |
| Forgetting zh-TW translations | en-US and zh-TW must be synchronized |
| Using `OperationResult.Fail()` for access denied | Use `OperationResult.AccessDenied()` |
| View subscribes to event but ViewModel doesn't fire | Ensure all permission errors invoke the event |

---

## Feature Coverage Matrix

| Feature | Pre-flight Check | Event Subscription | OperationResult | InfoBar |
| --------- | ---- | ---- | --------------- | ------- |
| Device Manager | ✅ | — | — | ✅ |
| Disk Management | ✅ | — | ✅ | ✅ |
| Group Policy Editor | — | ✅ | — | ✅ |
| Local Users & Groups | ✅ | — | — | ✅ |
| Performance Monitor | ✅ | ✅ | — | ✅ |
| Security Policy (Account) | ✅ | ✅ | — | — |
| Security Policy (Local) | ✅ | ✅ | — | — |
| Services | — | ✅ | — | ✅ |
| TPM Management | ✅ | — | — | ✅ |
| Authorization Manager | — | — | — | — |

---

## File Reference

| File | Layer | Purpose |
|------------------------------------------- | ---- | --------------------- |
| `Helpers/AdminDialogHelper.cs` | UI | Unified dialog / InfoBar |
| `Core/Abstractions/Services/IAdminService.cs` | Core | Admin privilege interface |
| `Core/Infrastructure/Admin/AdminService.cs` | Core | Singleton implementation |
| `Core/Localization/ResourceKeys.cs` | Core | All localization key constants |
| `Core/Localization/LocalizationProvider.cs` | Core | `Current.GetString()` |
| `Strings/en-US/Common.resw` | UI | English admin strings |
| `Strings/zh-TW/Common.resw` | UI | Traditional Chinese admin strings |