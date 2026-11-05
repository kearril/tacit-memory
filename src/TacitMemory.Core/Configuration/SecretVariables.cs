namespace TacitMemory.Core.Configuration;

/// <summary>
/// The environment variables that carry secrets. Each one, when it holds a non-blank value, overrides the config key of
/// the same secret. <see cref="TacitOptions.FromEnvironment"/> reads them; tests set them through
/// <see cref="TacitOptions.Secrets"/>.
/// </summary>
internal static class SecretVariables
{
    /// <summary>The decision model's key, <c>models.decision.apiKey</c>.</summary>
    public const string DecisionApiKey = "TACIT_DECISION_API_KEY";

    /// <summary>The WebDAV password, <c>backup.webdav.password</c>.</summary>
    public const string WebDavPassword = "TACIT_WEBDAV_PASSWORD";

    public static readonly IReadOnlyList<string> All = [DecisionApiKey, WebDavPassword];
}
