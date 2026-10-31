using System.Text;
using System.Text.RegularExpressions;
using TacitMemory.Core.Models;
using TacitMemory.Core.Tools;

namespace TacitMemory.Core.Decisions;

/// <summary>One piece of text a write would store, and where it goes, for example "title" or "section Steps".</summary>
internal sealed record WrittenText(string Where, string Text);

/// <summary>
/// Keeps secrets out of the memory repository. Common credential formats are refused by pattern; text the patterns
/// pass goes to the decision model as a true-or-false question, and a secret it is sure of or suspects is refused too.
/// Without the decision model every write needs the agent's confirmation.
/// </summary>
internal sealed partial class CredentialCheck(TacitConfig config, IDecisionModel decision)
{
    private const string Instructions =
        "The state is a Chinese text that may be stored in a personal memory. Does it contain a real credential or secret "
        + "value (password, API key, access token, session value, private key, recovery code, or similar) that must not be "
        + "stored? Placeholders, example values, hashes, commit SHAs, UUIDs, identifiers and descriptions of password "
        + "processes are not secrets. Answer high if a real secret is present, low otherwise.";

    private const string WhenTrue = "The text contains a real secret value that should not be stored.";

    private const string WhenFalse = "The text contains no real secret value; it only has placeholders, hashes, identifiers, examples or descriptions.";

    /// <summary>Characters of a suspicious fragment shown back to the agent; the rest is never echoed.</summary>
    private const int ShownLength = 6;

    private static readonly (string Kind, Regex Pattern)[] Patterns =
    [
        ("API key", ApiKey()),
        ("GitHub token", GitHubToken()),
        ("AWS access key", AwsKey()),
        ("private key", PrivateKey()),
        ("password", Password()),
        ("long base64 value", Base64()),
    ];

    /// <param name="op">The <c>write_page</c> op, named in the next step.</param>
    /// <returns>The refusal, or <c>null</c> when the write may go ahead.</returns>
    public async Task<ToolResult?> RunAsync(IReadOnlyList<WrittenText> texts, string op, CancellationToken cancellationToken)
    {
        var retry = $"call write_page op={op} again with confirm_no_secret=true";
        var found = Find(texts);
        if (found.Count > 0)
            return ToolText.Refuse(
                found,
                $"Remove the secret and write again. If it is not a real secret (a placeholder, an example, a hash), make sure with the user, then {retry}.");

        var probability = await decision.IsTrueAsync(State(texts), Instructions, WhenTrue, WhenFalse, cancellationToken);
        if (probability is null)
            return ToolText.Refuse(
                "The decision model is unavailable, so this write could only be checked for common credential formats.",
                $"Make sure it holds no password, key, token or other secret, then {retry}.");

        return Judgment.TrueOrFalse(probability.Value, config.Decision.Credential ?? Judgment.Credential) == Tier.Unlikely
            ? null
            : ToolText.Refuse(
                "This write may contain a secret (a password, key, token or similar).",
                $"Remove any secret and write again. If there is none, make sure with the user, then {retry}.");
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

    /// <summary>The write as the decision model reads it: English labels, the text as written.</summary>
    private static string State(IReadOnlyList<WrittenText> texts)
    {
        var state = new StringBuilder();
        foreach (var text in texts)
            state.Append('[').Append(text.Where).Append("]\n").Append(text.Text.Trim()).Append("\n\n");
        return state.ToString().TrimEnd();
    }

    [GeneratedRegex(@"\bsk-[A-Za-z0-9_-]{20,}")]
    private static partial Regex ApiKey();

    [GeneratedRegex(@"\b(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,})")]
    private static partial Regex GitHubToken();

    [GeneratedRegex(@"\b(?:AKIA|ASIA)[0-9A-Z]{16}\b")]
    private static partial Regex AwsKey();

    [GeneratedRegex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----")]
    private static partial Regex PrivateKey();

    /// <summary>A value assigned to a password; placeholders such as <c>&lt;...&gt;</c>, <c>${...}</c> or <c>***</c> are left alone.</summary>
    [GeneratedRegex(@"(?i)\b(?:password|passwd|pwd)\s*=\s*['""]?(?![<{$%*])[^\s'""]+")]
    private static partial Regex Password();

    /// <summary>
    /// 40 or more base64 characters mixing upper case, lower case and digits; hex hashes have no upper case and paths
    /// break at dots.
    /// </summary>
    [GeneratedRegex(@"(?<![A-Za-z0-9+/_-])(?=(?:[A-Za-z0-9+/_-]*[A-Z]){3})(?=(?:[A-Za-z0-9+/_-]*[a-z]){3})(?=(?:[A-Za-z0-9+/_-]*[0-9]){3})[A-Za-z0-9+/_-]{40,}={0,2}")]
    private static partial Regex Base64();
}
