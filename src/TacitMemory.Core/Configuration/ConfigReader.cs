using System.Globalization;
using TacitMemory.Core.Localization;
using YamlDotNet.RepresentationModel;

namespace TacitMemory.Core.Configuration;

/// <summary>
/// Reads typed values out of a YAML mapping tree and collects every problem with its key path, so the user sees all
/// errors at once. Missing optional values return their default; wrong ones are reported and also return the
/// default, so reading can go on.
/// </summary>
internal sealed class ConfigReader(MemoryLanguage language)
{
    private readonly List<ConfigProblem> _problems = [];

    /// <summary>Every problem found, in the order found, each with its key path.</summary>
    public IReadOnlyList<ConfigProblem> Problems => _problems;

    /// <summary>The problems as the lines of a report.</summary>
    public IEnumerable<string> Errors => _problems.Select(problem => Messages.ConfigErrorLine.Format(language, problem.Path, problem.Message));

    public static string Join(string path, string key) => path.Length == 0 ? key : $"{path}.{key}";

    public void Error(string path, Localized message, params object?[] args) =>
        _problems.Add(new ConfigProblem(path, message.Format(language, args)));

    /// <summary>Reports keys of <paramref name="map"/> outside <paramref name="known"/>.</summary>
    public void OnlyKnown(YamlMappingNode? map, string path, params string[] known)
    {
        if (map is null)
            return;
        foreach (var key in map.Children.Keys)
        {
            var name = key is YamlScalarNode scalar ? scalar.Value ?? "" : key.ToString();
            if (!known.Contains(name, StringComparer.Ordinal))
                Error(Join(path, name), Messages.ConfigUnknownKey);
        }
    }

    /// <summary>A nested mapping, or <c>null</c> when absent, empty, or not a mapping (the last is reported).</summary>
    public YamlMappingNode? Mapping(YamlMappingNode? map, string path, string key, bool required = false)
    {
        var node = Value(map, key);
        var keyPath = Join(path, key);
        switch (node)
        {
            case null:
                if (required)
                    Error(keyPath, Messages.ConfigRequired);
                return null;
            case YamlMappingNode mapping:
                return mapping;
            default:
                Error(keyPath, Messages.ConfigExpectedMapping);
                return null;
        }
    }

    /// <summary>Non-empty text, or <c>null</c> when absent or empty.</summary>
    public string? Text(YamlMappingNode? map, string path, string key, bool required = false)
    {
        var node = Value(map, key);
        var keyPath = Join(path, key);
        if (node is not null and not YamlScalarNode)
        {
            Error(keyPath, Messages.ConfigExpectedText);
            return null;
        }

        var text = (node as YamlScalarNode)?.Value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            if (required)
                Error(keyPath, Messages.ConfigRequired);
            return null;
        }

        return text;
    }

    /// <summary>The whole number at <paramref name="entry"/> within its range; its default when absent or wrong (reported).</summary>
    public int Integer(YamlMappingNode? map, IntegerEntry entry)
    {
        var text = Text(map, entry.Section, entry.Key);
        if (text is null)
            return entry.Default;
        if (int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            && value >= entry.Lowest && value <= entry.Highest)
            return value;
        Error(entry.Path, Messages.ConfigExpectedInteger, entry.Lowest, entry.Highest, text);
        return entry.Default;
    }

    /// <summary>The number at <paramref name="entry"/> within its range; <c>null</c> when absent or wrong (reported).</summary>
    public double? Probability(YamlMappingNode? map, ProbabilityEntry entry, bool required = false)
    {
        var text = Text(map, entry.Section, entry.Key, required);
        if (text is null)
            return null;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value >= entry.Min && value <= entry.Max)
            return value;
        Error(entry.Path, Messages.ConfigExpectedProbability, text);
        return null;
    }

    /// <summary>A required http(s) URL; only reported missing when its section exists.</summary>
    public Uri? Url(YamlMappingNode? map, string path, string key)
    {
        var text = Text(map, path, key, required: map is not null);
        if (text is null)
            return null;
        if (Uri.TryCreate(text, UriKind.Absolute, out var url) && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps))
            return url;
        Error(Join(path, key), Messages.ConfigExpectedUrl, text);
        return null;
    }

    /// <summary>The value under <paramref name="key"/>; YAML null (empty, <c>~</c>, <c>null</c>) counts as absent.</summary>
    private static YamlNode? Value(YamlMappingNode? map, string key)
    {
        if (map is null || !map.Children.TryGetValue(new YamlScalarNode(key), out var node))
            return null;
        return node is YamlScalarNode { Style: YamlDotNet.Core.ScalarStyle.Plain, Value: null or "" or "~" or "null" or "Null" or "NULL" }
            ? null
            : node;
    }
}
