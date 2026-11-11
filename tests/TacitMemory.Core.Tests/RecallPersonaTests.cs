using System.Text.RegularExpressions;

namespace TacitMemory.Core.Tests;

/// <summary>
/// <c>recall</c> with <c>kind=persona</c> and <c>kind=rules</c>: the persona and the situational rules are read directly,
/// with their short codes, and never go through search.
/// </summary>
public sealed partial class RecallPersonaTests : IDisposable
{
    private readonly TestHome _home = new();
    private MemoryService? _service;

    public RecallPersonaTests() => _home.InitReady();

    public void Dispose()
    {
        _service?.Dispose();
        _home.Dispose();
    }

    private MemoryService Service => _service ??= _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

    private string PersonaMd => Path.Combine(_home.DefaultMemory, "persona.md");

    [GeneratedRegex(@"\[([0-9a-f]{6}(?:-\d+)?)\] 性能/节点比较")]
    private static partial Regex NodeRuleCode();

    [GeneratedRegex(@"\[([0-9a-f]{6}(?:-\d+)?)\] 沟通：先要结论")]
    private static partial Regex CommunicationCode();

    /// <summary>A persona with all four global sections, two domains and two workstreams, written out of their sorted order.</summary>
    private const string Fixture = """
        ## 红线
        - 不要删库；改为先备份
          <!-- 2026-10-25 原话「别删库」 -->

        ## 协作规则
        - 需求不清时 → 先连续追问
          <!-- 2026-10-20 原话「先问清楚再动手」 -->

        ## 偏好
        - 沟通：先要结论
          <!-- 2026-10-21 原话「先说结论」 -->

        ## 档案
        - 系统：Windows 11 x64

        ## 领域：network-proxy
        触发：改代理、分流或测速时
        ### 协作规则
        - 性能/节点比较 → 不拿单次测速当日常结论
          <!-- 2026-11-06 原话「别拿单次测速下结论」 -->

        ## 主线：tacit-memory
        ### 协作规则
        - 发版 → 先跑全量测试

        ## 领域：backup
        触发：改备份策略时
        ### 偏好
        - 频率：每天一次

        ## 主线：my-app
        ### 偏好
        - 游戏内导出：先验收再发版

        """;

    private void WritePersona(string text) => File.WriteAllText(PersonaMd, text.ReplaceLineEndings("\n"));

    private Task<ToolResult> Persona() => Service.RecallAsync(new RecallRequest { Kind = ToolValues.RecallKinds.Persona });

    private Task<ToolResult> Rules(string? domain = null) =>
        Service.RecallAsync(new RecallRequest { Kind = ToolValues.RecallKinds.Rules, Domain = domain });

    [Fact]
    public async Task Persona_lists_the_global_sections_then_domains_then_workstreams_each_sorted_with_short_codes()
    {
        WritePersona(Fixture);

        var result = await Persona();

        Assert.False(result.IsError, result.Text);
        var order = new[]
        {
            "Red lines, 1 entry:",
            "Working rules, 1 entry:",
            "Preferences, 1 entry:",
            "Profile, 1 entry:",
            "Domain backup, 1 entry. Trigger: 改备份策略时",
            "Domain network-proxy, 1 entry. Trigger: 改代理、分流或测速时",
            "Workstream my-app, 1 entry:",
            "Workstream tacit-memory, 1 entry:",
        };
        var positions = order.Select(text => result.Text.IndexOf(text, StringComparison.Ordinal)).ToList();
        Assert.All(positions, position => Assert.True(position >= 0, result.Text));
        Assert.Equal(positions.Order(), positions);
        Assert.Matches(@"- \[[0-9a-f]{6}\] 不要删库；改为先备份", result.Text);
        Assert.Matches(@"- \[[0-9a-f]{6}\] 系统：Windows 11 x64", result.Text);
        Assert.Matches(@"- \[[0-9a-f]{6}\] 性能/节点比较 → 不拿单次测速当日常结论", result.Text);
    }

    [Fact]
    public async Task Persona_leaves_out_the_source_comments()
    {
        WritePersona(Fixture);

        var result = await Persona();

        Assert.DoesNotContain("原话", result.Text);
        Assert.DoesNotContain("<!--", result.Text);
    }

    [Fact]
    public async Task A_short_code_from_persona_replaces_its_entry()
    {
        WritePersona(Fixture);
        var code = CommunicationCode().Match((await Persona()).Text).Groups[1].Value;

        var result = await Service.WritePersonaAsync(new WritePersonaRequest
        {
            Op = "replace",
            Hash = code,
            Text = "沟通：先要结论，反感客套和铺垫",
            Source = "先说结论",
        });

        Assert.False(result.IsError, result.Text);
        Assert.Contains("沟通：先要结论，反感客套和铺垫", File.ReadAllText(PersonaMd));
    }

    [Fact]
    public async Task A_short_code_from_rules_deletes_a_domain_entry_and_keeps_its_block()
    {
        WritePersona(Fixture);
        var code = NodeRuleCode().Match((await Rules("network-proxy")).Text).Groups[1].Value;

        var result = await Service.WritePersonaAsync(new WritePersonaRequest { Op = "delete", Hash = code });

        Assert.False(result.IsError, result.Text);
        var after = File.ReadAllText(PersonaMd);
        Assert.DoesNotContain("性能/节点比较", after);
        Assert.Contains("## 领域：network-proxy\n触发：改代理、分流或测速时", after);
    }

    [Fact]
    public async Task Persona_ignores_the_query_and_is_not_a_search()
    {
        WritePersona(Fixture);

        var withQuery = await Service.RecallAsync(new RecallRequest { Query = "数据库升级", Kind = ToolValues.RecallKinds.Persona });
        var without = await Persona();

        Assert.False(withQuery.IsError, withQuery.Text);
        Assert.Equal(without.Text, withQuery.Text);
    }

    [Fact]
    public async Task An_empty_persona_says_no_relevant_memory()
    {
        WritePersona("## 红线\n\n## 协作规则\n\n## 偏好\n\n## 档案\n");

        var result = await Persona();

        Assert.False(result.IsError);
        Assert.Equal("No relevant memory.", result.Text);
    }

    [Fact]
    public async Task Rules_for_one_domain_give_its_trigger_and_every_entry_with_its_short_code()
    {
        WritePersona(Fixture);

        var result = await Rules("network-proxy");

        Assert.False(result.IsError, result.Text);
        Assert.Contains("Domain network-proxy, 1 entry. Trigger: 改代理、分流或测速时", result.Text);
        Assert.Matches(@"- \[[0-9a-f]{6}\] 性能/节点比较 → 不拿单次测速当日常结论", result.Text);
        Assert.DoesNotContain("Domain backup", result.Text);
        Assert.DoesNotContain("Red lines", result.Text);
        Assert.DoesNotContain("原话", result.Text);
    }

    [Fact]
    public async Task Rules_without_a_domain_group_every_domain_by_label_and_nothing_else()
    {
        WritePersona(Fixture);

        var result = await Rules();

        Assert.False(result.IsError, result.Text);
        var backup = result.Text.IndexOf("Domain backup,", StringComparison.Ordinal);
        var proxy = result.Text.IndexOf("Domain network-proxy,", StringComparison.Ordinal);
        Assert.True(backup >= 0 && proxy > backup, result.Text);
        Assert.DoesNotContain("发版", result.Text);
        Assert.DoesNotContain("游戏内导出", result.Text);
        Assert.DoesNotContain("Red lines", result.Text);
    }

    [Fact]
    public async Task An_unknown_domain_is_refused_with_the_labels_that_exist()
    {
        WritePersona(Fixture);

        var result = await Rules("networking");

        Assert.True(result.IsError);
        Assert.Contains("networking", result.Text);
        Assert.Contains("backup, network-proxy", result.Text);
        Assert.Contains("Next step:", result.Text);
    }

    [Fact]
    public async Task Rules_say_no_relevant_memory_when_the_persona_has_no_domain()
    {
        WritePersona("## 红线\n- 不要删库；改为先备份\n\n## 主线：tacit-memory\n### 协作规则\n- 发版 → 先跑全量测试\n");

        var result = await Rules();

        Assert.False(result.IsError);
        Assert.Equal("No relevant memory.", result.Text);
    }

    [Theory]
    [InlineData(null, null, null, "backup")]
    [InlineData("pages", null, null, "backup")]
    [InlineData("activities", null, null, "backup")]
    [InlineData("persona", null, null, "backup")]
    public async Task A_domain_goes_only_with_kind_rules(string? kind, string? page, string? from, string? domain)
    {
        WritePersona(Fixture);

        var result = await Service.RecallAsync(new RecallRequest { Query = "数据库", Kind = kind, Page = page, From = from, Domain = domain });

        Assert.True(result.IsError);
        Assert.Contains("Parameter domain applies only to kind=rules.", result.Text);
        Assert.Contains("Next step:", result.Text);
    }

    [Theory]
    [InlineData("persona", "tacit-memory/db-upgrade", null, null, null)]
    [InlineData("rules", "tacit-memory/db-upgrade", null, null, "backup")]
    [InlineData("persona", null, "2026-10-01", null, null)]
    [InlineData("persona", null, null, "2026-10-31", null)]
    [InlineData("persona", null, null, null, "tacit-memory")]
    [InlineData("rules", null, "2026-10-01", null, "backup")]
    public async Task Persona_and_rules_refuse_the_parameters_that_do_not_apply(string kind, string? page, string? from, string? to, string? workstream)
    {
        WritePersona(Fixture);

        var result = await Service.RecallAsync(new RecallRequest { Kind = kind, Page = page, From = from, To = to, Workstream = workstream, Domain = "backup" });

        Assert.True(result.IsError);
        Assert.StartsWith("Refused:", result.Text);
        Assert.Contains("Next step:", result.Text);
    }

    [Fact]
    public async Task Persona_refuses_a_page_by_name()
    {
        WritePersona(Fixture);

        var result = await Service.RecallAsync(new RecallRequest { Kind = ToolValues.RecallKinds.Persona, Page = "tacit-memory/db-upgrade" });

        Assert.True(result.IsError);
        Assert.Contains("Parameter page cannot be combined with kind=persona.", result.Text);
    }

    [Fact]
    public async Task An_unknown_kind_is_refused_with_all_four_kinds()
    {
        var result = await Service.RecallAsync(new RecallRequest { Query = "数据库", Kind = "events" });

        Assert.True(result.IsError);
        Assert.Contains("Unknown kind \"events\"; use pages, activities, rules or persona.", result.Text);
    }
}
