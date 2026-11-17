using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using TacitMemory.Core;
using TacitMemory.Core.Tests;
using TacitMemory.Host;
using TacitMemory.Host.Api;

namespace TacitMemory.Cli.Tests;

/// <summary>
/// The CLI seam: runs <see cref="CommandLine.RunAsync"/> in process with a throwaway home, captured stdout and stderr,
/// a scripted stdin, the three terminal flags and the NO_COLOR setting. Every test class in this workstream joins <see cref="CliCollection"/>,
/// because the framework keeps its help and error writers in static state.
/// </summary>
internal sealed class CliHarness : IDisposable
{
    /// <summary>A config that does not load: the embedding model still uses the removed <c>endpoint</c> key.</summary>
    private const string BrokenConfig = "models:\n  embedding: { endpoint: 'http://127.0.0.1:38417/v1' }\n";

    private readonly CapturedText _stdout = new();
    private readonly CapturedText _stderr = new();
    private ServiceHost? _host;
    private MemoryService? _service;
    private WebApplication? _foreign;

    public CliHarness() => Home = new TestHome();

    public TestHome Home { get; }

    /// <summary>The port the test config asks for. Picked free, so nothing else on the machine is answering it.</summary>
    public int Port { get; } = FreePort();

    /// <summary>What the last run wrote to stdout so far; readable while a run is still going.</summary>
    public string Stdout => _stdout.Text;

    /// <summary>What the last run wrote to stderr so far.</summary>
    public string Stderr => _stderr.Text;

    public string ConfigPath => Path.Combine(Home.TacitHome, "config.yaml");

    /// <summary>
    /// Runs <c>init</c> and writes a config that loads, listening on <see cref="Port"/>. The models are <paramref name="models"/>, or
    /// <see cref="ModelDouble.Quiet"/> when none is given.
    /// </summary>
    public void InitReady(ModelDouble? models = null) => Home.InitReady(extraConfig: ServerSection, models: models);

    /// <summary>
    /// Sets the access password in the state file, as the service's own API would from this machine. A config whose
    /// <c>server.host</c> is not loopback needs one before the service starts.
    /// </summary>
    public void SetAccessPassword() =>
        Home.Tacit().OpenAccess(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance).SetPassword("correct horse", current: null, local: true, System.Net.IPAddress.Loopback);

    /// <summary>
    /// The <c>connect</c> arguments for the omp paths under the throwaway user home. The folders a user would already have
    /// are created first, since <c>connect</c> refuses a skills folder that is missing.
    /// </summary>
    public string[] OmpConnectArgs()
    {
        Directory.CreateDirectory(Path.Combine(Home.UserHome, ".omp", "agent", "skills"));
        return ["connect", "--client", "omp", "--context", Path.Combine(Home.UserHome, ".omp", "agent", "AGENTS.md"), "--skills", Path.Combine(Home.UserHome, ".omp", "agent", "skills")];
    }

    /// <summary>Replaces the config with one that does not load: the embedding endpoint is not a URL.</summary>
    public void BreakConfig() => File.WriteAllText(ConfigPath, Language + BrokenConfig + ServerSection);

    /// <summary>Replaces the config with one whose decision model has no key, so it does not load.</summary>
    public void RemoveDecisionKey() =>
        File.WriteAllText(ConfigPath, (Language + ModelDouble.Quiet.ModelsYaml(decisionKey: string.Empty) + ServerSection).ReplaceLineEndings("\n"));

    /// <summary>Replaces the config with one that has no <c>models</c> section at all: every model is unconfigured.</summary>
    public void RemoveModels() => File.WriteAllText(ConfigPath, Language + ServerSection);

    private string ServerSection => $"server:\n  port: {Port}\n";

    /// <summary>The language line of a config that replaces the whole file: Chinese, as <see cref="InitReady"/> writes.</summary>
    private const string Language = "language: zh\n";

    /// <summary>
    /// Runs one command line in process. Stdout and stderr are cleared first and returned as they were at the end. The
    /// stdin text is read by commands that ask a question; the terminal flags say what each stream is connected to, and
    /// noColor says the environment set NO_COLOR.
    /// </summary>
    public async Task<CliResult> RunAsync(
        string[] args,
        string stdin = "",
        bool stdinIsTerminal = false,
        bool stdoutIsTerminal = false,
        bool stderrIsTerminal = false,
        bool noColor = false,
        CancellationToken cancellationToken = default)
    {
        _stdout.Clear();
        _stderr.Clear();
        var console = new CliConsole(_stdout, _stderr, new StringReader(stdin), stdoutIsTerminal, stderrIsTerminal, stdinIsTerminal, noColor);
        var exitCode = await CommandLine.RunAsync(args, Home.Options(), console, cancellationToken);
        return new CliResult(exitCode, _stdout.Text, _stderr.Text);
    }

    /// <summary>
    /// Starts the memory service in process on <see cref="Port"/>, the way <c>tacit serve</c> does: the same service, the
    /// same management API and MCP host.
    /// </summary>
    public async Task StartServiceAsync()
    {
        _service = Home.Tacit().OpenService(_ => { }, Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        _host = await ServiceHost.TryStartAsync(_service, Port, Microsoft.Extensions.Logging.Abstractions.NullLoggerProvider.Instance)
            ?? throw new InvalidOperationException($"Port {Port} is already in use.");
    }

    /// <summary>Answers on <see cref="Port"/> with a plain HTTP reply that is not the management API.</summary>
    public Task StartForeignListenerAsync() => ListenAsync(() => Results.Text("hello from another program"));

    /// <summary>Answers on <see cref="Port"/> the way a running service refuses a request it cannot serve: status 503 with an error body.</summary>
    public Task StartRefusingServiceAsync(TacitError error, string message) =>
        ListenAsync(() => Results.Json(new ApiError(error, message), statusCode: StatusCodes.Status503ServiceUnavailable));

    private async Task ListenAsync(Func<IResult> reply)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, Port));
        _foreign = builder.Build();
        _foreign.MapFallback(reply);
        await _foreign.StartAsync();
    }

    /// <summary>Holds a port without answering on it, so a service cannot start there.</summary>
    public static TcpListener Occupy(int port)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        return listener;
    }

    /// <summary>A port nothing listens on at the moment it is picked.</summary>
    public static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public void Dispose()
    {
        _host?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _service?.Dispose();
        _foreign?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Home.Dispose();
    }
}

/// <summary>The outcome of one run: the exit code and everything written to each stream.</summary>
internal sealed record CliResult(int ExitCode, string Stdout, string Stderr);

/// <summary>A text writer whose contents can be read while another thread is still writing to it.</summary>
internal sealed class CapturedText : TextWriter
{
    private readonly StringBuilder _text = new();

    public override Encoding Encoding => Encoding.UTF8;

    public string Text
    {
        get
        {
            lock (_text)
                return _text.ToString();
        }
    }

    public override void Write(char value)
    {
        lock (_text)
            _text.Append(value);
    }

    public override void Write(string? value)
    {
        lock (_text)
            _text.Append(value);
    }

    public void Clear()
    {
        lock (_text)
            _text.Clear();
    }
}

/// <summary>The one collection every CLI test belongs to: the framework's static state cannot take two runs at once.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CliCollection
{
    public const string Name = "CLI";
}
