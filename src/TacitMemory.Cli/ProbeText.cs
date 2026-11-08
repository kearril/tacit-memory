using System.Globalization;
using TacitMemory.Core;

namespace TacitMemory.Cli;

/// <summary>One line of the probe report and the level that colours it when the terminal is styled; <c>null</c> leaves it plain.</summary>
internal readonly record struct ProbeLine(string Text, StatusLevel? Level);

/// <summary>
/// The text of <c>tacit probe</c> in the memory language, and its exit code. It only reads results and builds lines; the
/// command prints them. Response bodies are shown on one line with every control character replaced, so the plain text
/// carries no control characters.
/// </summary>
internal static class ProbeText
{
    private static readonly Bilingual Note = new(
        "以下结果测试的是 config.yaml 中的配置。运行中的服务仍用启动时的配置，修改后需重启服务。",
        "These results test the configuration in config.yaml. A running service keeps the configuration it started with; restart it after a change.");

    private static readonly Bilingual Succeeded = new("{0}：成功", "{0}: succeeded");
    private static readonly Bilingual NotConfiguredExplicit = new(
        "{0}：未配置，config.yaml 中没有 models.{1}", "{0}: not configured; config.yaml has no models.{1}");
    private static readonly Bilingual NotConfiguredSkipped = new("{0}：未配置，跳过", "{0}: not configured, skipped");
    private static readonly Bilingual FailedHttp = new("{0}：失败，HTTP {1}，耗时 {2} 毫秒", "{0}: failed, HTTP {1}, {2} ms");
    private static readonly Bilingual FailedFormat = new(
        "{0}：失败，响应格式不对（HTTP {1}），耗时 {2} 毫秒", "{0}: failed, the response format is wrong (HTTP {1}), {2} ms");
    private static readonly Bilingual FailedNetwork = new("{0}：失败，网络错误，耗时 {1} 毫秒", "{0}: failed, network error, {1} ms");
    private static readonly Bilingual FailedTimeout = new("{0}：失败，超时未应答，耗时 {1} 毫秒", "{0}: failed, timed out with no answer, {1} ms");
    private static readonly Bilingual BodyEmpty = new("  响应体为空", "  The response body is empty.");
    private static readonly Bilingual BodyText = new(
        "  响应体前 500 个字符：{0}", "  The first 500 characters of the response body: {0}");

    private static readonly Bilingual Embedding = new("嵌入模型（embedding）", "embedding model");
    private static readonly Bilingual Reranker = new("重排模型（reranker）", "reranker model");
    private static readonly Bilingual Decision = new("决策模型（decision）", "decision model");

    /// <summary>The report: the note, then one or more lines for each result, in the order given.</summary>
    public static IReadOnlyList<ProbeLine> Lines(MemoryLanguage language, IReadOnlyList<ProbeResult> results, bool explicitTargets)
    {
        var lines = new List<ProbeLine> { new(Note.In(language), null) };
        foreach (var result in results)
            lines.AddRange(Describe(language, result, explicitTargets));
        return lines;
    }

    /// <summary>
    /// 1 when a model failed, or when a model the user named is not configured; 0 otherwise. An unconfigured model that
    /// was not named is skipped and is not a failure.
    /// </summary>
    public static int ExitCode(IReadOnlyList<ProbeResult> results, bool explicitTargets) =>
        results.Any(result => result.Outcome == ProbeOutcome.Failed || (explicitTargets && result.Outcome == ProbeOutcome.Unconfigured)) ? 1 : 0;

    private static IEnumerable<ProbeLine> Describe(MemoryLanguage language, ProbeResult result, bool explicitTargets)
    {
        var name = NameOf(result.Role).In(language);
        var key = result.Role.ToString().ToLowerInvariant();
        var milliseconds = (int)Math.Round(result.Elapsed.TotalMilliseconds);

        switch (result.Outcome)
        {
            case ProbeOutcome.Succeeded:
                yield return new ProbeLine(Format(Succeeded, language, name), StatusLevel.Ok);
                yield break;

            case ProbeOutcome.Unconfigured when explicitTargets:
                yield return new ProbeLine(Format(NotConfiguredExplicit, language, name, key), StatusLevel.Problem);
                yield break;

            case ProbeOutcome.Unconfigured:
                yield return new ProbeLine(Format(NotConfiguredSkipped, language, name), null);
                yield break;

            default:
                yield return new ProbeLine(FailureLine(language, name, result, milliseconds), StatusLevel.Problem);
                if (result.Failure is ProbeFailure.HttpStatus or ProbeFailure.WrongFormat)
                    yield return new ProbeLine(BodyLine(language, result.ResponseBody), null);
                yield break;
        }
    }

    private static string FailureLine(MemoryLanguage language, string name, ProbeResult result, int milliseconds) => result.Failure switch
    {
        ProbeFailure.HttpStatus => Format(FailedHttp, language, name, result.StatusCode, milliseconds),
        ProbeFailure.WrongFormat => Format(FailedFormat, language, name, result.StatusCode, milliseconds),
        ProbeFailure.Network => Format(FailedNetwork, language, name, milliseconds),
        ProbeFailure.Timeout => Format(FailedTimeout, language, name, milliseconds),
        _ => Format(FailedNetwork, language, name, milliseconds),
    };

    /// <summary>The body on one line, control characters replaced, or the note that it is empty.</summary>
    private static string BodyLine(MemoryLanguage language, string? body)
    {
        var plain = new string((body ?? "").Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
        return plain.Length == 0 ? BodyEmpty.In(language) : Format(BodyText, language, plain);
    }

    private static Bilingual NameOf(ModelRole role) => role switch
    {
        ModelRole.Embedding => Embedding,
        ModelRole.Reranker => Reranker,
        _ => Decision,
    };

    private static string Format(Bilingual text, MemoryLanguage language, params object?[] args) =>
        string.Format(CultureInfo.InvariantCulture, text.In(language), args);

    /// <summary>A sentence in both memory languages.</summary>
    private sealed record Bilingual(string Zh, string En)
    {
        public string In(MemoryLanguage language) => language switch
        {
            MemoryLanguage.Zh => Zh,
            MemoryLanguage.En => En,
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, null),
        };
    }
}
