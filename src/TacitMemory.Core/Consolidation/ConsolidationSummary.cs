using TacitMemory.Core.Git;
using TacitMemory.Core.Injection;
using TacitMemory.Core.Persona;
using TacitMemory.Core.Recent;
using TacitMemory.Core.Workstreams;

namespace TacitMemory.Core.Consolidation;

/// <summary>
/// The numbers the consolidation reminder and <c>tacit status</c> both show: the last consolidation, and the pending counts. Every
/// count comes from the report's categories that need no model, so reading the summary never calls one.
/// </summary>
/// <param name="Baseline">The last consolidation, or the init commit standing in for it, or none.</param>
/// <param name="DaysSince">Days from the baseline's commit to today; <c>null</c> when there is no baseline commit.</param>
/// <param name="CandidatesReadable">Whether <c>candidates.md</c> is valid UTF-8; the candidate count is zero otherwise.</param>
/// <param name="Candidates">The candidates in <c>candidates.md</c>; zero when the file is not readable.</param>
/// <param name="Problems">Format and rule problems.</param>
/// <param name="WorkstreamEmpty">Whether no workstream is established.</param>
/// <param name="UnsummarizedWeeks">Completed ISO weeks that have activities and no summary line yet.</param>
internal sealed record ConsolidationSummary(
    ConsolidationBaseline Baseline,
    int? DaysSince,
    bool CandidatesReadable,
    int Candidates,
    int Problems,
    bool WorkstreamEmpty,
    int UnsummarizedWeeks)
{
    /// <summary>
    /// Reads the repository. Throws when git cannot read it, so each caller decides what that means: the reminder gives
    /// none, the status report says why.
    /// </summary>
    /// <exception cref="TacitException">The repository's history cannot be read.</exception>
    /// <exception cref="IOException">A file the counts read is held open by another program.</exception>
    public static async Task<ConsolidationSummary> ReadAsync(TacitConfig config, DateOnly today, CancellationToken cancellationToken)
    {
        var git = new GitCli(config.MemoryPath, config.Language);
        var baseline = await ConsolidationBaseline.ReadAsync(git, cancellationToken);
        var context = new ConsolidationContext(config, today, git, baseline);
        // Candidates are counted without a model: every candidate in the file counts.
        var candidates = TextFile.Read(Path.Combine(config.MemoryPath, PersonaFiles.Candidates));
        var candidateCount = candidates is { } read ? CandidatesDocument.Parse(read.Text).Candidates.Count : 0;
        var problems = FormatViolations.Issues(context).Count;
        var workstreamEmpty = WorkstreamFile.Established(config.MemoryPath).Count == 0;
        var unsummarizedWeeks = SummaryCalendar.Unsummarized(config.MemoryPath, today).Count(period => period.Kind == SummaryKind.Week);

        var days = baseline.Commit is { } commit ? today.DayNumber - commit.Day.DayNumber : (int?)null;
        return new ConsolidationSummary(baseline, days, candidates is not null, candidateCount, problems, workstreamEmpty, unsummarizedWeeks);
    }
}
