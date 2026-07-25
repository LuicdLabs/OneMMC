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

        // Debug level is a local diagnostic mode, not the normal operating level: every LogDebug call
        // formats a template and allocates property values, which is sustained gen0 pressure for output
        // nobody reads in production. Opt in per machine via AppSettings.VerboseLogging.
        bool verbose = IsVerboseLoggingEnabled();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(verbose ? LogEventLevel.Debug : LogEventLevel.Information)
            // Setting filters as requested: Microsoft/System to Warning
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "OneMMC")
            .WriteTo.File(
                path: logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                encoding: Encoding.UTF8,
                // Kept shared rather than switching to buffered (Serilog.Sinks.File allows only one of
                // the two): the "Run as administrator" flow starts an elevated second process that
                // overlaps with this one, and both write this file. Dropping the level to Information
                // already removes the bulk of the write volume.
                shared: true)
            // Add custom Debug Sink that uses OutputDebugString to avoid Trace loop
            .WriteTo.Sink(new DebugOutputSink(textFormatter))
            .CreateLogger();

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(verbose
                ? Microsoft.Extensions.Logging.LogLevel.Debug
                : Microsoft.Extensions.Logging.LogLevel.Information);
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
    /// A custom Serilog sink that writes directly to the attached debugger.
    /// This bypasses common .NET Trace listeners to avoid infinite loops when
    /// we are also capturing Trace output into Serilog.
    /// </summary>
    private class DebugOutputSink : ILogEventSink
    {
        private readonly ITextFormatter _textFormatter;

        public DebugOutputSink(ITextFormatter textFormatter)
        {
            _textFormatter = textFormatter ?? throw new ArgumentNullException(nameof(textFormatter));
        }

        public void Emit(LogEvent logEvent)
        {
            if (!Debugger.IsAttached) return;

            var buffer = new StringWriter();
            _textFormatter.Format(logEvent, buffer);
            
            // Use Debug.WriteLine instead of Debugger.Log for better Visual Studio Output window support
            Debug.WriteLine(buffer.ToString().TrimEnd());
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
