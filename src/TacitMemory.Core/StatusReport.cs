using System.Text.Json.Serialization;

namespace TacitMemory.Core;

/// <summary>
/// What <c>tacit status</c> reports, one record per column. A column the report does not carry is <c>null</c>, so a
/// report from a stopped service simply leaves out the columns that need the service. A new column is a new record
/// with a property here; the CLI prints every column it finds, in the order it lists them.
/// </summary>
public sealed record StatusReport
{
    /// <summary>The configuration errors, present only when <c>config.yaml</c> does not load.</summary>
    public ConfigStatus? Config { get; init; }

    /// <summary>Whether the service runs, and if so where, which version, and for how long.</summary>
    public ServiceStatus? Service { get; init; }

    /// <summary>The auxiliary models' states. When the service runs, every model; when it does not, only the unconfigured ones.</summary>
    public ModelsStatus? Models { get; init; }

    /// <summary>The clients the service has seen, newest first, with what each declared. Read from the state file, so it is shown with the service stopped too.</summary>
    public ClientsStatus? Clients { get; init; }

    /// <summary>Each client binding and whether its files are still in place.</summary>
    public BindingsStatus? Bindings { get; init; }

    /// <summary>When the last consolidation was, and how many days ago.</summary>
    public ConsolidationStatus? Consolidation { get; init; }

    /// <summary>The counts a consolidation would raise: candidates, format problems, an empty workstream.</summary>
    public PendingStatus? Pending { get; init; }

    /// <summary>The latest local backup and the cloud backup record.</summary>
    public BackupsStatus? Backups { get; init; }

    /// <summary>Where the service writes its logs.</summary>
    public LogsStatus? Logs { get; init; }

    /// <summary>Where each secret's value came from. Never the value.</summary>
    public SecretsStatus? Secrets { get; init; }

    /// <summary>The columns the report carries, in the order the CLI lists them. Not part of the reply: it is derived from the columns above.</summary>
    [JsonIgnore]
    public IReadOnlyList<IStatusColumn> Columns =>
        new IStatusColumn?[] { Config, Service, Models, Clients, Bindings, Consolidation, Pending, Backups, Logs, Secrets }.OfType<IStatusColumn>().ToList();
}

/// <summary>How a line of a status column stands. The CLI colours it: ok green, attention yellow, problem red.</summary>
public enum StatusLevel
{
    Ok,
    Attention,
    Problem,
}

/// <summary>One column of the status report: its title and its lines, both already in the memory language.</summary>
public interface IStatusColumn
{
    string Title { get; }

    IReadOnlyList<string> Lines { get; }

    /// <summary>How each line stands, in the order of <see cref="Lines"/>; <c>null</c> leaves a line uncoloured.</summary>
    IReadOnlyList<StatusLevel?> LineLevels() => new StatusLevel?[Lines.Count];
}

/// <summary>The configuration errors: one line per wrong key, with its key path.</summary>
public sealed record ConfigStatus(string Title, IReadOnlyList<string> Lines) : IStatusColumn
{
    /// <summary>Every line of the configuration errors is a problem.</summary>
    public IReadOnlyList<StatusLevel?> LineLevels() => Lines.Select(_ => (StatusLevel?)StatusLevel.Problem).ToList();
}

/// <param name="Running">Whether a service answered the status request.</param>
/// <param name="Endpoint">The MCP endpoint of the running service; <c>null</c> when it does not run.</param>
/// <param name="Version">The running service's version; <c>null</c> when it does not run.</param>
/// <param name="UptimeSeconds">How long the service has run; <c>null</c> when it does not run.</param>
/// <param name="OtherProgramAnswered">Another program answered on the configured port, so the service is not running.</param>
public sealed record ServiceStatus(
    string Title,
    IReadOnlyList<string> Lines,
    bool Running,
    Uri? Endpoint,
    string? Version,
    long? UptimeSeconds,
    bool OtherProgramAnswered) : IStatusColumn
{
    /// <summary>Every line of the service column is ok while the service runs, and attention while it does not.</summary>
    public IReadOnlyList<StatusLevel?> LineLevels() => Lines.Select(_ => (StatusLevel?)(Running ? StatusLevel.Ok : StatusLevel.Attention)).ToList();
}

/// <summary>The auxiliary models, one line each; <paramref name="Items"/> carries the same states as data. Offline, only the unconfigured models are listed.</summary>
public sealed record ModelsStatus(string Title, IReadOnlyList<string> Lines, IReadOnlyList<ModelStatus> Items) : IStatusColumn
{
    /// <summary>A model line is ok while the model is available and attention while it cools down; an unconfigured model and a line without an item are plain.</summary>
    public IReadOnlyList<StatusLevel?> LineLevels() => Lines.Select((_, index) => index < Items.Count ? LevelOf(Items[index].State) : null).ToList();

    private static StatusLevel? LevelOf(ModelState state) => state switch
    {
        ModelState.Available => StatusLevel.Ok,
        ModelState.CoolingDown => StatusLevel.Attention,
        _ => null,
    };
}

/// <param name="Role">The model's role name in <c>config.yaml</c>: <c>embedding</c>, <c>reranker</c> or <c>decision</c>.</param>
/// <param name="State">Available, cooling down after a failed request, or unconfigured.</param>
/// <param name="CoolingSeconds">Seconds left in the cooldown, rounded up; <c>null</c> unless cooling down.</param>
public sealed record ModelStatus(string Role, ModelState State, int? CoolingSeconds);

public enum ModelState
{
    /// <summary>Called as usual.</summary>
    Available,

    /// <summary>A request finally failed; the model is not called until the cooldown ends.</summary>
    CoolingDown,

    /// <summary>Not configured in <c>config.yaml</c>: the model is never called and never fails.</summary>
    Unconfigured,
}

/// <summary>The bindings, one line each; <paramref name="Items"/> carries the same checks as data.</summary>
public sealed record BindingsStatus(string Title, IReadOnlyList<string> Lines, IReadOnlyList<BindingCheck> Items) : IStatusColumn
{
    /// <summary>A binding line is ok when its binding has no problem, and a problem when it has one; a line without an item is plain.</summary>
    public IReadOnlyList<StatusLevel?> LineLevels() => Lines.Select((_, index) => index < Items.Count
        ? (StatusLevel?)(Items[index].Problem is null ? StatusLevel.Ok : StatusLevel.Problem)
        : null).ToList();
}

/// <param name="Client">The client's name, as <c>bindings.json</c> records it.</param>
/// <param name="ContextFile">The context file the resident block goes into.</param>
/// <param name="SkillsFolder">The folder the skill links go into.</param>
/// <param name="Problem">What is wrong with the binding; <c>null</c> when it is healthy.</param>
/// <param name="SkillLink">The skill link that is missing or points elsewhere, when <paramref name="Problem"/> says so.</param>
/// <param name="SharedWith">The other bindings that use one of this binding's paths; empty when none does.</param>
public sealed record BindingCheck(
    string Client,
    string ContextFile,
    string SkillsFolder,
    BindingProblem? Problem,
    string? SkillLink,
    IReadOnlyList<string> SharedWith);

/// <summary>The ways a binding can be broken, checked in this order; the first one found is reported.</summary>
public enum BindingProblem
{
    /// <summary>The context file is not there.</summary>
    ContextFileMissing,

    /// <summary>The context file is not valid UTF-8, so the block cannot be read.</summary>
    ContextFileNotUtf8,

    /// <summary>The context file could not be read at all, for example because another program holds it.</summary>
    ContextFileUnreadable,

    /// <summary>The block's begin and end markers are missing, unpaired, or repeated.</summary>
    MarkersIncomplete,

    /// <summary>One of the client's skill links is missing or does not point at its tacit skill.</summary>
    SkillLinkNotTacit,
}

/// <param name="LastConsolidationDay">The day of the last consolidation, or of the init commit standing in for it; <c>null</c> when there is none, or the history cannot be read.</param>
/// <param name="DaysSince">Days from that day to today; <c>null</c> together with <paramref name="LastConsolidationDay"/>.</param>
public sealed record ConsolidationStatus(string Title, IReadOnlyList<string> Lines, DateOnly? LastConsolidationDay, int? DaysSince) : IStatusColumn;

/// <param name="Candidates">Candidates in <c>candidates.md</c>; <c>null</c> when <c>candidates.md</c> is not valid UTF-8 or the history cannot be read.</param>
/// <param name="FormatProblems">Format and rule problems; <c>null</c> when the history cannot be read.</param>
/// <param name="WorkstreamEmpty">Whether the workstream has no items; <c>null</c> when the history cannot be read.</param>
public sealed record PendingStatus(string Title, IReadOnlyList<string> Lines, int? Candidates, int? FormatProblems, bool? WorkstreamEmpty, int? UnsummarizedWeeks = null) : IStatusColumn
{
    /// <summary>
    /// The lines are the candidate count, the format problems, the workstream and the weeks to summarize, in that order. No count
    /// means the history or the file could not be read, which is a problem; a count above zero, or an empty workstream, needs attention.
    /// </summary>
    public IReadOnlyList<StatusLevel?> LineLevels() =>
        new StatusLevel?[] { CountLevel(Candidates), CountLevel(FormatProblems), WorkstreamLevel(WorkstreamEmpty), CountLevel(UnsummarizedWeeks) }.Take(Lines.Count).ToList();

    private static StatusLevel CountLevel(int? count) => count is null ? StatusLevel.Problem : count > 0 ? StatusLevel.Attention : StatusLevel.Ok;

    private static StatusLevel WorkstreamLevel(bool? empty) => empty is null ? StatusLevel.Problem : empty.Value ? StatusLevel.Attention : StatusLevel.Ok;
}

/// <param name="Latest">The file name of the newest local backup of any kind; <c>null</c> when there is none.</param>
/// <param name="LatestAt">When that file was last written.</param>
/// <param name="LastRemote">The last attempt at the cloud backup; <c>null</c> when there was none, or it could not be read.</param>
public sealed record BackupsStatus(string Title, IReadOnlyList<string> Lines, string? Latest, DateTimeOffset? LatestAt, CloudBackupRecord? LastRemote) : IStatusColumn
{
    /// <summary>The cloud line is ok after a success and a problem after a failure; the local line and a missing record stay plain.</summary>
    public IReadOnlyList<StatusLevel?> LineLevels() => Lines.Select((_, index) => index == 1 && LastRemote is { } last
        ? (StatusLevel?)(last.Succeeded ? StatusLevel.Ok : StatusLevel.Problem)
        : null).ToList();
}

/// <summary>Where each secret's value came from, one line each; <paramref name="Items"/> carries the same as data.</summary>
public sealed record SecretsStatus(string Title, IReadOnlyList<string> Lines, IReadOnlyList<SecretStatus> Items) : IStatusColumn;

/// <param name="Key">The config key the secret belongs to, such as <c>models.decision.apiKey</c>.</param>
/// <param name="Variable">The environment variable that overrides it.</param>
/// <param name="Source">Where the value came from; <c>null</c> when neither place holds one.</param>
/// <param name="Overridden">The environment variable holds the value and <c>config.yaml</c> also holds one that is ignored.</param>
public sealed record SecretStatus(string Key, string Variable, SecretSource? Source, bool Overridden);

/// <param name="Directory">The directory holding the service's daily log files.</param>
public sealed record LogsStatus(string Title, IReadOnlyList<string> Lines, string Directory) : IStatusColumn;

/// <summary>The recorded clients, one line each; <paramref name="Items"/> carries the same clients as data. Plain lines: a sighting is information, not a problem.</summary>
public sealed record ClientsStatus(string Title, IReadOnlyList<string> Lines, IReadOnlyList<ClientStatus> Items) : IStatusColumn;

/// <summary>
/// One client as the service last saw it: the name and version it declared, the day of its last request, the protocol revision
/// it used, and the capabilities and extensions it declared then. Shown as it was written, with control characters removed.
/// </summary>
/// <param name="LastSeen">The local day of the last request from this client, not the time of day.</param>
public sealed record ClientStatus(
    string Name,
    string Version,
    DateOnly LastSeen,
    string ProtocolVersion,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> Extensions);
