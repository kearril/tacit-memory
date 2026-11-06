using ConsoleAppFramework;
using Microsoft.Extensions.Logging;
using TacitMemory.Core;
using TacitMemory.Core.Show;
using TacitMemory.Mcp;

namespace TacitMemory.Cli;

/// <summary>
/// The commands of <c>tacit</c>. Every parameter is optional so the framework always reaches the body; each body checks
/// its own required and mutually exclusive options first, then does its work, both inside <see cref="Execute"/>, which
/// maps the outcome to the exit code.
/// </summary>
internal sealed class Commands(TacitOptions options, CliConsole console)
{
    private readonly Tacit _tacit = new(options);

    /// <summary>TacitMemory's command line. Exit codes: 0 success, 1 failed, 2 usage error, 3 config invalid, 4 service not running.</summary>
    /// <param name="command">The command name that is not known.</param>
    [Command("")]
    public Task<int> Unknown([Argument] string? command = null) => Execute(() => throw new UsageException(command is null
        ? "No command given. Run tacit --help to list the commands."
        : $"Unknown command '{command}'. Run tacit --help to list the commands."));

    /// <summary>Create the config and the memory repository.</summary>
    /// <param name="language">Memory language: zh or en. Defaults to the system UI language.</param>
    /// <param name="memory">Memory repository location. Defaults to memory under TACIT_HOME.</param>
    [Command("init")]
    public Task<int> Init(string? language = null, string? memory = null, CancellationToken cancellationToken = default) => Execute(async () =>
    {
        var parsed = language is null ? (MemoryLanguage?)null : ParseChoice<MemoryLanguage>("--language", language);
        var result = await _tacit.InitAsync(new InitRequest { Language = parsed, MemoryPath = memory }, cancellationToken);
        console.Out.WriteLine(result.Report);
        return 0;
    });

    /// <summary>Run the memory service on 127.0.0.1 until Ctrl+C.</summary>
    [Command("serve")]
    public Task<int> Serve(ConsoleAppContext context, CancellationToken cancellationToken = default) => Execute(async () =>
    {
        RejectArguments(context);

        // Everything this command prints is written to the day's log file as well; see ServeLog.
        using var log = new ServeLog(options.TacitHome);
        MemoryService service;
        try
        {
            service = _tacit.OpenService(line => console.Out.WriteLine(line), log.Factory);
        }
        catch (Exception e) when (e is TacitException or IOException or UnauthorizedAccessException)
        {
            log.Output.LogError("{Text}", e.Message);
            throw;
        }

        using (service)
        {
            foreach (var render in service.StartupRender)
                console.Out.WriteLine(render.Message);

            await using var host = await McpHost.TryStartAsync(service, service.Config.Port, log.Provider, cancellationToken);
            if (host is null)
            {
                var busy = service.PortInUseMessage();
                log.Output.LogWarning("{Text}", busy);
                console.Error.WriteLine(busy);
                return 1;
            }

            var listening = service.ListeningMessage(host.Endpoint);
            log.Output.LogInformation("{Text}", listening);
            console.Out.WriteLine(listening);
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Ctrl+C: stop the host and leave.
            }

            return 0;
        }
    });

    /// <summary>Bind a client: write the resident block into its context file and register the MCP server.</summary>
    /// <param name="client">The client to bind: omp.</param>
    [Command("connect")]
    public Task<int> Connect(string? client = null) => Execute(() =>
    {
        var kind = ParseChoice<ClientKind>("--client", client);
        console.Out.WriteLine(_tacit.Connect(kind).Report);
        return Task.FromResult(0);
    });

    /// <summary>Unbind a client: remove the resident block and the MCP registration.</summary>
    /// <param name="client">The client to unbind: omp.</param>
    [Command("disconnect")]
    public Task<int> Disconnect(string? client = null) => Execute(() =>
    {
        var kind = ParseChoice<ClientKind>("--client", client);
        console.Out.WriteLine(_tacit.Disconnect(kind).Report);
        return Task.FromResult(0);
    });

    /// <summary>Show what the agent receives, with each part's source. Needs no running service.</summary>
    /// <param name="target">instructions, resident, skill, tools, remember or checkup.</param>
    /// <param name="default">Print the built-in text alone, ready to save as the file that replaces it (instructions, resident) or the skill's system part (skill).</param>
    [Command("show")]
    public Task<int> Show([Argument] string? target = null, bool @default = false) => Execute(async () =>
    {
        var what = ParseChoice<ShowWhat>("show target", target);
        if (@default && what is ShowWhat.Tools or ShowWhat.Remember or ShowWhat.Checkup)
            throw new UsageException($"--default cannot be used with show {Lower(what)}: no file replaces it.");

        var report = what switch
        {
            ShowWhat.Tools => _tacit.ShowTools(ToolCatalog.Describe(), @default).Report,
            ShowWhat.Instructions => (await _tacit.ShowAsync(ShowTarget.Instructions, @default)).Report,
            ShowWhat.Resident => (await _tacit.ShowAsync(ShowTarget.Resident, @default)).Report,
            ShowWhat.Skill => (await _tacit.ShowAsync(ShowTarget.Skill, @default)).Report,
            ShowWhat.Remember => (await _tacit.ShowAsync(ShowTarget.Remember, @default)).Report,
            ShowWhat.Checkup => (await _tacit.ShowAsync(ShowTarget.Checkup, @default)).Report,
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
        };
        console.Out.WriteLine(report);
        return 0;
    });

    /// <summary>Show the service's state, models, bindings, checkup, pending items, backups, logs and secrets.</summary>
    /// <remarks>Only whether the service runs decides the exit code: a cooling model or an open problem does not.</remarks>
    [Command("status")]
    public Task<int> Status(ConsoleAppContext context, CancellationToken cancellationToken = default) => Execute(async () =>
    {
        RejectArguments(context);

        TacitConfig config;
        try
        {
            config = _tacit.LoadConfig();
        }
        catch (TacitException e) when (IsConfigRefusal(e))
        {
            PrintStatus(_tacit.InvalidConfigStatus(e));
            return 3;
        }

        using var api = new ManagementApiClient(config, _tacit);
        try
        {
            var report = await api.GetAsync<StatusReport>("status", TimeSpan.FromSeconds(2), cancellationToken);
            if (report.Service is not null)
            {
                PrintStatus(report);
                return report.Service.Running ? 0 : 4;
            }

            // A status report that names no service is not this service's answer.
            return await PrintOfflineAsync(config, portAnsweredByOther: true, cancellationToken);
        }
        catch (ServiceNotRunningException)
        {
            return await PrintOfflineAsync(config, portAnsweredByOther: false, cancellationToken);
        }
        catch (ForeignServiceException)
        {
            return await PrintOfflineAsync(config, portAnsweredByOther: true, cancellationToken);
        }
    });

    /// <summary>Rebuild the derived index from the memory files. Needs the running service.</summary>
    [Command("reindex")]
    public Task<int> Reindex(ConsoleAppContext context, CancellationToken cancellationToken = default) => Execute(() =>
    {
        RejectArguments(context);
        return WithServiceAsync(async api =>
        {
            var result = await api.PostAsync<ReindexResult>("reindex", new { }, cancellationToken);
            console.Out.WriteLine(result.Report);
            return 0;
        });
    });

    /// <summary>Back up the memory repository, config, prompts and bindings. Needs the running service.</summary>
    /// <param name="to">Write the backup into this directory instead of TACIT_HOME/backups/; the directory is created when missing.</param>
    /// <param name="remote">Upload the backup to the cloud (backup.webdav) instead; cannot be combined with --to.</param>
    [Command("backup")]
    public Task<int> Backup(string? to = null, bool remote = false, CancellationToken cancellationToken = default) => Execute(() =>
    {
        if (remote && to is not null)
            throw new UsageException("--to and --remote cannot be combined: a backup goes to one place.");

        var target = string.IsNullOrWhiteSpace(to) ? null : Path.GetFullPath(to);
        var body = new BackupBody { Target = remote ? BackupTarget.Cloud : BackupTarget.Local, To = target };
        return WithServiceAsync(async api =>
        {
            var result = await api.PostAsync<BackupResult>("backup", body, cancellationToken);
            console.Out.WriteLine(result.Report);
            if (result.Notice is not null)
                console.Out.WriteLine(result.Notice);
            return 0;
        });
    });

    /// <summary>
    /// Restore the memory repository from a backup zip, or from the cloud backup with <c>--remote</c>. Needs the running service.
    /// Shows what the backup holds and what the restore replaces, then waits for a line that is exactly <c>y</c> before it changes
    /// anything; <c>--yes</c> skips the wait. Without <c>--yes</c>, standard input must be a terminal to answer.
    /// A cloud restore downloads the cloud backup to TACIT_HOME/backups/.download/ first,
    /// and the confirmed restore takes that same file.
    /// </summary>
    /// <param name="zip">The backup zip to restore from; cannot be combined with <c>--remote</c>.</param>
    /// <param name="remote">Restore from the cloud backup in <c>backup.webdav</c> instead of a zip.</param>
    /// <param name="config">Also restore <c>config.yaml</c> and <c>prompts/</c> from the backup; <c>bindings.json</c> is never restored.</param>
    /// <param name="yes">Restore without asking, after the preview is shown.</param>
    [Command("restore")]
    public Task<int> Restore([Argument] string? zip = null, bool remote = false, bool config = false, bool yes = false, CancellationToken cancellationToken = default) => Execute(() =>
    {
        if (zip is null && !remote)
            throw new UsageException("Give a backup zip to restore from, or --remote for the cloud backup.");
        if (zip is not null && remote)
            throw new UsageException("A backup zip and --remote cannot be combined: restore from one place.");

        var path = zip is null ? null : Path.GetFullPath(zip);
        return WithServiceAsync(async api =>
        {
            var preview = await api.PostAsync<RestorePreview>("restore", new RestoreBody { Path = path, Remote = remote, Config = config, Preview = true }, cancellationToken);
            console.Out.WriteLine(preview.Report);
            if (!yes)
            {
                if (!console.InIsTerminal)
                    throw new UsageException("restore asks for confirmation, but standard input is not a terminal: add --yes to restore without asking.");

                console.Error.Write(preview.Prompt + " ");
                var answer = console.In.ReadLine();
                if (!string.Equals(answer?.Trim(), "y", StringComparison.Ordinal))
                {
                    console.Error.WriteLine(preview.Cancelled);
                    return 1;
                }
            }

            // The confirmed file is the one the preview named, so a cloud restore takes the file that was shown.
            var result = await api.PostAsync<RestoreResult>("restore", new RestoreBody { Path = preview.Path, Config = config, Preview = false }, cancellationToken);
            console.Out.WriteLine(result.Report);
            return 0;
        });
    });

    /// <summary>
    /// The one pipeline every command runs through. Maps the outcome to the exit code and writes every failure to stderr:
    /// 2 for a usage error, 3 when the config refuses to load, 4 when the service does not answer or another program
    /// answers its port, 1 for any other refusal, failure or file error. Anything else is left to the framework, which
    /// reports it as an unexpected failure.
    /// </summary>
    private async Task<int> Execute(Func<Task<int>> body)
    {
        try
        {
            return await body();
        }
        catch (UsageException e)
        {
            return Fail(2, e.Message);
        }
        catch (TacitException e) when (IsConfigRefusal(e))
        {
            return Fail(3, e.Message);
        }
        catch (Exception e) when (e is ServiceNotRunningException or ForeignServiceException)
        {
            return Fail(4, e.Message);
        }
        catch (Exception e) when (e is ServiceRefusedException or TacitException or IOException or UnauthorizedAccessException)
        {
            return Fail(1, e.Message);
        }
    }

    /// <summary>Loads the config, then runs a call through one management API client. A missing config is refused before any call.</summary>
    private async Task<int> WithServiceAsync(Func<ManagementApiClient, Task<int>> call)
    {
        var config = _tacit.LoadConfig();
        using var api = new ManagementApiClient(config, _tacit);
        return await call(api);
    }

    private async Task<int> PrintOfflineAsync(TacitConfig config, bool portAnsweredByOther, CancellationToken cancellationToken)
    {
        PrintStatus(await _tacit.OfflineStatus(config, portAnsweredByOther, cancellationToken));
        return 4;
    }

    /// <summary>Prints each column's title, then its lines indented, in the order the report lists them.</summary>
    private void PrintStatus(StatusReport report)
    {
        foreach (var column in new IStatusColumn?[]
                 {
                     report.Config, report.Service, report.Models, report.Bindings, report.Checkup,
                     report.Pending, report.Backups, report.Logs, report.Secrets,
                 })
        {
            if (column is null)
                continue;
            console.Out.WriteLine(column.Title);
            foreach (var line in column.Lines)
                console.Out.WriteLine($"  {line}");
        }
    }

    /// <summary>A command with no options or positional arguments: anything after its name is an error.</summary>
    private static void RejectArguments(ConsoleAppContext context)
    {
        if (context.CommandArguments is { Length: > 0 } rest)
            throw new UsageException($"Argument '{rest[0]}' is not recognized.");
    }

    private int Fail(int exitCode, string message)
    {
        console.Error.WriteLine(message);
        return exitCode;
    }

    /// <summary>A config the commands cannot load: missing, unreadable or wrong, or a memory repository not initialized yet.</summary>
    private static bool IsConfigRefusal(TacitException e) =>
        e.Error is TacitError.NotInitialized or TacitError.ConfigInvalid or TacitError.PromptInvalid;

    /// <summary>
    /// Reads an option whose values are the names of an enum. The match ignores case; the valid values are listed in
    /// lower case, since that is how they are written.
    /// </summary>
    private static T ParseChoice<T>(string what, string? value) where T : struct, Enum
    {
        var names = Enum.GetNames<T>();
        var valid = string.Join(", ", names.Select(name => name.ToLowerInvariant()));
        if (value is null)
            throw new UsageException($"Missing {what}. Valid values: {valid}.");

        var match = names.FirstOrDefault(name => string.Equals(name, value, StringComparison.OrdinalIgnoreCase))
            ?? throw new UsageException($"Invalid {what} '{value}'. Valid values: {valid}.");
        return Enum.Parse<T>(match);
    }

    private static string Lower<T>(T value) where T : Enum => value.ToString().ToLowerInvariant();
}

/// <summary>The targets of <c>tacit show</c>.</summary>
internal enum ShowWhat
{
    Instructions,
    Resident,
    Skill,
    Tools,
    Remember,
    Checkup,
}
