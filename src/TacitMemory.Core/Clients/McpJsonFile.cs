using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using TacitMemory.Core.Localization;

namespace TacitMemory.Core.Clients;

/// <summary>
/// Adds or removes one entry under <c>mcpServers</c> in a client's MCP JSON config. Other servers and fields keep
/// their order; the file is rewritten with its own indentation and line endings, and only when something changed.
/// </summary>
internal static partial class McpJsonFile
{
    private static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>Sets <c>mcpServers.&lt;name&gt;</c>; <c>false</c> when it already had exactly that value.</summary>
    public static bool Put(string path, string name, JsonObject server, MemoryLanguage language)
    {
        var (root, text) = Read(path, language);
        if (root["mcpServers"] is not JsonObject servers)
        {
            servers = [];
            root["mcpServers"] = servers;
        }

        if (servers[name] is { } current && JsonNode.DeepEquals(current, server))
            return false;
        servers[name] = server;
        Write(path, root, text);
        return true;
    }

    /// <summary>Removes <c>mcpServers.&lt;name&gt;</c>; <c>false</c> when there was none.</summary>
    public static bool Remove(string path, string name, MemoryLanguage language)
    {
        if (!File.Exists(path))
            return false;
        var (root, text) = Read(path, language);
        if (root["mcpServers"] is not JsonObject servers || !servers.Remove(name))
            return false;
        Write(path, root, text);
        return true;
    }

    private static (JsonObject Root, string Text) Read(string path, MemoryLanguage language)
    {
        if (!File.Exists(path))
            return ([], "");
        var text = File.ReadAllText(path);
        try
        {
            return (JsonNode.Parse(text, documentOptions: ReadOptions) as JsonObject ?? throw new JsonException(), text);
        }
        catch (JsonException e)
        {
            throw new TacitException(TacitError.ClientConfigInvalid, Messages.ClientConfigInvalid.Format(language, path, e.Message), e);
        }
    }

    private static void Write(string path, JsonObject root, string original)
    {
        var newline = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var indent = IndentPattern().Match(original) is { Success: true } match ? match.Groups[1].Value : "  ";
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            NewLine = newline,
            IndentCharacter = indent[0],
            IndentSize = indent.Length,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        var trailing = original.Length == 0 || original.EndsWith('\n') ? newline : "";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, root.ToJsonString(options) + trailing);
    }

    /// <summary>The indentation of the first indented line.</summary>
    [GeneratedRegex(@"\n([ \t]+)\S")]
    private static partial Regex IndentPattern();
}
