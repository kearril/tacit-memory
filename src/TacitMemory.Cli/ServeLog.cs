using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using TacitMemory.Core.Logging;

namespace TacitMemory.Cli;

/// <summary>
/// The log of <c>tacit serve</c>, in the files and shape <see cref="ServiceLogFiles"/> defines, the newest 14 kept. It holds
/// Information and above, so what the command prints and the service's events; ASP.NET Core and the MCP SDK write only
/// their warnings. No other command opens it.
/// </summary>
internal sealed class ServeLog : IDisposable
{
    private readonly Serilog.Core.Logger _logger;

    public ServeLog(string tacitHome)
    {
        _logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                ServiceLogFiles.RollingPathOf(tacitHome),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: ServiceLogFiles.RetainedDays,
                // Shared, so a second serve that fails on the port can still write its own line.
                shared: true,
                outputTemplate: ServiceLogFiles.OutputTemplate)
            .CreateLogger();
        Factory = new SerilogLoggerFactory(_logger);
        Provider = new SerilogLoggerProvider(_logger);
        Output = Factory.CreateLogger("TacitMemory.Serve");
    }

    /// <summary>The factory the memory service logs its events through.</summary>
    public ILoggerFactory Factory { get; }

    /// <summary>The provider ASP.NET Core and the MCP SDK log through.</summary>
    public ILoggerProvider Provider { get; }

    /// <summary>The logger of the lines the serve command prints.</summary>
    public Microsoft.Extensions.Logging.ILogger Output { get; }

    public void Dispose() => _logger.Dispose();
}
