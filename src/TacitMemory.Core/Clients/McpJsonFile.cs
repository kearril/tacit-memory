using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using TacitMemory.Core.Injection;
using TacitMemory.Core.Localization;

namespace TacitMemory.Core.Clients;

/// <summary>
/// Adds or removes one entry under <c>mcpServers</c> in a client's MCP JSON config. Other servers and fields keep
/// their order; the file is rewritten with its own indentation, line endings and BOM, and only when something
/// changed. A file with comments is refused, since rewriting would lose them.
/// </summary>
internal static partial class McpJsonFile
{
    private static readonly JsonDocumentOptions Strict = new() { AllowTrailingCommas = true };
    private static readonly JsonDocumentOptions WithComments = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    /// <summary>
    /// Sets <paramref name="fields"/> on <c>mcpServers.&lt;name&gt;</c>, keeping any other fields the user put there;
    /// <c>false</c> when nothing changed.
    /// </summary>
    public static bool Put(string path, string name, JsonObject fields, MemoryLanguage language)
    {
        var (root, text, hasBom) = Read(path, language, fields);
        if (root["mcpServers"] is not JsonObject servers)
        {
            servers = [];
            root["mcpServers"] = servers;
        }

        if (servers[name] is not JsonObject server)
        {
            server = [];
            servers[name] = server;
        }

        var changed = false;
        foreach (var (key, value) in fields)
        {
            if (JsonNode.DeepEquals(server[key], value))
                continue;
            server[key] = value?.DeepClone();
            changed = true;
        }

        if (changed)
            Write(path, root, text, hasBom);
        return changed;
    }

    /// <summary>Removes <c>mcpServers.&lt;name&gt;</c>; returns the remaining root, or <c>null</c> when there was no entry.</summary>
    public static JsonObject? Remove(string path, string name, MemoryLanguage language)
    {
        if (!File.Exists(path))
            return null;
        var (root, text, hasBom) = Read(path, language, null);
        if (root["mcpServers"] is not JsonObject servers || !servers.Remove(name))
            return null;
        Write(path, root, text, hasBom);
        return root;
    }

    private static (JsonObject Root, string Text, bool HasBom) Read(string path, MemoryLanguage language, JsonObject? fields)
    {
        if (TextFile.Read(path) is not var (text, hasBom))
            throw new TacitException(TacitError.FileNotUtf8, Messages.FileNotUtf8.Format(language, path));
        if (text.Length == 0)
            return ([], text, hasBom);
        try
        {
            return (JsonNode.Parse(text, documentOptions: Strict) as JsonObject ?? throw new JsonException(), text, hasBom);
        }
        catch (JsonException) when (Parses(text, WithComments))
        {
            var entry = fields?.ToJsonString() ?? "";
            throw new TacitException(TacitError.ClientConfigInvalid, Messages.ClientConfigHasComments.Format(language, path, entry));
        }
        catch (JsonException e)
        {
            throw new TacitException(TacitError.ClientConfigInvalid, Messages.ClientConfigInvalid.Format(language, path, e.Message), e);
        }
    }

    private static bool Parses(string text, JsonDocumentOptions options)
    {
        try
        {
            using var _ = JsonDocument.Parse(text, options);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void Write(string path, JsonObject root, string original, bool hasBom)
    {
        var newline = TextFile.NewlineOf(original);
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
        TextFile.Write(path, root.ToJsonString(options) + trailing, hasBom);
    }

    /// <summary>The indentation of the first indented line.</summary>
    [GeneratedRegex(@"\n([ \t]+)\S")]
    private static partial Regex IndentPattern();
}
