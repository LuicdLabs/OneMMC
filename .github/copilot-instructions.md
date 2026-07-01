# Copilot Instructions

## General Guidelines
- **Naming Conventions**: Strictly follow the official [C# Coding Conventions](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions) and [.NET Runtime Coding Guidelines](https://github.com/dotnet/runtime/blob/main/docs/coding-guidelines/coding-style.md).
    - Use **PascalCase** for public members, types, and namespaces.
    - Use **_camelCase** for private fields (preferably with `_` prefix).
    - Avoid this prefix `I` for non-interface classes.
- **Logging**: Follow the unified logging architecture documented in `doc/Logging.md`. Key points:
    - Always use `Microsoft.Extensions.Logging` (`ILogger`/`ILogger<T>`).
    - **Strictly prohibit** `Debug.WriteLine`, `Console.WriteLine`, or `Trace.WriteLine`.
    - Services/ViewModels: Constructor injection with `ILogger<T>`.
    - Page classes: Use `App.GetRequiredService<T>()` to obtain instances (never `new`).
    - Static/Factory: Provide `ConfigureLogger(...)` or `SetLogger(...)` methods.
- **Microsoft Guidance**: Adhere to the latest implementation guidance provided by Microsoft Learn for WinUI 3 and Windows App SDK.
- **Native Implementation**: 
    - Use native WinUI 3 / Windows App SDK APIs (e.g., `Windows.Storage`, `WinRT interop`) whenever possible.
    - **Avoid** using `System.Diagnostics.Process` or `ProcessStartInfo` to shell out to command-line tools unless absolutely necessary for a missing API. Prefer P/Invoke or existing WinRT wrappers.
- **Configuration Management**: 
    - Avoid hardcoding values (strings, magic numbers, colors).
    - Use `Constants` classes for compile-time constants.
- **Native Interop**:
    - Treat **CsWin32 as the default** for Win32 interop. Add supported APIs to the project-level `NativeMethods.txt` and call generated `Windows.Win32.PInvoke` members instead of creating new handwritten imports.
    - Only keep handwritten `[DllImport]` / `[LibraryImport]` for explicit exceptions such as unsupported exports, APIs that CsWin32 cannot emit for the active target configuration or architecture, unavoidable BCL/COM marshalling gaps, or mixed native workflows where a partial conversion would introduce a second unsafe marshalling model.
    - For one-off missing exports, prefer `NativeLibrary` + delegate binding over a new static import.
    - Any handwritten interop that remains must be centralized in a native wrapper/helper file and documented with the reason CsWin32 could not be used directly.

## Architecture Boundaries
- **Core may reference Windows App SDK platform APIs**: `OneMMC.Core` may reference `Microsoft.WindowsAppSDK` and `Microsoft.UI.*` only for reusable Windows-native services such as file/folder pickers, native OS dialogs, interop helpers, and image conversion helpers. Dependency still flows one way: UI → Core only.
- **ViewModel must not touch UI elements**: ViewModels in Core must not create or manipulate `ContentDialog`, `FrameworkElement`, `XamlRoot`, `DispatcherQueue`, `ElementTheme`, pages, windows, controls, or any presentation state. Expose state via observable properties; let the View decide how to present it.
- **Features must not cross-reference each other**: A Feature (e.g. `PCManagement`) must not directly reference types from another Feature (e.g. `SystemManagement`). Share only through `Abstractions`.
- **No direct `new` on Infrastructure classes from Features**: Features must depend on `Abstractions` interfaces only. Infrastructure implementations (e.g. `AdminService`) are resolved via DI — never instantiated directly with `new`.
- **No hardcoded user-facing strings in ViewModels or Views**: All user-visible strings must come from resource keys defined in `Core/Localization/ResourceKeys.cs` and loaded via `ILocalizationProvider`. Never inline string literals that will be shown to the user.
- **Async relay commands must return `Task`**: Methods decorated with `[RelayCommand]` that are async must return `Task`, not use `async void`. See [MVVMTK0039](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/generators/errors/mvvmtk0039).
- **New functionality placement**: New features go under `Core/Features/<FeatureName>/` (Models, Services, ViewModels) with a corresponding `<FeatureName>Module.cs` for DI registration, and Views under `Views/<FeatureName>/` in the UI project.
- **UI-specific code stays in UI**: App window ownership, XAML dialogs/pages, theme mapping (`WinUIThemeService`), visual presentation, and UI composition belong in the UI project. Reusable Windows-native platform services may live in Core when they keep HWND ownership at the call boundary and do not own XAML presentation.

## WinUI 3 Specifics
- **Architecture**: Follow the **MVVM (Model-View-ViewModel)** pattern. Keep code-behind (`.xaml.cs`) minimal and avoid putting UI assembly logic in code-behind; prefer using `DataTemplate` and data binding.
- **WPF vs WinUI 3**: 
    - **DO NOT** apply WPF patterns, APIs, or techniques to WinUI 3. They are fundamentally different frameworks.
    - WPF APIs like `SetResourceReference`, `DependencyProperty.Register`, `RoutedCommand`, `ICommand` from `System.Windows.Input`, etc. **do not exist** in WinUI 3.
    - WinUI 3 uses `Microsoft.UI.Xaml` namespace, not `System.Windows`.
    - Threading model differs: WinUI 3 uses `DispatcherQueue`, not WPF's `Dispatcher`.
    - Resource system differs: WinUI 3 uses `{ThemeResource}` and `{StaticResource}`, but dynamic resource updates work differently than WPF.
- **UWP vs WinUI 3**:
    - **DO NOT** assume UWP APIs or patterns work in WinUI 3 without verification.
    - WinUI 3 is **desktop-only** (Win32 app model), not sandboxed like UWP. No app container restrictions.
    - Namespace migration: `Windows.UI.Xaml` (UWP) → `Microsoft.UI.Xaml` (WinUI 3).
    - Some UWP APIs moved or changed: `ApplicationData.Current` still works, but prefer Windows App SDK equivalents.
    - WinUI 3 supports full Win32 APIs, P/Invoke, and COM interop without restrictions (unlike UWP).
    - Packaging: WinUI 3 supports both packaged (MSIX) and unpackaged deployment; UWP is always packaged.
    - Window management: WinUI 3 uses `Microsoft.UI.Windowing.AppWindow` for advanced scenarios, not UWP's `ApplicationView`.
- **UI Threading**: 
    - Ensure UI updates happen on the UI thread. Use `DispatcherQueue.TryEnqueue` for marshaling calls.
    - Avoid blocking UI threads with `.Result` or `.Wait()` on async methods; use `await` correctly.
- **Asynchronous Programming**: Use `async`/`await` consistently. Avoid legacy patterns like `IAsyncResult`.
- **Resource Management**: Use `x:Uid` for localization and define styles in `ResourceDictionary` files rather than inline styles.
- **ThemeResource in Code-Behind**: When dynamically creating UI elements in code-behind that need theme-aware brushes, define a named `Style` with `{ThemeResource ...}` in the page's XAML `ResourceDictionary` and apply it via `Style = (Style)Resources["StyleKey"]` in code-behind. Never use `Application.Current.Resources["ResourceKey"]` directly — it is a one-time static fetch that will not update when the user switches between Light and Dark mode. Note: `SetResourceReference` is a WPF API and does **not** exist in WinUI 3.
- **Navigation**: Use `SelectorBar` instead of `Pivot` in the UI wherever tab-like navigation is needed.

## Administrator Permission Handling
- **Unified System**: Always use the unified administrator detection system documented in `doc/AdminDetectionSystem.md`.
- **Three Patterns**:
  - **Pattern 1 (Pre-flight)**: Check `IAdminService.IsRunningAsAdmin` before operations that always require admin rights.
  - **Pattern 2 (Event-driven)**: Catch permission errors in ViewModel, trigger `AdminPermissionRequired` event, handle in View.
  - **Pattern 3 (OperationResult)**: Use `OperationResult.AccessDenied()` for disk management operations.
- **UI Consistency**: Always use `AdminDialogHelper` for admin-related dialogs and InfoBars. Never create custom admin permission dialogs.
- **Localization**: Use `LocalizationProvider.Current.GetString()` with `ResourceKeys` constants for all admin-related messages.
- **Error Detection**: Use `IAdminService.IsPermissionError(ex)` to identify permission-related exceptions.

## Code Style & Formatting
- **Null Checking**: Use pattern matching for null checks (e.g., `if (obj is null)` or `if (obj is not null)`).
- **String Interpolation**: Prefer string interpolation (`$""`) over `String.Format` or concatenation for readability.
- **Implicit Usings**: Assume `ImplicitUsings` is enabled. Do not add redundant namespace imports for common namespaces like `System`, `System.Collections.Generic`, etc.

## API Usage
- **Do Not Use Non-Existent APIs**: Never suggest or use APIs that do not exist in the target framework or SDK. If you are unsure whether an API exists, say so explicitly rather than guessing or fabricating method/property names.

## Known WinUI 3 Pitfalls
- **Layout Panel IsEnabled**: `Grid`, `StackPanel`, `Border`, and other layout panels do **not** have an `IsEnabled` property in WinUI 3 — they do not inherit from `Control`. Placing `IsEnabled` on them in XAML causes `WMC0011` compiler errors. The correct pattern is: omit `IsEnabled` from the panel in XAML entirely, then call a helper method (e.g. `SetMode(bool enabled)`) from the constructor and from event handlers to iterate children and set `IsEnabled` on each `Control` individually.
- **ContentDialog `await ShowAsync()`**: `ContentDialog.ShowAsync()` returns `IAsyncOperation<ContentDialogResult>`. If you see `CS4036 'IAsyncOperation<T>' does not contain a definition for 'GetAwaiter'`, add `using System;` explicitly to the file. Alternatively use `.AsTask()` to convert to a standard `Task<T>`.

## Shell & Terminal Commands
- **Always use PowerShell** and prioritize native cmdlets (e.g., `Get-Content` instead of `cat`, `Select-String` instead of `grep`/`rg`, `$env:VAR = "value"` instead of `export`); `;` can separate commands but is not equivalent to `&&`. Cross-platform CLI tools such as `dotnet` are still acceptable.

## Comments and Maintenance
- **XML Documentation**: Use XML documentation comments (`///`) for all public APIs, classes, methods, and properties to enable IntelliSense.
- **Clarity**: Write clear and concise comments to explain complex logic, algorithms, or non-obvious code behavior.
- **Maintenance**: Keep comments up-to-date when modifying code; outdated comments are worse than no comments.
- **Avoid Redundancy**: Do not write comments that simply restate what the code does (e.g., `// Set x to 5` for `x = 5;`).
- **TODO Comments**: Use TODO comments sparingly and include context about what needs to be done and why.
- **Workarounds**: Document any workarounds, hacks, or non-standard implementations with clear explanations of why they exist.

# CLAUDE.md

Behavioral guidelines to reduce common LLM coding mistakes. Merge with project-specific instructions as needed.

**Tradeoff:** These guidelines bias toward caution over speed. For trivial tasks, use judgment.

## 1. Think Before Coding

**Don't assume. Don't hide confusion. Surface tradeoffs.**

Before implementing:
- State your assumptions explicitly. If uncertain, ask.
- If multiple interpretations exist, present them - don't pick silently.
- If a simpler approach exists, say so. Push back when warranted.
- If something is unclear, stop. Name what's confusing. Ask.

## 2. Simplicity First

**Minimum code that solves the problem. Nothing speculative.**

- No features beyond what was asked.
- No abstractions for single-use code.
- No "flexibility" or "configurability" that wasn't requested.
- No error handling for impossible scenarios.
- If you write 200 lines and it could be 50, rewrite it.

Ask yourself: "Would a senior engineer say this is overcomplicated?" If yes, simplify.

## 3. Surgical Changes

**Touch only what you must. Clean up only your own mess.**

When editing existing code:
- Don't "improve" adjacent code, comments, or formatting.
- Don't refactor things that aren't broken.
- Match existing style, even if you'd do it differently.
- If you notice unrelated dead code, mention it - don't delete it.

When your changes create orphans:
- Remove imports/variables/functions that YOUR changes made unused.
- Don't remove pre-existing dead code unless asked.

The test: Every changed line should trace directly to the user's request.

## 4. Goal-Driven Execution

**Define success criteria. Loop until verified.**

Transform tasks into verifiable goals:
- "Add validation" → "Write tests for invalid inputs, then make them pass"
- "Fix the bug" → "Write a test that reproduces it, then make it pass"
- "Refactor X" → "Ensure tests pass before and after"

For multi-step tasks, state a brief plan:
```
1. [Step] → verify: [check]
2. [Step] → verify: [check]
3. [Step] → verify: [check]
```

Strong success criteria let you loop independently. Weak criteria ("make it work") require constant clarification.

---

**These guidelines are working if:** fewer unnecessary changes in diffs, fewer rewrites due to overcomplication, and clarifying questions come before implementation rather than after mistakes.