using System.Globalization;
using TacitMemory.Core.Configuration;
using TacitMemory.Core.Consolidation;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Logging;
using TacitMemory.Core.Models;
using TacitMemory.Core.Recent;
using TacitMemory.Core.Status;
using TacitMemory.Core.Upkeep;

namespace TacitMemory.Core;

/// <summary>
/// What the service reports for the dashboard and the logs page: the status report, the activity heatmap, whether a
/// consolidation is due, the service log and the probe of the auxiliary models. Each reads files, the repository's history
/// or the models only, changes nothing, and does not wait for a maintenance operation.
/// </summary>
public sealed class ServiceReports
{
    private readonly TacitOptions _options;
    private readonly DateTimeOffset _startedAt;
    private readonly DerivedUpkeep _upkeep;
    private readonly Func<ReportRuntime> _runtime;
    private readonly Func<ExtraListenerStatus?> _extraListener;

    internal ServiceReports(
        TacitOptions options, DateTimeOffset startedAt, DerivedUpkeep upkeep, Func<ReportRuntime> runtime, Func<ExtraListenerStatus?> extraListener)
    {
        _options = options;
        _startedAt = startedAt;
        _upkeep = upkeep;
        _runtime = runtime;
        _extraListener = extraListener;
    }

    private MemoryLanguage Language => _runtime().Config.Language;

    /// <summary>
    /// The status report of this running service, reachable at <paramref name="endpoint"/>: its version, uptime, the
    /// auxiliary models' states and the shared columns. Calls no model and reads no network.
    /// </summary>
    public Task<StatusReport> StatusAsync(Uri endpoint, CancellationToken cancellationToken = default)
    {
        var runtime = _runtime();
        var uptime = _options.Time.GetUtcNow() - _startedAt;
        return StatusReporter.RunningAsync(
            _options, runtime.Config, endpoint, ProductVersion.Current, uptime < TimeSpan.Zero ? TimeSpan.Zero : uptime, runtime.Models.Statuses,
            _extraListener(), PromptProblems(), cancellationToken);
    }

    /// <summary>Each prompt document whose file the service does not run, in the order of <see cref="PromptName"/>, with the refusal.</summary>
    private IReadOnlyList<PromptProblem> PromptProblems()
    {
        var problems = _upkeep.PromptState.Problems;
        return [.. Enum.GetValues<PromptName>().Where(problems.ContainsKey).Select(name => new PromptProblem(name, problems[name]))];
    }

    /// <summary>
    /// The activity log's counts per day over the last 53 weeks, for the dashboard's heatmap, with the consolidation days in
    /// that window. Reads files and the repository's history only.
    /// </summary>
    public Task<ActivityReport> ActivityAsync(CancellationToken cancellationToken = default) =>
        ActivityCounts.ReadAsync(_runtime().Config, _options.Today, cancellationToken);

    /// <summary>
    /// Whether a consolidation is due, by the rule of the consolidation reminder. Computed when asked from files and the
    /// repository's history; calls no model.
    /// </summary>
    /// <exception cref="TacitException">The repository's history cannot be read.</exception>
    public async Task<ConsolidationDue> ConsolidationDueAsync(CancellationToken cancellationToken = default)
    {
        var config = _runtime().Config;
        var summary = await ConsolidationSummary.ReadAsync(config, _options.Today, cancellationToken);
        return new ConsolidationDue(summary.IsDue(config.Consolidation));
    }

    /// <summary>The days the service log has a file for, newest first, and today. Reads the directory only.</summary>
    public ServiceLogDays LogDays() => new(_options.Today, ServiceLogReader.Days(_options.TacitHome));

    /// <summary>
    /// The events of one day's service log, oldest first, read from the file the service may still be writing to. Today
    /// without a file has no events yet. With <paramref name="after"/>, only the events past that many are returned, so a
    /// reader that follows the log asks for what is new; the events of a file never move, as it is only appended to.
    /// </summary>
    /// <param name="date">The day as <c>yyyy-MM-dd</c>.</param>
    /// <param name="after">How many events the caller already has, as written in the request; none means 0.</param>
    /// <exception cref="TacitException">
    /// <see cref="TacitError.RequestInvalid"/> when <paramref name="date"/> is not a date or <paramref name="after"/> not a
    /// count; <see cref="TacitError.LogDateNotFound"/> for another day without a file.
    /// </exception>
    public ServiceLogDay ServiceLog(string date, string? after = null)
    {
        var language = Language;
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            throw new TacitException(TacitError.RequestInvalid, RequestMessages.LogDateInvalid.Format(language, date));
        var skip = 0;
        if (after is not null && !int.TryParse(after, NumberStyles.None, CultureInfo.InvariantCulture, out skip))
            throw new TacitException(TacitError.RequestInvalid, RequestMessages.LogAfterInvalid.Format(language, after));
        var today = _options.Today;
        var entries = ServiceLogReader.Read(_options.TacitHome, day)
            ?? (day == today ? [] : throw new TacitException(TacitError.LogDateNotFound, RequestMessages.LogDateNotFound.Format(language, date)));
        return new ServiceLogDay(day, today, [.. entries.Skip(skip)], entries.Count);
    }

    /// <summary>
    /// Probes the three auxiliary models as the saved <c>config.yaml</c> configures them, which is what <c>tacit probe</c>
    /// reads, which is also what the service runs once a save has reloaded it. An unconfigured model is reported and skipped.
    /// No model's health changes.
    /// </summary>
    /// <exception cref="TacitException">The saved configuration cannot be loaded.</exception>
    public async Task<ProbeReport> ProbeAsync(CancellationToken cancellationToken = default)
    {
        var config = ConfigLoader.Read(_options);
        var results = await ModelProbe.RunAsync(config, [.. ModelRoles.All], _options.ModelClock, cancellationToken);
        return new ProbeReport(results, ProbeLines.Build(config.Language, results, explicitTargets: false));
    }
}

/// <summary>The parts of the running memory the reports read; a reload replaces them.</summary>
internal sealed record ReportRuntime(TacitConfig Config, AuxiliaryModels Models);
