using System.Text.RegularExpressions;
using TacitMemory.Core.Tools;

namespace TacitMemory.Core.Decisions;

/// <summary>One piece of text a write would store, and where it goes, for example "title" or "section Steps".</summary>
internal sealed record WrittenText(string Where, string Text);

/// <summary>
/// Keeps secrets out of the memory repository by refusing common credential formats. Only local patterns are used, so
/// the text a write would store never leaves this machine for the check.
/// </summary>
internal static partial class CredentialCheck
{
    /// <summary>Characters of a suspicious fragment shown back to the agent; the rest is never echoed.</summary>
    private const int ShownLength = 6;

    private static readonly (string Kind, Regex Pattern)[] Patterns =
    [
        ("API key", ApiKey()),
        ("GitHub token", GitHubToken()),
        ("AWS access key", AwsKey()),
        ("service token", ServiceToken()),
        ("JWT", Jwt()),
        ("private key", PrivateKey()),
        ("credentials in a URL", UrlCredentials()),
        ("bearer token", Bearer()),
        ("password", Password()),
        ("assigned secret", AssignedSecret()),
        ("long base64 value", Base64()),
    ];

    /// <param name="op">The <c>write_page</c> op, named in the next step.</param>
    /// <returns>The refusal, or <c>null</c> when the write may go ahead.</returns>
    public static ToolResult? Run(IReadOnlyList<WrittenText> texts, string op)
    {
        var found = Find(texts);
        return found.Count == 0
            ? null
            : ToolText.Refuse(
                found,
                "Remove the secret and write again. If it is not a real secret (a placeholder, an example, a hash), make sure "
                + $"with the user, then call write_page op={op} again with confirm_no_secret=true.");
    }

    /// <summary>
    /// One line per pattern hit: what it looks like, where it is, and its first characters. A fragment matched by an
    /// earlier, more specific pattern is not reported again by a later one.
    /// </summary>
    private static List<string> Find(IReadOnlyList<WrittenText> texts)
    {
        var found = new List<string>();
        foreach (var text in texts)
        {
            var covered = new List<(int Start, int End)>();
            foreach (var (kind, pattern) in Patterns)
            {
                foreach (Match match in pattern.Matches(text.Text))
                {
                    var end = match.Index + match.Length;
                    if (covered.Any(span => match.Index < span.End && span.Start < end))
                        continue;
                    covered.Add((match.Index, end));
                    var line = $"Possible {kind} in {text.Where}: {Mask(match.Value)}";
                    if (!found.Contains(line))
                        found.Add(line);
                }
            }
        }

        return found;
    }

    private static string Mask(string fragment) =>
        $"{fragment[..Math.Min(ShownLength, fragment.Length)]}… ({fragment.Length} characters)";

    [GeneratedRegex(@"\bsk-[A-Za-z0-9_-]{20,}")]
    private static partial Regex ApiKey();

    [GeneratedRegex(@"\b(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,})")]
    private static partial Regex GitHubToken();

    [GeneratedRegex(@"\b(?:AKIA|ASIA)[0-9A-Z]{16}\b")]
    private static partial Regex AwsKey();

    [GeneratedRegex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----")]
    private static partial Regex PrivateKey();

    /// <summary>Slack, GitLab, Google, Alibaba Cloud, SendGrid, Docker Hub, Notion and Stripe live keys.</summary>
    [GeneratedRegex(@"\b(?:xox[abprs]-[A-Za-z0-9-]{10,}|glpat-[A-Za-z0-9_-]{20,}|AIza[A-Za-z0-9_-]{35}|LTAI[A-Za-z0-9]{12,}|SG\.[A-Za-z0-9_-]{16,}\.[A-Za-z0-9_-]{16,}|dckr_pat_[A-Za-z0-9_-]{20,}|secret_[A-Za-z0-9]{30,}|[rs]k_live_[A-Za-z0-9]{20,})")]
    private static partial Regex ServiceToken();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]{10,}\.eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}")]
    private static partial Regex Jwt();

    /// <summary>A password in a URL such as <c>scheme://user:pass@host</c>; a placeholder password is left alone.</summary>
    [GeneratedRegex(@"\b[A-Za-z][A-Za-z0-9+.-]*://[^\s:/@]+:(?![<{$%*])[^\s/@]+@")]
    private static partial Regex UrlCredentials();

    /// <summary>A bearer token with at least one digit; <c>Bearer &lt;token&gt;</c> and prose such as "bearer tokens" are left alone.</summary>
    [GeneratedRegex(@"(?i)\bbearer[\s:：]+(?![<{$%*])(?=[A-Za-z0-9._~+/=-]*\d)[A-Za-z0-9._~+/=-]{8,}")]
    private static partial Regex Bearer();

    /// <summary>A value assigned to a password; placeholders such as <c>&lt;...&gt;</c>, <c>${...}</c> or <c>***</c> are left alone.</summary>
    [GeneratedRegex(@"(?i)\b(?:password|passwd|pwd)\s*=\s*['""]?(?![<{$%*])[^\s'""]+")]
    private static partial Regex Password();

    /// <summary>
    /// A value of six or more characters with a digit, assigned with <c>=</c> or <c>:</c> to a key ending in a secret word,
    /// such as <c>mail_pass</c>, <c>access_token</c>, <c>x-api-key</c> or <c>clientSecret</c>. The word must start the key or
    /// follow a separator or a camel-case hump, so <c>bypass</c> is no key; placeholders and <c>xxxx</c> or <c>your…</c> values
    /// are left alone.
    /// </summary>
    [GeneratedRegex(@"(?<![A-Za-z0-9])(?:[A-Za-z0-9]+[_.-])*(?:[A-Za-z0-9]*[a-z](?=[A-Z]))?(?i:password|passwd|pass|pwd|token|secret|api[_-]?key|access[_-]?key|session)['""]?\s*[=:：]\s*['""]?(?![<{$%*]|(?i:x{4}|your))(?=[^\s'""`;,，。]*\d)[^\s'""`;,，。]{6,}")]
    private static partial Regex AssignedSecret();

    /// <summary>
    /// 40 or more base64 characters mixing upper case, lower case and digits; hex hashes have no upper case and paths
    /// break at dots.
    /// </summary>
    [GeneratedRegex(@"(?<![A-Za-z0-9+/_-])(?=(?:[A-Za-z0-9+/_-]*[A-Z]){3})(?=(?:[A-Za-z0-9+/_-]*[a-z]){3})(?=(?:[A-Za-z0-9+/_-]*[0-9]){3})[A-Za-z0-9+/_-]{40,}={0,2}")]
    private static partial Regex Base64();
}
