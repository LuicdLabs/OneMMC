# Logging Technical Documentation

This document is the authoritative reference for OneMMC logging. Other documents
(`.github/copilot-instructions.md`, `AGENTS.md`, `.github/CONTRIBUTING.md`, the project READMEs) only
summarize these rules and link here.

## Purpose

OneMMC uses one logging pipeline across UI and Core:

- abstraction: `Microsoft.Extensions.Logging`
- provider: Serilog
- file sink: `%LOCALAPPDATA%/OneMMC/Logs/`
- debug sink: custom `DebugOutputSink`

The goal is predictable diagnostics without feature-specific logging patterns.

## Startup

Logging is bootstrapped in `src/OneMMC/Services/Logging/LoggingBootstrapper.cs`.

Startup flow:

1. Create the Serilog pipeline (file sink + `DebugOutputSink`).
2. Add `Microsoft.Extensions.Logging` to the service collection via `AddSerilog(...)`, at `Debug`
   by default (or `Verbose`/Trace when `AppSettings.VerboseLogging` is set).
3. Register application services through `AddOneMMCApplicationServices()` (UI), which chains to
   `AddOneMMCCore()` (Core) and each feature module.
4. Build the service provider (`ValidateScopes`/`ValidateOnBuild` in Debug only).
5. Enable the Trace → Serilog bridge, but only when a debugger is attached.

## Dependency Injection

- Core services and view models should receive `ILogger<T>` through constructor injection.
- WinUI pages should resolve dependencies with `App.GetRequiredService<T>()`.
- Do not instantiate Core services or view models directly in page code-behind.
- Do not add new parameterless fallback constructors just to get a logger.

## Static Components

Some native helpers still expose `ConfigureLogger(...)` or `SetLogger(...)` when
constructor injection is not practical. Use that pattern only for low-level static
interop helpers.

## Rules

- Use structured logging with named properties.
- Do not add `Debug.WriteLine`, `Console.WriteLine`, or `Trace.WriteLine` directly.
- Prefer `LogDebug`, `LogInformation`, `LogWarning`, `LogError`, and `LogCritical`
  with contextual properties instead of concatenated strings.

## Levels

First-party (OneMMC) logging always runs at **`Debug`** on every build, Release included, so any user
can submit a complete diagnostic log without first turning anything on. Framework categories
(`Microsoft`/`System`) are held at `Warning` so the file keeps to OneMMC rather than platform noise.

- `LogDebug` is captured by default — use it freely for detailed tracing.
- Anything that must always be visible (lifecycle events, operation outcomes) still logs at
  `Information` or above.
- `"VerboseLogging": true` in `%LOCALAPPDATA%/OneMMC/Settings.json` is the deeper opt-in: it raises
  OneMMC to `Verbose` (Trace) and lets `Microsoft`/`System` log at `Debug` for maximum detail.
- The `Trace` → Serilog bridge (`EnableDebugBridge`) is installed only when a debugger is attached; it
  forwards all framework `Trace`/`Debug` output and is pure overhead otherwise.
- `DebugOutputSink` posts formatted events to the debugger — `Debugger.Log` for the Visual Studio
  Output window, falling back to `OutputDebugString` (CsWin32) for DebugView when no managed debugger is
  capturing text. It avoids `Debug.WriteLine`, which is compiled out of Release builds and would loop
  back through the Trace bridge. Gated on an attached debugger; the file sink is the durable record.
- The file sink uses `shared: true` (not `buffered: true` — Serilog.Sinks.File allows only one of the
  two) because the "Run as administrator" flow briefly runs a second process writing the same file.

## Current Architecture Notes

- Core registration is explicit. Logging no longer relies on reflection-based
  auto-registration.
- `OneMMC.Core` now exposes a single public DI entrypoint:
  `AddOneMMCCore(this IServiceCollection services)`.
- Windows-native capability services such as file dialogs and ACL editor integration
  live under `Infrastructure/WindowsCapabilities`.

## Verification

Common checks after logging-related changes:

```powershell
dotnet build src/OneMMC/OneMMC.csproj -p:Platform=x64
rg \"Debug.WriteLine|Console.WriteLine|Trace.WriteLine\" src/OneMMC src/OneMMC.Core
```

Expected result:

- build succeeds
- no direct debug/console/trace writes are introduced
