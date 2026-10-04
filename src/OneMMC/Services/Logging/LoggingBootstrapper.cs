using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using OneMMC.Services.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;
using Windows.Win32;

namespace OneMMC.Services.Logging;

public static class LoggingBootstrapper
{
    public static IServiceProvider BuildServiceProvider()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appFolder = Path.Combine(localAppData, "OneMMC");
        string logsFolder = Path.Combine(appFolder, "Logs");
        Directory.CreateDirectory(logsFolder);

        string logPath = Path.Combine(logsFolder, "OneMMC-.log");

        // Define a consistent output template for both file and debug sinks (for Visual Studio Output Window)
        string outputTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";
        var textFormatter = new MessageTemplateTextFormatter(outputTemplate);

        // First-party Debug logging is always captured, on every build (Release included), so a user
        // can hand over a complete diagnostic log without first flipping a switch. Framework categories
        // (Microsoft/System) stay at Warning so the file keeps to OneMMC rather than platform noise.
        // VerboseLogging is the deeper opt-in: it lifts OneMMC to Verbose (Trace) and lets the framework
        // categories log at Debug.
        bool verbose = IsVerboseLoggingEnabled();
        LogEventLevel appLevel = verbose ? LogEventLevel.Verbose : LogEventLevel.Debug;
        LogEventLevel frameworkLevel = verbose ? LogEventLevel.Debug : LogEventLevel.Warning;

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(appLevel)
            .MinimumLevel.Override("Microsoft", frameworkLevel)
            .MinimumLevel.Override("System", frameworkLevel)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "OneMMC")
            .WriteTo.File(
                path: logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                encoding: Encoding.UTF8,
                // Kept shared rather than switching to buffered (Serilog.Sinks.File allows only one of
                // the two): the "Run as administrator" flow starts an elevated second process that
                // overlaps with this one, and both write this file.
                shared: true)
            // Custom debug sink → Visual Studio Output window / DebugView, bypassing Trace.Listeners
            // so it does not loop back through the Trace bridge.
            .WriteTo.Sink(new DebugOutputSink(textFormatter))
            .CreateLogger();

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(verbose
                ? Microsoft.Extensions.Logging.LogLevel.Trace
                : Microsoft.Extensions.Logging.LogLevel.Debug);
            builder.AddSerilog(Log.Logger, dispose: true);
        });

        services.AddOneMMCApplicationServices();

        // Validation walks the registration table only (no reflection, so it stays AOT-safe) but costs
        // startup time, so it guards development builds and is left off in Release.
        var providerOptions = new ServiceProviderOptions
        {
#if DEBUG
            ValidateScopes = true,
            ValidateOnBuild = true,
#endif
        };

        IServiceProvider serviceProvider = BuildValidatedProvider(services, providerOptions);
        EnableDebugBridge(serviceProvider);

        return serviceProvider;
    }

    /// <summary>
    /// Builds the provider, replacing the framework's unhelpful "Some services are not able to be
    /// constructed" aggregate with a message naming each offending registration.
    /// </summary>
    /// <remarks>
    /// The usual cause is a registration nothing ever resolves, whose type cannot actually be built —
    /// a constructor that needs a non-service argument, or a private constructor because the type is
    /// really a static singleton. Delete the registration rather than working around the validation.
    /// </remarks>
    private static ServiceProvider BuildValidatedProvider(
        IServiceCollection services,
        ServiceProviderOptions options)
    {
        try
        {
            return services.BuildServiceProvider(options);
        }
        catch (AggregateException ex)
        {
            var detail = new StringBuilder("Dependency injection validation failed:");
            foreach (Exception inner in ex.InnerExceptions)
            {
                detail.Append("\n  - ").Append(inner.Message);
            }

            throw new InvalidOperationException(detail.ToString(), ex);
        }
    }

    public static void Shutdown()
    {
        Log.CloseAndFlush();
    }

    /// <summary>
    /// Reads the verbose-logging opt-in. Runs before the logger exists, so failures fall back to the
    /// quieter default rather than surfacing an error.
    /// </summary>
    private static bool IsVerboseLoggingEnabled()
    {
        try
        {
            return Models.AppSettings.Load().VerboseLogging;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Forwards <see cref="Trace"/> output into Serilog, but only while a debugger is attached.
    /// </summary>
    /// <remarks>
    /// The listener turns every Trace/Debug write anywhere in the process — including framework and
    /// XAML/CsWinRT chatter — into a formatted Serilog event on the writing thread, and
    /// <see cref="Trace.AutoFlush"/> forces a flush for each one. That is worth paying while debugging and
    /// pure overhead otherwise; it mirrors the check <c>DebugOutputSink.Emit</c> already makes.
    /// </remarks>
    private static void EnableDebugBridge(IServiceProvider serviceProvider)
    {
        if (!Debugger.IsAttached)
        {
            return;
        }

        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

        Trace.AutoFlush = true;

        Trace.Listeners.Add(new SerilogTraceListener(loggerFactory, "SystemTrace"));
    }

    /// <summary>
    /// A Serilog sink that posts formatted events to the debugger's output stream (the Visual Studio
    /// Output window, or DebugView) without routing through <c>Trace.Listeners</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This mirrors what <c>DefaultTraceListener</c> does internally, but deliberately does not go
    /// through the shared <c>Trace.Listeners</c> collection — routing through it would feed every event
    /// back into Serilog via the Trace bridge and duplicate it. When a debugger is capturing log text
    /// (Visual Studio) <c>Debugger.Log</c> posts to its Output window; otherwise <c>OutputDebugString</c>
    /// reaches native listeners such as DebugView. Neither call is compiled out of Release builds,
    /// unlike <c>Debug.WriteLine</c> (<c>[Conditional("DEBUG")]</c>).
    /// </para>
    /// <para>
    /// Gated on <see cref="Debugger.IsAttached"/>: the durable, submittable record is the file sink,
    /// which already captures everything at Debug level, so this stream only pays its per-line cost
    /// while a debugger is actually watching.
    /// </para>
    /// </remarks>
    private sealed class DebugOutputSink : ILogEventSink
    {
        private readonly ITextFormatter _textFormatter;

        public DebugOutputSink(ITextFormatter textFormatter)
        {
            _textFormatter = textFormatter ?? throw new ArgumentNullException(nameof(textFormatter));
        }

        public void Emit(LogEvent logEvent)
        {
            if (!Debugger.IsAttached)
            {
                return;
            }

            var buffer = new StringWriter();
            _textFormatter.Format(logEvent, buffer);
            string message = buffer.ToString().TrimEnd() + Environment.NewLine;

            if (Debugger.IsLogging())
            {
                // Surfaces in the Visual Studio Output window (Debug pane).
                Debugger.Log(0, null, message);
            }
            else
            {
                // Reaches native listeners such as DebugView when no managed debugger is capturing text.
                PInvoke.OutputDebugString(message);
            }
        }
    }
}

internal sealed partial class SerilogTraceListener : TraceListener
{
    private readonly Microsoft.Extensions.Logging.ILogger _logger;

    [ThreadStatic]
    private static bool _isForwarding;

    public SerilogTraceListener(ILoggerFactory loggerFactory, string categoryName)
    {
        Name = categoryName;
        _logger = loggerFactory.CreateLogger(categoryName);
    }

    public override void Write(string? message)
    {
        LogMessage(message, null);
    }

    public override void WriteLine(string? message)
    {
        LogMessage(message, null);
    }

    public override void Write(string? message, string? category)
    {
        LogMessage(message, category);
    }

    public override void WriteLine(string? message, string? category)
    {
        LogMessage(message, category);
    }

    private void LogMessage(string? message, string? category)
    {
        if (string.IsNullOrWhiteSpace(message) || _isForwarding)
        {
            return;
        }

        try
        {
            _isForwarding = true;

            if (string.IsNullOrWhiteSpace(category))
            {
                _logger.LogDebug("{TraceMessage}", message);
                return;
            }

            _logger.LogDebug("[{Category}] {TraceMessage}", category, message);
        }
        finally
        {
            _isForwarding = false;
        }
    }
}
