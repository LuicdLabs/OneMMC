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

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
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
                shared: true)
            // Add custom Debug Sink that uses OutputDebugString to avoid Trace loop
            .WriteTo.Sink(new DebugOutputSink(textFormatter))
            .CreateLogger();

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug);
            builder.AddSerilog(Log.Logger, dispose: true);
        });

        services.AddOneMMCApplicationServices();

        IServiceProvider serviceProvider = services.BuildServiceProvider();
        EnableDebugBridge(serviceProvider);

        return serviceProvider;
    }

    public static void Shutdown()
    {
        Log.CloseAndFlush();
    }

    private static void EnableDebugBridge(IServiceProvider serviceProvider)
    {
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

internal sealed class SerilogTraceListener : TraceListener
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
