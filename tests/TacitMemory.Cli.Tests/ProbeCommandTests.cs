using TacitMemory.Core;
using TacitMemory.Core.Tests;

namespace TacitMemory.Cli.Tests;

/// <summary>
/// <c>tacit probe</c> through the CLI seam: the exit codes, the targets and their skipping, and the report. A probe runs with
/// no service and no initialized memory repository; its output carries no key value and no control character.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class ProbeCommandTests : IDisposable
{
    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    public static TheoryData<string[]> WorkingTargets() => new()
    {
        { ["probe"] },
        { ["probe", "embedding"] },
        { ["probe", "Reranker"] },
        { ["PROBE", "DECISION"] },
    };

    [Theory]
    [MemberData(nameof(WorkingTargets))]
    public async Task Probing_models_that_work_exits_0_and_says_they_succeeded(string[] args)
    {
        var models = new ModelDouble();
        _cli.InitReady(models);

        var result = await _cli.RunAsync(args);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("成功", result.Stdout);
        Assert.Contains("config.yaml", result.Stdout);
        Assert.Empty(result.Stderr);
    }

    [Fact]
    public async Task A_model_that_fails_exits_1_and_gives_its_status_code()
    {
        var models = new ModelDouble { EmbeddingDown = true };
        _cli.InitReady(models);

        var result = await _cli.RunAsync(["probe", "embedding"]);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("HTTP 404", result.Stdout);
        Assert.Empty(result.Stderr);
    }

    [Fact]
    public async Task A_named_model_that_is_not_configured_exits_1_and_says_so()
    {
        _cli.InitReady();
        _cli.RemoveModels();

        var result = await _cli.RunAsync(["probe", "reranker"]);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("未配置", result.Stdout);
        Assert.DoesNotContain("跳过", result.Stdout);
    }

    [Fact]
    public async Task Without_a_target_an_unconfigured_model_is_skipped_and_not_a_failure()
    {
        var models = new ModelDouble();
        _cli.Home.InitReady(extraConfig: $"server:\n  port: {_cli.Port}\n", models: models);
        File.WriteAllText(
            _cli.ConfigPath,
            $"server:\n  port: {_cli.Port}\nmodels:\n  decision: {{ url: '{models.DecisionUrl}', model: double-decision }}\n");

        var result = await _cli.RunAsync(["probe"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(2, result.Stdout.Split("未配置，跳过").Length - 1);
        Assert.Contains("成功", result.Stdout);
        Assert.Empty(result.Stderr);
    }

    public static TheoryData<string[]> UnloadableConfigTargets() => new()
    {
        { ["probe"] },
        { ["probe", "embedding"] },
    };

    [Theory]
    [MemberData(nameof(UnloadableConfigTargets))]
    public async Task A_config_that_does_not_load_exits_3(string[] args)
    {
        _cli.InitReady();
        _cli.BreakConfig();

        var result = await _cli.RunAsync(args);

        Assert.Equal(3, result.ExitCode);
        Assert.Empty(result.Stdout);
    }

    [Fact]
    public async Task The_probe_runs_with_no_service_and_no_initialized_memory_repository()
    {
        var models = new ModelDouble();
        Directory.CreateDirectory(_cli.Home.TacitHome);
        File.WriteAllText(_cli.ConfigPath, $"server:\n  port: {_cli.Port}\n{models.ModelsYaml()}");

        var result = await _cli.RunAsync(["probe"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Stderr);
        Assert.False(Directory.Exists(_cli.Home.DefaultMemory));
    }

    [Fact]
    public async Task The_report_shows_no_key_value_even_when_the_answer_echoes_it()
    {
        var models = new ModelDouble();
        _cli.InitReady(models);
        models.ScriptDecision(ModelReply.StatusWithBody(401, "rejected test-key"));

        var result = await _cli.RunAsync(["probe", "decision"]);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("rejected ***", result.Stdout);
        Assert.DoesNotContain("test-key", result.Stdout);
        Assert.DoesNotContain("test-key", result.Stderr);
    }

    [Fact]
    public async Task The_plain_report_has_no_control_characters_other_than_line_breaks()
    {
        var models = new ModelDouble();
        _cli.InitReady(models);
        models.ScriptEmbedding(ModelReply.StatusWithBody(500, "line one\n\u001b[31mred\tend"));

        var result = await _cli.RunAsync(["probe", "embedding"]);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("line one", result.Stdout);
        Assert.DoesNotContain(result.Stdout, character => char.IsControl(character) && character is not ('\r' or '\n'));
    }

    [Fact]
    public async Task The_report_follows_the_memory_language()
    {
        var models = new ModelDouble();
        _cli.Home.InitReady(extraConfig: $"server:\n  port: {_cli.Port}\nlanguage: en\n", models: models);

        var result = await _cli.RunAsync(["probe", "reranker"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("succeeded", result.Stdout);
        Assert.Contains("config.yaml", result.Stdout);
    }
}
