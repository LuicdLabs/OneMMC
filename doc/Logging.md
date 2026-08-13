# Logging Technical Documentation

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

1. Create the Serilog pipeline.
2. Add `Microsoft.Extensions.Logging` to the service collection.
3. Register Core services through `AddOneMMCCore(...)`.
4. Register UI services and view models.
5. Build the service provider.
6. Enable the Trace bridge for legacy trace listeners.

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

The default minimum level is **`Information`**. `Debug` is a local diagnostic mode, not a normal
operating level: every `LogDebug` call formats a message template and allocates property values even
when nobody reads the output, which shows up as sustained gen0 pressure (see `doc/MemoryManagement.md`).

- `LogDebug` is still the right call for detailed tracing — just do not expect it in a default session.
- To turn it on, set `"VerboseLogging": true` in `%LOCALAPPDATA%/OneMMC/Settings.json`.
- Anything that must always be visible (lifecycle events and operation outcomes) logs at
  `Information` or above.
- The `Trace` → Serilog bridge (`EnableDebugBridge`) is installed only when a debugger is attached; it
  forwards all framework `Trace`/`Debug` output and is pure overhead otherwise.
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
