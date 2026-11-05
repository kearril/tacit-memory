using Microsoft.Extensions.Logging;
using ConsoleAppFramework;
using TacitMemory.Core;
using TacitMemory.Mcp;
using TacitMemory.Core.Show;

namespace TacitMemory.Cli;

internal sealed class Commands
{
    private readonly TacitOptions _options = TacitOptions.FromEnvironment();
    private readonly Tacit _tacit;

    public Commands() => _tacit = new Tacit(_options);

    /// <summary>Create the config templates and the memory repository.</summary>
    /// <param name="language">Memory language: zh or en. Defaults to the system UI language.</param>
    /// <param name="memory">Memory repository location. Defaults to memory under TACIT_HOME.</param>
    [Command("init")]
    public async Task<int> Init(MemoryLanguage? language = null, string? memory = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _tacit.InitAsync(new InitRequest { Language = language, MemoryPath = memory }, cancellationToken);
            Console.WriteLine(result.Report);
            return 0;
        }
        catch (TacitException e)
        {
            return Fail(e);
        }
    }

    /// <summary>Start the memory service: maintain the resident blocks and serve MCP on 127.0.0.1.</summary>
    [Command("serve")]
    public async Task<int> Serve(CancellationToken cancellationToken = default)
    {
        // Everything this command prints is written to the day's log file as well; see ServeLog.
        using var log = new ServeLog(_options.TacitHome);
        MemoryService service;
        try
        {
            service = _tacit.OpenService(Console.WriteLine, log.Factory);
        }
        catch (Exception e) when (IsUserFacing(e))
        {
            log.Output.LogError("{Text}", e.Message);
            return Fail(e);
        }

        using (service)
        {
            foreach (var render in service.StartupRender)
                Console.WriteLine(render.Message);

            await using var host = await McpHost.TryStartAsync(service, service.Config.Port, log.Provider, cancellationToken);
            if (host is null)
            {
                var busy = service.PortInUseMessage();
                log.Output.LogWarning("{Text}", busy);
                Console.Error.WriteLine(busy);
                return 1;
            }

            var listening = service.ListeningMessage(host.Endpoint);
            log.Output.LogInformation("{Text}", listening);
            Console.WriteLine(listening);
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
    }

    /// <summary>Bind a client: write the resident block into its context file and register the MCP server.</summary>
    /// <param name="client">The client to bind: omp.</param>
    [Command("connect")]
    public int Connect(ClientKind client) => Run(() => _tacit.Connect(client).Report);

    /// <summary>Unbind a client: remove the resident block and the MCP registration.</summary>
    /// <param name="client">The client to unbind: omp.</param>
    [Command("disconnect")]
    public int Disconnect(ClientKind client) => Run(() => _tacit.Disconnect(client).Report);

    /// <summary>Show what the agent receives: the server instructions, the resident block, the memory skill, the tool descriptions or the remember prompt, each with its source. Needs no running service.</summary>
    /// <param name="target">instructions, resident, skill, tools or remember.</param>
    /// <param name="default">Print the built-in text alone, ready to save as the file that replaces it (instructions, resident) or the skill's system part (skill).</param>
    [Command("show")]
    public Task<int> Show([Argument] ShowWhat target, bool @default = false) => RunAsync(async () => target switch
    {
        ShowWhat.Tools => _tacit.ShowTools(ToolCatalog.Describe(), @default).Report,
        ShowWhat.Instructions => (await _tacit.ShowAsync(ShowTarget.Instructions, @default)).Report,
        ShowWhat.Resident => (await _tacit.ShowAsync(ShowTarget.Resident, @default)).Report,
        ShowWhat.Skill => (await _tacit.ShowAsync(ShowTarget.Skill, @default)).Report,
        _ => (await _tacit.ShowAsync(ShowTarget.Remember, @default)).Report,
    });

    /// <summary>
    /// Show the service's state, its auxiliary models, the bindings, the last checkup, the pending issues, the latest
    /// local and cloud backups, where the logs are and where each secret comes from. Exit code 0 when the service runs, 1 when it
    /// does not, 2 when <c>config.yaml</c> does not load (every wrong key path is listed). Only whether the service runs
    /// decides the exit code: a cooling model or an open problem does not.
    /// </summary>
    [Command("status")]
    public async Task<int> Status(CancellationToken cancellationToken = default)
    {
        TacitConfig config;
        try
        {
            config = _tacit.LoadConfig();
        }
        catch (TacitException e)
        {
            return PrintStatus(_tacit.InvalidConfigStatus(e), exitCode: 2);
        }

        using var api = new ManagementApiClient(config, _tacit);
        try
        {
            var report = await api.GetAsync<StatusReport>("status", TimeSpan.FromSeconds(2), cancellationToken);
            if (report.Service is null)
                throw new ForeignServiceException();
            return PrintStatus(report, report.Service.Running ? 0 : 1);
        }
        catch (ServiceNotRunningException)
        {
            return PrintStatus(await _tacit.OfflineStatus(config, cancellationToken: cancellationToken), exitCode: 1);
        }
        catch (ForeignServiceException)
        {
            return PrintStatus(await _tacit.OfflineStatus(config, portAnsweredByOther: true, cancellationToken: cancellationToken), exitCode: 1);
        }
        catch (ServiceRefusedException e)
        {
            return api.Fail(e);
        }
    }

    /// <summary>
    /// Rebuild the derived index from the memory files: the full-text index, the vectors and the judgment cache. Needs the
    /// running service. Waits until the rebuild is done, then prints the counts, the files that are not UTF-8 and the time taken.
    /// </summary>
    [Command("reindex")]
    public Task<int> Reindex(CancellationToken cancellationToken = default) => ServiceCommand(async api =>
    {
        var result = await api.PostAsync<ReindexResult>("reindex", new { }, cancellationToken);
        Console.WriteLine(result.Report);
        return 0;
    });

    /// <summary>
    /// Make a backup now: a zip of the memory repository, config, prompts and bindings. A local one goes to
    /// TACIT_HOME/backups/ (or to --to), and the automatic backup never deletes it. With --remote, the zip is uploaded to the
    /// WebDAV service in backup.webdav and replaces the one there. Needs the running service. Exit code 0 when it is written,
    /// 1 when it is not.
    /// </summary>
    /// <param name="to">Write the backup into this directory instead of TACIT_HOME/backups/; the directory is created when missing.</param>
    /// <param name="remote">Upload the backup to the cloud instead of writing it here; cannot be combined with --to.</param>
    [Command("backup")]
    public Task<int> Backup(string? to = null, bool remote = false, CancellationToken cancellationToken = default) => ServiceCommand(async api =>
    {
        var target = string.IsNullOrWhiteSpace(to) ? null : Path.GetFullPath(to);
        var body = new BackupBody { Target = remote ? BackupTarget.Cloud : BackupTarget.Local, To = target };
        var result = await api.PostAsync<BackupResult>("backup", body, cancellationToken);
        Console.WriteLine(result.Report);
        if (result.Notice is not null)
            Console.WriteLine(result.Notice);
        return 0;
    });

    /// <summary>
    /// Restore the memory repository from a backup zip, or from the cloud backup with <c>--remote</c>. Needs the running service.
    /// Shows what the backup holds and what the restore replaces, then waits for a line that is exactly <c>y</c> before it changes
    /// anything; <c>--yes</c> skips the wait. A cloud restore downloads the cloud backup to TACIT_HOME/backups/.download/ first,
    /// and the confirmed restore takes that same file. Exit code 0 when it is restored, 1 when it is not, including when the answer
    /// is anything but <c>y</c>.
    /// </summary>
    /// <param name="zip">The backup zip to restore from; cannot be combined with <c>--remote</c>.</param>
    /// <param name="remote">Restore from the cloud backup in <c>backup.webdav</c> instead of a zip.</param>
    /// <param name="config">Also restore <c>config.yaml</c> and <c>prompts/</c> from the backup; <c>bindings.json</c> is never restored.</param>
    /// <param name="yes">Restore without asking, after the preview is shown.</param>
    [Command("restore")]
    public Task<int> Restore([Argument] string? zip = null, bool remote = false, bool config = false, bool yes = false, CancellationToken cancellationToken = default) => ServiceCommand(async api =>
    {
        var path = zip is null ? null : Path.GetFullPath(zip);
        var preview = await api.PostAsync<RestorePreview>("restore", new RestoreBody { Path = path, Remote = remote, Config = config, Preview = true }, cancellationToken);
        Console.WriteLine(preview.Report);
        if (!yes)
        {
            Console.Write(preview.Prompt + " ");
            var answer = Console.ReadLine();
            if (!string.Equals(answer?.Trim(), "y", StringComparison.Ordinal))
            {
                Console.WriteLine(preview.Cancelled);
                return 1;
            }
        }

        // The confirmed file is the one the preview named, so a cloud restore takes the file that was shown.
        var result = await api.PostAsync<RestoreResult>("restore", new RestoreBody { Path = preview.Path, Config = config, Preview = false }, cancellationToken);
        Console.WriteLine(result.Report);
        return 0;
    });

    /// <summary>
    /// Runs a command that needs the running service: loads the config, calls the management API through one client, and
    /// shows a failure of the call (no service, another program on the port, a refusal) as its message with exit code 1.
    /// </summary>
    private async Task<int> ServiceCommand(Func<ManagementApiClient, Task<int>> call)
    {
        TacitConfig config;
        try
        {
            config = _tacit.LoadConfig();
        }
        catch (TacitException e)
        {
            return Fail(e);
        }

        using var api = new ManagementApiClient(config, _tacit);
        try
        {
            return await call(api);
        }
        catch (Exception e) when (e is ServiceNotRunningException or ForeignServiceException or ServiceRefusedException)
        {
            return api.Fail(e);
        }
    }

    /// <summary>Prints each column's title, then its lines indented, in the order the report lists them. Returns <paramref name="exitCode"/>.</summary>
    private static int PrintStatus(StatusReport report, int exitCode)
    {
        foreach (var column in new IStatusColumn?[]
                 {
                     report.Config, report.Service, report.Models, report.Bindings, report.Checkup,
                     report.Pending, report.Backups, report.Logs, report.Secrets,
                 })
        {
            if (column is null)
                continue;
            Console.WriteLine(column.Title);
            foreach (var line in column.Lines)
                Console.WriteLine($"  {line}");
        }

        return exitCode;
    }

    private static int Run(Func<string> operation)
    {
        try
        {
            Console.WriteLine(operation());
            return 0;
        }
        catch (Exception e) when (IsUserFacing(e))
        {
            return Fail(e);
        }
    }

    private static async Task<int> RunAsync(Func<Task<string>> operation)
    {
        try
        {
            Console.WriteLine(await operation());
            return 0;
        }
        catch (Exception e) when (IsUserFacing(e))
        {
            return Fail(e);
        }
    }

    /// <summary>Refusals and file-system failures the user can act on; their messages are shown as they are.</summary>
    private static bool IsUserFacing(Exception e) => e is TacitException or IOException or UnauthorizedAccessException;

    private static int Fail(Exception e)
    {
        Console.Error.WriteLine(e.Message);
        return 1;
    }
}

/// <summary>The targets of <c>tacit show</c>.</summary>
internal enum ShowWhat
{
    Instructions,
    Resident,
    Skill,
    Tools,
    Remember,
}
