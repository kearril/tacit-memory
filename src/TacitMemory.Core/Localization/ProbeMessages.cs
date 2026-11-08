namespace TacitMemory.Core.Localization;

/// <summary>The texts of the probe report. Built into lines by <see cref="Models.ProbeLines"/>.</summary>
internal static class ProbeMessages
{
    public static readonly Localized Note = new(
        "以下为对 config.yaml 中配置的模型的探测结果。运行中的服务仍用启动时的配置，修改后需重启服务。",
        "These results probe the models configured in config.yaml. A running service keeps the configuration it started with; restart it after a change.");

    public static readonly Localized Succeeded = new("{0}：成功", "{0}: succeeded");
    public static readonly Localized NotConfiguredExplicit = new(
        "{0}：未配置，config.yaml 中没有 models.{1}", "{0}: not configured; config.yaml has no models.{1}");
    public static readonly Localized NotConfiguredSkipped = new("{0}：未配置，跳过", "{0}: not configured, skipped");
    public static readonly Localized FailedHttp = new("{0}：失败，HTTP {1}，耗时 {2} 毫秒", "{0}: failed, HTTP {1}, {2} ms");
    public static readonly Localized FailedFormat = new(
        "{0}：失败，响应格式不对（HTTP {1}），耗时 {2} 毫秒", "{0}: failed, the response format is wrong (HTTP {1}), {2} ms");
    public static readonly Localized FailedNetwork = new("{0}：失败，网络错误，耗时 {1} 毫秒", "{0}: failed, network error, {1} ms");
    public static readonly Localized FailedTimeout = new("{0}：失败，超时未应答，耗时 {1} 毫秒", "{0}: failed, timed out with no answer, {1} ms");
    public static readonly Localized BodyEmpty = new("  响应体为空", "  The response body is empty.");

    /// <summary>The body line: {0} is the body, {1} the number of characters it keeps.</summary>
    public static readonly Localized BodyText = new(
        "  响应体前 {1} 个字符：{0}", "  The first {1} characters of the response body: {0}");

    /// <summary>The model names, as the probe report shows them: the glossary's names for the roles.</summary>
    public static readonly Localized Embedding = new("嵌入模型", "embedding model");
    public static readonly Localized Reranker = new("reranker", "reranker model");
    public static readonly Localized Decision = new("决策模型", "decision model");
}
