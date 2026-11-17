using System.Globalization;
using System.Text;

namespace TacitMemory.Core.Tests;

/// <summary>
/// Every key the settings page describes is checked against the loader and the writer: its range is the one the loader
/// enforces, and its default is the one the loader falls back to and the writer leaves out.
/// </summary>
public sealed class ConfigKeyTableTests : IDisposable
{
    private readonly TestHome _home = new();
    private MemoryService? _service;

    public ConfigKeyTableTests() => _home.InitReady("");

    public void Dispose()
    {
        _service?.Dispose();
        _home.Dispose();
    }

    private MemoryService Service => _service ??= _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

    private string ConfigPath => Path.Combine(_home.TacitHome, "config.yaml");

    private void WriteConfig(string yaml) => File.WriteAllText(ConfigPath, yaml.ReplaceLineEndings("\n"));

    private IReadOnlyList<ConfigKey> Ranged => [.. Service.ConfigView().Keys.Where(key => key.Min is not null)];

    [Fact]
    public void Every_key_with_a_range_refuses_a_number_beyond_either_end_naming_its_path()
    {
        var missed = new List<string>();
        var keys = Ranged;
        foreach (var key in keys)
        {
            foreach (var beyond in (double[])[key.Max!.Value + 1, key.Min!.Value - 1])
            {
                WriteConfig(TestHome.Models + "language: zh\n" + Yaml(WithPartner(key.Path, beyond)));
                var error = Assert.Throws<TacitException>(() => _home.Tacit().LoadConfigFile());
                if (error.Error != TacitError.ConfigInvalid || (error.Problems ?? []).All(problem => problem.Path != key.Path))
                    missed.Add($"{key.Path} = {beyond}");
            }
        }

        Assert.Empty(missed);
    }

    [Fact]
    public void The_defaults_the_settings_page_shows_are_written_as_nothing()
    {
        var defaults = Service.ConfigView().Defaults;

        _home.Tacit().SaveConfig(defaults with { Language = "zh" });

        Assert.Equal("language: zh\n", File.ReadAllText(ConfigPath));
    }

    [Fact]
    public void A_file_that_leaves_every_key_out_is_written_back_as_nothing()
    {
        WriteConfig("language: zh\n");

        _home.Tacit().SaveConfig(_home.Tacit().ReadConfigFile());

        Assert.Equal("language: zh\n", File.ReadAllText(ConfigPath));
    }

    [Fact]
    public void Every_key_at_the_far_end_of_its_range_is_written_and_read_back_as_it_was()
    {
        var values = new Dictionary<string, double>();
        var keys = Ranged;
        foreach (var key in keys)
            values[key.Path] = key.Path.EndsWith(".unlikely", StringComparison.Ordinal) ? key.Min!.Value : key.Max!.Value;
        WriteConfig(TestHome.Models + "language: zh\n" + Yaml(values));
        var before = _home.Tacit().ReadConfigFile();

        _home.Tacit().SaveConfig(before);

        Assert.Equal(before, _home.Tacit().ReadConfigFile());
        foreach (var path in values.Keys)
            Assert.Contains(path[(path.LastIndexOf('.') + 1)..] + ":", File.ReadAllText(ConfigPath));
    }

    /// <summary>The value at <paramref name="path"/>, with the other threshold of a pair at a value that keeps the pair in order.</summary>
    private static Dictionary<string, double> WithPartner(string path, double value)
    {
        var values = new Dictionary<string, double> { [path] = value };
        if (path.EndsWith(".certain", StringComparison.Ordinal))
            values[path[..^"certain".Length] + "unlikely"] = value > 0 ? 0 : -2;
        else if (path.EndsWith(".unlikely", StringComparison.Ordinal))
            values[path[..^"unlikely".Length] + "certain"] = 1;
        return values;
    }

    /// <summary>The values as block YAML, each dotted path nested under its sections.</summary>
    private static string Yaml(IReadOnlyDictionary<string, double> values)
    {
        var root = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var (path, value) in values)
        {
            var parts = path.Split('.');
            var node = root;
            foreach (var part in parts[..^1])
            {
                if (!node.TryGetValue(part, out var child))
                    node[part] = child = new SortedDictionary<string, object>(StringComparer.Ordinal);
                node = (SortedDictionary<string, object>)child;
            }

            node[parts[^1]] = value.ToString("R", CultureInfo.InvariantCulture);
        }

        var output = new StringBuilder();
        Render(root, 0);
        return output.ToString();

        void Render(SortedDictionary<string, object> node, int depth)
        {
            foreach (var (key, value) in node)
            {
                output.Append(' ', depth * 2).Append(key).Append(':');
                if (value is SortedDictionary<string, object> child)
                {
                    output.Append('\n');
                    Render(child, depth + 1);
                }
                else
                {
                    output.Append(' ').Append(value).Append('\n');
                }
            }
        }
    }
}
