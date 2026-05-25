using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ManagementTools.Services.Logging;

public static class UiLogger
{
    private static ILogger _logger = NullLogger.Instance;

    public static void Configure(ILoggerFactory loggerFactory)
    {
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger("ManagementTools.UI");
    }

    public static void LogDebug(string message)
    {
        _logger.LogDebug(message);
    }
}
