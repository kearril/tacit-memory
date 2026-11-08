using TacitMemory.Core.Localization;

namespace TacitMemory.Core.Models;

/// <summary>
/// Builds the lines of the probe report in the config's language from the results. Response bodies are shown on one line
/// with every control character replaced, so the text carries no control characters.
/// </summary>
internal static class ProbeLines
{
    /// <summary>The report: the note, then one or more lines for each result, in the order given.</summary>
    public static IReadOnlyList<ProbeReportLine> Build(MemoryLanguage language, IReadOnlyList<ProbeResult> results, bool explicitTargets)
    {
        var lines = new List<ProbeReportLine> { new(ProbeMessages.Note.In(language), null) };
        foreach (var result in results)
            lines.AddRange(Describe(language, result, explicitTargets));
        return lines;
    }

    private static IEnumerable<ProbeReportLine> Describe(MemoryLanguage language, ProbeResult result, bool explicitTargets)
    {
        var name = NameOf(result.Role).In(language);
        var key = ModelRoles.Name(result.Role);
        var milliseconds = (int)Math.Round(result.Elapsed.TotalMilliseconds);

        switch (result.Outcome)
        {
            case ProbeOutcome.Succeeded:
                yield return new ProbeReportLine(ProbeMessages.Succeeded.Format(language, name), StatusLevel.Ok);
                yield break;

            case ProbeOutcome.Unconfigured when explicitTargets:
                yield return new ProbeReportLine(ProbeMessages.NotConfiguredExplicit.Format(language, name, key), StatusLevel.Problem);
                yield break;

            case ProbeOutcome.Unconfigured:
                yield return new ProbeReportLine(ProbeMessages.NotConfiguredSkipped.Format(language, name), null);
                yield break;

            default:
                yield return new ProbeReportLine(FailureLine(language, name, result, milliseconds), StatusLevel.Problem);
                if (result.Failure is ProbeFailure.HttpStatus or ProbeFailure.WrongFormat)
                    yield return new ProbeReportLine(BodyLine(language, result.ResponseBody), null);
                yield break;
        }
    }

    private static string FailureLine(MemoryLanguage language, string name, ProbeResult result, int milliseconds) => result.Failure switch
    {
        ProbeFailure.HttpStatus => ProbeMessages.FailedHttp.Format(language, name, result.StatusCode, milliseconds),
        ProbeFailure.WrongFormat => ProbeMessages.FailedFormat.Format(language, name, result.StatusCode, milliseconds),
        ProbeFailure.Timeout => ProbeMessages.FailedTimeout.Format(language, name, milliseconds),
        _ => ProbeMessages.FailedNetwork.Format(language, name, milliseconds),
    };

    /// <summary>The body on one line, control characters replaced, or the note that it is empty.</summary>
    private static string BodyLine(MemoryLanguage language, string? body)
    {
        var plain = new string((body ?? "").Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
        return plain.Length == 0
            ? ProbeMessages.BodyEmpty.In(language)
            : ProbeMessages.BodyText.Format(language, plain, ModelProbe.BodyLimit);
    }

    private static Localized NameOf(ModelRole role) => role switch
    {
        ModelRole.Embedding => ProbeMessages.Embedding,
        ModelRole.Reranker => ProbeMessages.Reranker,
        _ => ProbeMessages.Decision,
    };
}
