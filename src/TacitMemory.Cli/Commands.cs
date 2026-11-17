using System.Diagnostics;
using ConsoleAppFramework;
using Microsoft.Extensions.Logging;
using TacitMemory.Core;
using TacitMemory.Core.Show;
using TacitMemory.Host;
using TacitMemory.Host.Api;
using TacitMemory.Host.Localization;
using TacitMemory.Host.Mcp;

namespace TacitMemory.Cli;

/// <summary>
/// The commands of <c>tacit</c>. Every parameter is optional so the framework always reaches the body; each body checks
/// its own required and mutually exclusive options first, then does its work, both inside <see cref="Execute"/>, which
/// maps the outcome to the exit code.
/// </summary>
/// <remarks>
/// A string option is declared as a non-nullable string with the default <c>null!</c>. The framework prints a nullable
/// type with a trailing <c>?</c> in help, and a null default is what keeps the option optional; null means the option was
/// not given. Its value is never null when it is given.
/// </remarks>
internal sealed class Commands(TacitOptions options, CliConsole console)
{
    private readonly Tacit _tacit = new(options);

    /// <summary>TacitMemory's command line. Exit codes: 0 success, 1 failed, 2 usage error, 3 config invalid, 4 service not running.</summary>
    [Command("")]
    public Task<int> Unknown(ConsoleAppContext context) => Execute(() =>
    {
        var command = FirstArgument(context);
        throw new UsageException(command is null
            ? "No command given. Run tacit --help to list the commands."
            : $"Unknown command '{command}'. Run tacit --help to list the commands.");
    });

    /// <summary>Create the config and the memory repository.</summary>
    /// <param name="language">Memory language, zh or en.</param>
    /// <param name="memory">Memory repository location.</param>
    [Command("init")]
    public Task<int> Init([HideDefaultValue] string language = null!, [HideDefaultValue] string memory = null!, CancellationToken cancellationToken = default) => Execute(async () =>
    {
        if (memory is not null)
            RequirePath("--memory", memory);
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

            await using var host = await ServiceHost.TryStartAsync(service, service.Config.Port, log.Provider, cancellationToken);
            if (host is null)
            {
                var busy = HostMessages.PortInUse.Format(service.Language, service.Config.Port, Path.Combine(options.TacitHome, "config.yaml"));
                log.Output.LogWarning("{Text}", busy);
                console.Error.WriteLine(busy);
                return 1;
            }

            var listening = HostMessages.ServiceListening.Format(service.Language, host.Endpoint);
            log.Output.LogInformation("{Text}", listening);
            console.Out.WriteLine(listening);
            if (service.ExtraListener is { FailureReason: null } extra)
            {
                var extraLine = extra.Line(service.Language);
                log.Output.LogInformation("{Text}", extraLine);
                console.Out.WriteLine(extraLine);
            }
            // Ctrl+C stops the host and leaves; so does a reload that could open the service with neither config, which the
            // service has logged, and then the exit says the run failed.
            var stopped = Task.Delay(Timeout.Infinite, cancellationToken);
            if (await Task.WhenAny(stopped, service.Failed) == stopped)
                return 0;
            console.Error.WriteLine((await service.Failed).Message);
            return 1;
        }
    });

    /// <summary>Bind a client: write the resident block into its context file and link the skills into its skills folder.</summary>
    /// <param name="client">Client name: letters, digits and hyphens, up to 32 characters.</param>
    /// <param name="context">Context file the resident block goes into; created when missing.</param>
    /// <param name="skills">Skills folder the client scans; it must already exist.</param>
    [Command("connect")]
    public Task<int> Connect([HideDefaultValue] string client = null!, [HideDefaultValue] string context = null!, [HideDefaultValue] string skills = null!) => Execute(() =>
    {
        RequireOptions(("--client", client), ("--context", context), ("--skills", skills));
        RequirePath("--context", context);
        RequirePath("--skills", skills);
        var result = _tacit.Connect(new ConnectRequest(client, context, skills));
        if (console.StylesOut)
            StyledOutput.Endpoint(console.Out, result);
        else
            console.Out.WriteLine(result.Report);
        return Task.FromResult(0);
    });

    /// <summary>Unbind a client: remove its resident block and skill links. The paths come from the binding.</summary>
    /// <param name="client">Name of the client to unbind.</param>
    [Command("disconnect")]
    public Task<int> Disconnect([HideDefaultValue] string client = null!) => Execute(() =>
    {
        RequireOptions(("--client", client));
        console.Out.WriteLine(_tacit.Disconnect(client).Report);
        return Task.FromResult(0);
    });

    /// <summary>Show what the agent receives, with each part's source. Needs no running service.</summary>
    [Command("show")]
    public Task<int> Show(ConsoleAppContext context) => Execute(() =>
    {
        var subcommand = FirstArgument(context);
        throw new UsageException(subcommand is null
            ? "Missing show subcommand. Valid subcommands: instructions, rules, resident, skill, tools, remember, consolidate."
            : $"Unknown show subcommand '{subcommand}'. Valid subcommands: instructions, rules, resident, skill, tools, remember, consolidate.");
    });

    /// <summary>Show the server instructions a session receives.</summary>
    /// <param name="default">Print only the built-in text, ready to save as the file that replaces it.</param>
    [Command("show instructions")]
    public Task<int> ShowInstructions(bool @default = false) => ShowPart(ShowTarget.Instructions, @default);

    /// <summary>Show the recording criteria: what is worth recording and what is not.</summary>
    /// <param name="default">Print only the built-in text, ready to save as the file that replaces it.</param>
    [Command("show rules")]
    public Task<int> ShowRules(bool @default = false) => ShowPart(ShowTarget.Rules, @default);

    /// <summary>Show the resident block that is written into the context file.</summary>
    /// <param name="default">Print only the built-in text, ready to save as the file that replaces it.</param>
    [Command("show resident")]
    public Task<int> ShowResident(bool @default = false) => ShowPart(ShowTarget.Resident, @default);

    /// <summary>Show the memory skill.</summary>
    /// <param name="default">Print only the built-in text of the skill's system part.</param>
    [Command("show skill")]
    public Task<int> ShowSkill(bool @default = false) => ShowPart(ShowTarget.Skill, @default);

    /// <summary>
    /// Sends one real request to each model in config.yaml and says whether it works. The request is a fixed English text;
    /// nothing from memory is read or sent. Needs no running service and no initialized memory repository.
    /// </summary>
    /// <remarks>
    /// Exit codes: 0 when every model probed works, or none is configured and none was named; 1 when a model fails, or a model
    /// named on the command line is not configured; 2 for an unknown target or an extra argument; 3 when config.yaml does not
    /// load. Without a target, models that are not configured are skipped.
    /// </remarks>
    [Command("probe")]
    public Task<int> ProbeAll(ConsoleAppContext context) => Execute(() =>
    {
        var target = FirstArgument(context);
        if (target is not null)
            throw new UsageException($"Unknown probe target '{target}'. Valid targets: {string.Join(", ", ModelRoles.All.Select(ModelRoles.Name))}.");
        return RunProbeAsync(AllModels, explicitTargets: false);
    });

    /// <summary>Probe the embedding model in config.yaml.</summary>
    [Command("probe embedding")]
    public Task<int> ProbeEmbedding(ConsoleAppContext context) => ProbeOne(context, ModelRole.Embedding);

    /// <summary>Probe the reranker in config.yaml.</summary>
    [Command("probe reranker")]
    public Task<int> ProbeReranker(ConsoleAppContext context) => ProbeOne(context, ModelRole.Reranker);

    /// <summary>Probe the decision model in config.yaml.</summary>
    [Command("probe decision")]
    public Task<int> ProbeDecision(ConsoleAppContext context) => ProbeOne(context, ModelRole.Decision);

    /// <summary>The models the bare <c>probe</c> command probes, in report order.</summary>
    private static readonly ModelRole[] AllModels = [.. ModelRoles.All];

    private Task<int> ProbeOne(ConsoleAppContext context, ModelRole role) => Execute(() =>
    {
        RejectArguments(context);
        return RunProbeAsync([role], explicitTargets: true);
    });

    /// <summary>
    /// Probes the given models and prints the report. The exit code is 1 when a model failed, or when a model the user named is
    /// not configured; 0 otherwise. An unconfigured model that was not named is skipped and is not a failure.
    /// </summary>
    private async Task<int> RunProbeAsync(IReadOnlyList<ModelRole> roles, bool explicitTargets)
    {
        var config = _tacit.LoadConfigFile();
        var report = await _tacit.ProbeAsync(config, roles, explicitTargets);
        if (console.StylesOut)
            StyledOutput.Probe(console.Out, report.Lines);
        else
            foreach (var line in report.Lines)
                console.Out.WriteLine(line.Text);
        return ProbeExitCode(report.Results, explicitTargets);
    }

    private static int ProbeExitCode(IReadOnlyList<ProbeResult> results, bool explicitTargets) =>
        results.Any(result => result.Outcome == ProbeOutcome.Failed || (explicitTargets && result.Outcome == ProbeOutcome.Unconfigured)) ? 1 : 0;

    /// <summary>Show the memory tools the agent can call.</summary>
    [Command("show tools")]
    public Task<int> ShowTools(ConsoleAppContext context) => Execute(() =>
    {
        RejectArguments(context);
        var config = _tacit.LoadConfig();
        console.Out.WriteLine(_tacit.ShowTools(config, ToolCatalog.Describe(config.Limits)).Report);
        return Task.FromResult(0);
    });

    /// <summary>Show the remember skill.</summary>
    [Command("show remember")]
    public Task<int> ShowRemember(ConsoleAppContext context) => ShowPart(ShowTarget.Remember, useDefault: false, context);

    /// <summary>Show the consolidation skill.</summary>
    [Command("show consolidate")]
    public Task<int> ShowConsolidate(ConsoleAppContext context) => ShowPart(ShowTarget.Consolidation, useDefault: false, context);

    /// <summary>Prints one part of what the agent receives. A context is given only by the parts that take no options, so leftover words are refused.</summary>
    private Task<int> ShowPart(ShowTarget target, bool useDefault, ConsoleAppContext? context = null) => Execute(async () =>
    {
        if (context is not null)
            RejectArguments(context);
        var result = await _tacit.ShowAsync(target, useDefault);
        console.Out.WriteLine(result.Report);
        return 0;
    });

    /// <summary>Show the service's state, models, bindings, consolidation, pending items, backups, logs and secrets.</summary>
    /// <remarks>
    /// The exit code is 3 whenever config.yaml does not load. Otherwise it is 0 while a service answers with its report,
    /// 1 when the service refuses the report or the offline report cannot be read (an IO or permission error; the reason
    /// goes to stderr), and 4 when nothing answers or another program holds the port. A cooling model or an open problem
    /// changes no code.
    /// </remarks>
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
            return await StatusWithInvalidConfigAsync(e, cancellationToken);
        }

        using var api = new ManagementApiClient(config.Port, config.Language, _tacit);
        switch (await ProbeAsync(api, cancellationToken))
        {
            case Probe.Answered answered:
                PrintStatus(answered.Report);
                return 0;
            case Probe.Refused refused:
                console.Error.WriteLine(refused.Message);
                return 1;
            case Probe.NotAnswered notAnswered:
                return await PrintOfflineAsync(config, notAnswered.OtherProgram, cancellationToken);
            default:
                throw new UnreachableException();
        }
    });

    /// <summary>
    /// The status when <c>config.yaml</c> does not load: the config errors, then the service's own report when a service
    /// answers on the port the file still names, or on the default port. Exits 3 whether or not the service runs.
    /// </summary>
    private async Task<int> StatusWithInvalidConfigAsync(TacitException error, CancellationToken cancellationToken)
    {
        var settings = _tacit.ReadLooseSettings();
        using var api = new ManagementApiClient(settings.Port, settings.Language, _tacit);
        switch (await ProbeAsync(api, cancellationToken))
        {
            case Probe.Answered answered:
                // The service runs on the config it loaded at start; this report keeps only the config column from the file now.
                PrintStatus(answered.Report with { Config = _tacit.InvalidConfigStatus(error, settings).Config });
                break;
            case Probe.Refused refused:
                // The config still decides the exit code: the config errors are shown, and the refusal is named on stderr.
                PrintStatus(_tacit.InvalidConfigStatus(error, settings) with { Service = null });
                console.Error.WriteLine(refused.Message);
                break;
            case Probe.NotAnswered notAnswered:
                PrintStatus(_tacit.InvalidConfigStatus(error, settings, portAnsweredByOther: notAnswered.OtherProgram));
                break;
            default:
                throw new UnreachableException();
        }

        return 3;
    }

    /// <summary>
    /// Asks the service for its status report. A report that names no service is not this service's answer, so it counts
    /// as another program holding the port. A service that is not running refuses the connection at once; the timeout only
    /// bounds a program that accepts it and never answers, and is generous because a busy machine can take seconds to build
    /// the report.
    /// </summary>
    private static async Task<Probe> ProbeAsync(ManagementApiClient api, CancellationToken cancellationToken)
    {
        try
        {
            var report = await api.GetAsync<StatusReport>("status", TimeSpan.FromSeconds(10), cancellationToken);
            return report.Service is null ? new Probe.NotAnswered(OtherProgram: true) : new Probe.Answered(report);
        }
        catch (ServiceNotRunningException)
        {
            return new Probe.NotAnswered(OtherProgram: false);
        }
        catch (ForeignServiceException)
        {
            return new Probe.NotAnswered(OtherProgram: true);
        }
        catch (ServiceRefusedException refused)
        {
            return new Probe.Refused(refused.Message);
        }
    }

    /// <summary>Rebuild the derived index from the memory files. Needs the running service.</summary>
    [Command("reindex")]
    public Task<int> Reindex(ConsoleAppContext context, CancellationToken cancellationToken = default) => Execute(() =>
    {
        RejectArguments(context);
        return WithServiceAsync(async api =>
        {
            var result = await WaitingAsync(() => api.PostAsync<ReindexResult>("reindex", cancellationToken: cancellationToken));
            console.Out.WriteLine(result.Report);
            return 0;
        });
    });

    /// <summary>Back up the memory repository, config, prompts and bindings. Needs the running service.</summary>
    /// <param name="to">Directory to write the backup into.</param>
    /// <param name="remote">Upload the backup to the cloud (backup.webdav) instead.</param>
    [Command("backup")]
    public Task<int> Backup([HideDefaultValue] string to = null!, bool remote = false, CancellationToken cancellationToken = default) => Execute(() =>
    {
        if (remote && to is not null)
            throw new UsageException("--to and --remote cannot be combined: a backup goes to one place.");
        if (to is not null)
            RequirePath("--to", to);

        var body = new BackupBody { Target = remote ? BackupTarget.Cloud : BackupTarget.Local, To = to is null ? null : Path.GetFullPath(to) };
        return WithServiceAsync(async api =>
        {
            var result = await WaitingAsync(() => api.PostAsync<BackupResult>("backup", body, cancellationToken), show: remote);
            console.Out.WriteLine(result.Report);
            if (result.Notice is not null)
                console.Out.WriteLine(result.Notice);
            return 0;
        });
    });

    /// <summary>Restore the memory repository from a backup zip or from the cloud backup. Needs the running service.</summary>
    /// <remarks>
    /// Shows what the backup holds and what the restore replaces, then waits for a line that is exactly <c>y</c> before it changes
    /// anything; <c>--yes</c> skips the wait. Without <c>--yes</c>, standard input must be a terminal to answer.
    /// A cloud restore downloads the cloud backup to TACIT_HOME/backups/.download/ first,
    /// and the confirmed restore takes that same file.
    /// </remarks>
    /// <param name="from">The backup zip to restore from; cannot be combined with <c>--remote</c>.</param>
    /// <param name="remote">Restore from the cloud backup (backup.webdav) instead.</param>
    /// <param name="includeConfig">Also restore config.yaml and prompts/ from the backup.</param>
    /// <param name="yes">-y, Restore without asking, after the preview is shown.</param>
    [Command("restore")]
    public Task<int> Restore([HideDefaultValue] string from = null!, bool remote = false, bool includeConfig = false, bool yes = false, CancellationToken cancellationToken = default) => Execute(() =>
    {
        if (from is null && !remote)
            throw new UsageException("Give a backup zip with --from, or --remote for the cloud backup.");
        if (from is not null && remote)
            throw new UsageException("--from and --remote cannot be combined: restore from one place.");
        if (from is not null)
            RequirePath("--from", from);

        var path = from is null ? null : Path.GetFullPath(from);
        return WithServiceAsync(async api =>
        {
            var preview = await api.PostAsync<RestorePreview>("restore", new RestoreBody { Path = path, Remote = remote, Config = includeConfig, Preview = true }, cancellationToken);
            if (console.StylesOut)
                StyledOutput.Preview(console.Out, preview);
            else
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
            var result = await WaitingAsync(() => api.PostAsync<RestoreResult>("restore", new RestoreBody { Path = preview.Path, Config = includeConfig, Preview = false }, cancellationToken));
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
        catch (TacitException e) when (e.Error == TacitError.ClientNameInvalid)
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
        using var api = new ManagementApiClient(config.Port, config.Language, _tacit);
        return await call(api);
    }

    /// <summary>
    /// Runs a call to the service. When <paramref name="show"/> is set and stderr is a terminal, a spinner and the elapsed
    /// time show on stderr while it runs; they are cleared when it ends.
    /// </summary>
    private Task<T> WaitingAsync<T>(Func<Task<T>> call, bool show = true) =>
        show && console.StylesError ? StyledOutput.WithIndicatorAsync(console.Error, call) : call();

    /// <summary>
    /// Prints the report for a service that does not answer, and returns 4. A report that cannot be read (an IO or
    /// permission error) is a run failure: the reason is named on stderr and the code is 1.
    /// </summary>
    private async Task<int> PrintOfflineAsync(TacitConfig config, bool portAnsweredByOther, CancellationToken cancellationToken)
    {
        try
        {
            PrintStatus(await _tacit.OfflineStatus(config, portAnsweredByOther, cancellationToken));
            return 4;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            console.Error.WriteLine(e.Message);
            return 1;
        }
    }

    /// <summary>Prints each column's title, then its lines indented, in the order the report lists them.</summary>
    private void PrintStatus(StatusReport report)
    {
        if (console.StylesOut)
        {
            StyledOutput.Status(console.Out, report);
            return;
        }

        foreach (var column in report.Columns)
        {
            console.Out.WriteLine(column.Title);
            foreach (var line in column.Lines)
                console.Out.WriteLine($"  {line}");
        }
    }

    /// <summary>Checks that each required option has a value, before anything is read; a blank value counts as missing.</summary>
    private static void RequireOptions(params (string Option, string? Value)[] options)
    {
        var missing = options.Where(option => string.IsNullOrWhiteSpace(option.Value)).Select(option => option.Option).ToList();
        if (missing.Count > 0)
            throw new UsageException($"Missing required option{(missing.Count > 1 ? "s" : "")} {string.Join(", ", missing)}.");
    }

    /// <summary>
    /// Checks that a path option names something the path syntax accepts, before anything is read. A blank value, or one
    /// the syntax refuses, is a usage error. The value itself is left as given; the command decides how to resolve it.
    /// </summary>
    private static void RequirePath(string option, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new UsageException($"{option} needs a path, but the value is empty.");
        try
        {
            Path.GetFullPath(value);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new UsageException($"{option} is not a valid path: '{value}'.");
        }
    }

    /// <summary>A command with no options or positional arguments: anything after its name is an error.</summary>
    private static void RejectArguments(ConsoleAppContext context)
    {
        if (context.CommandArguments is { Length: > 0 } rest)
            throw new UsageException($"Argument '{rest[0]}' is not recognized.");
    }

    /// <summary>The first word after the command name, which a catch-all command reports as unknown; null when there is none.</summary>
    private static string? FirstArgument(ConsoleAppContext context) => context.CommandArguments is { Length: > 0 } rest ? rest[0] : null;

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
    private static T ParseChoice<T>(string what, string value) where T : struct, Enum
    {
        var names = Enum.GetNames<T>();
        var valid = string.Join(", ", names.Select(name => name.ToLowerInvariant()));
        var match = names.FirstOrDefault(name => string.Equals(name, value, StringComparison.OrdinalIgnoreCase))
            ?? throw new UsageException($"Invalid {what} '{value}'. Valid values: {valid}.");
        return Enum.Parse<T>(match);
    }

    /// <summary>What one request for the status report found on the port.</summary>
    private abstract record Probe
    {
        /// <summary>The service answered with its report.</summary>
        public sealed record Answered(StatusReport Report) : Probe;

        /// <summary>The service answered and refused the request; the message says why.</summary>
        public sealed record Refused(string Message) : Probe;

        /// <summary>Nothing the service answers: the connection was refused or timed out, or another program holds the port.</summary>
        public sealed record NotAnswered(bool OtherProgram) : Probe;
    }
}
