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

    /// <summary>The auxiliary models' states. Present only when the service runs.</summary>
    public ModelsStatus? Models { get; init; }

    /// <summary>Each client binding and whether its files are still in place.</summary>
    public BindingsStatus? Bindings { get; init; }

    /// <summary>When the last checkup was, and how many days ago.</summary>
    public CheckupStatus? Checkup { get; init; }

    /// <summary>The counts a checkup would raise: confirmable candidates, format problems, a stale mainline.</summary>
    public PendingStatus? Pending { get; init; }

    /// <summary>The latest local backup and the cloud backup record.</summary>
    public BackupsStatus? Backups { get; init; }

    /// <summary>Where the service writes its logs.</summary>
    public LogsStatus? Logs { get; init; }

    /// <summary>Where each secret's value came from. Never the value.</summary>
    public SecretsStatus? Secrets { get; init; }
}

/// <summary>One column of the status report: its title and its lines, both already in the memory language.</summary>
public interface IStatusColumn
{
    string Title { get; }

    IReadOnlyList<string> Lines { get; }
}

/// <summary>The configuration errors: one line per wrong key, with its key path.</summary>
public sealed record ConfigStatus(string Title, IReadOnlyList<string> Lines) : IStatusColumn;

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
    bool OtherProgramAnswered) : IStatusColumn;

/// <summary>The auxiliary models, one line each; <paramref name="Items"/> carries the same states as data.</summary>
public sealed record ModelsStatus(string Title, IReadOnlyList<string> Lines, IReadOnlyList<ModelStatus> Items) : IStatusColumn;

/// <param name="Role">The model's role name in <c>config.yaml</c>: <c>embedding</c>, <c>reranker</c> or <c>decision</c>.</param>
/// <param name="State">Available, or cooling down after a failed request.</param>
/// <param name="CoolingSeconds">Seconds left in the cooldown, rounded up; <c>null</c> unless cooling down.</param>
public sealed record ModelStatus(string Role, ModelState State, int? CoolingSeconds);

public enum ModelState
{
    /// <summary>Called as usual.</summary>
    Available,

    /// <summary>A request finally failed; the model is not called until the cooldown ends.</summary>
    CoolingDown,
}

/// <summary>The bindings, one line each; <paramref name="Items"/> carries the same checks as data.</summary>
public sealed record BindingsStatus(string Title, IReadOnlyList<string> Lines, IReadOnlyList<BindingCheck> Items) : IStatusColumn;

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

/// <param name="LastCheckupDay">The day of the last checkup, or of the init commit standing in for it; <c>null</c> when there is none, or the history cannot be read.</param>
/// <param name="DaysSince">Days from that day to today; <c>null</c> together with <paramref name="LastCheckupDay"/>.</param>
public sealed record CheckupStatus(string Title, IReadOnlyList<string> Lines, DateOnly? LastCheckupDay, int? DaysSince) : IStatusColumn;

/// <param name="Confirmable">Candidates that can be confirmed; <c>null</c> when <c>candidates.md</c> is not valid UTF-8 or the history cannot be read.</param>
/// <param name="FormatProblems">Format and rule problems; <c>null</c> when the history cannot be read.</param>
/// <param name="MainlineStale">Whether the mainline is stale; <c>null</c> when the history cannot be read.</param>
public sealed record PendingStatus(string Title, IReadOnlyList<string> Lines, int? Confirmable, int? FormatProblems, bool? MainlineStale) : IStatusColumn;

/// <param name="Latest">The file name of the newest local backup of any kind; <c>null</c> when there is none.</param>
/// <param name="LatestAt">When that file was last written.</param>
/// <param name="LastRemote">The last attempt at the cloud backup; <c>null</c> when there was none, or it could not be read.</param>
public sealed record BackupsStatus(string Title, IReadOnlyList<string> Lines, string? Latest, DateTimeOffset? LatestAt, CloudBackupRecord? LastRemote) : IStatusColumn;

/// <summary>Where each secret's value came from, one line each; <paramref name="Items"/> carries the same as data.</summary>
public sealed record SecretsStatus(string Title, IReadOnlyList<string> Lines, IReadOnlyList<SecretStatus> Items) : IStatusColumn;

/// <param name="Key">The config key the secret belongs to, such as <c>models.decision.apiKey</c>.</param>
/// <param name="Variable">The environment variable that overrides it.</param>
/// <param name="Source">Where the value came from; <c>null</c> when neither place holds one.</param>
/// <param name="Overridden">The environment variable holds the value and <c>config.yaml</c> also holds one that is ignored.</param>
public sealed record SecretStatus(string Key, string Variable, SecretSource? Source, bool Overridden);

/// <param name="Directory">The directory holding the service's daily log files.</param>
public sealed record LogsStatus(string Title, IReadOnlyList<string> Lines, string Directory) : IStatusColumn;
