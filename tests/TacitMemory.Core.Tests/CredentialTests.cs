namespace TacitMemory.Core.Tests;

public sealed class CredentialTests : IDisposable
{
    private readonly TestHome _home = new();
    private readonly ModelDouble _models = new() { EmbeddingDown = true };
    private MemoryService? _service;

    public CredentialTests() => Configure();

    public void Dispose()
    {
        _service?.Dispose();
        _models.Dispose();
        _home.Dispose();
    }

    private MemoryService Service => _service ??= _home.Tacit().OpenService(_ => { });

    private void Configure(string extra = "", string? decisionKey = "test-key")
    {
        _home.InitReady(models: _models);
        File.WriteAllText(Path.Combine(_home.TacitHome, "config.yaml"), (_models.ModelsYaml(decisionKey: decisionKey) + extra).ReplaceLineEndings("\n"));
    }

    private Task<ToolResult> Create(string body, bool? confirm = null, string title = "部署脚本") => Service.WritePageAsync(new WritePageRequest
    {
        Op = "create",
        Page = "my-app/deploy",
        Title = title,
        Aliases = ["上线脚本", "发布流程"],
        Sections = [new("步骤", body)],
        ConfirmNoSecret = confirm,
    });

    private bool Exists(string page = "my-app/deploy") => File.Exists(Path.Combine(_home.DefaultMemory, "pages", page + ".md"));

    // Secret-shaped samples are assembled at run time so no scanner mistakes this file for a leak.
    private static readonly string GitHubToken = "gh" + "p_aB3dE5fG7hI9jK1lM3nO5pQ7rS9tU1vW3xY5";

    private static readonly string AwsKeyId = "AK" + "IAIOSFODNN7EXAMPLE";

    private static readonly string PrivateKey = "-----BEGIN " + "OPENSSH PRIVATE KEY-----\nb3BlbnNzaC1rZXktdjEAAAAA\n-----END OPENSSH PRIVATE KEY-----";

    public static TheoryData<string, string> Secrets => new()
    {
        { "OpenAI 的 key 是 sk-proj-Abc123def456GHI789jkl012MNO345", "sk-pro" },
        { $"令牌 {GitHubToken} 别外传", "ghp_aB" },
        { $"AWS 用 {AwsKeyId} 登录", "AKIAIO" },
        { PrivateKey, "-----B" },
        { "连接串里写了 password=hunter2xyz", "passwo" },
        { "数据是 dGhpcyBpcyBhIHNlY3JldCB2YWx1ZSB0aGF0IHNob3VsZCBub3QgbGVhaw1234XYZ", "dGhpcy" },
    };

    [Theory]
    [MemberData(nameof(Secrets))]
    public async Task Common_credential_formats_are_refused_without_asking_the_model_and_the_fragment_is_pointed_out_masked(string body, string start)
    {
        var result = await Create(body);

        Assert.True(result.IsError);
        Assert.StartsWith("Refused: ", result.Text);
        Assert.Contains("section 步骤", result.Text);
        Assert.Contains(start + "…", result.Text);
        Assert.DoesNotContain(body.Split(' ', '\n').OrderByDescending(word => word.Length).First(), result.Text);
        Assert.Contains("confirm_no_secret", result.Text);
        Assert.False(Exists());
        Assert.Equal(0, _models.DecisionCalls);
    }

    [Theory]
    [InlineData("提交 3f2a9c1d8e7b6a5f4e3d2c1b0a9f8e7d6c5b4a39 修好了")]
    [InlineData("请求编号 550e8400-e29b-41d4-a716-446655440000")]
    [InlineData("配置里写 password=<你的密码>，密码放在 1Password 里")]
    [InlineData("路径 src/TacitMemory.Core/Pages/PageTools.cs")]
    public async Task Hashes_identifiers_placeholders_and_paths_pass_the_patterns(string body)
    {
        var result = await Create(body);

        Assert.False(result.IsError, result.Text);
        Assert.Equal(1, _models.DecisionCalls);
    }

    [Theory]
    [InlineData(0.9)]
    [InlineData(0.5)]
    public async Task Content_the_model_finds_or_suspects_a_secret_in_is_refused(double probability)
    {
        _models.IsTrue = _ => probability;

        var result = await Create("服务器 root 口令是 Tr0ub4dor&3 记一下");

        Assert.True(result.IsError);
        Assert.StartsWith("Refused: ", result.Text);
        Assert.Contains("confirm_no_secret", result.Text);
        Assert.False(Exists());
    }

    [Fact]
    public async Task Content_the_model_finds_no_secret_in_is_written()
    {
        _models.IsTrue = _ => 0.1;

        var result = await Create("先跑迁移，再重启服务。");

        Assert.False(result.IsError, result.Text);
        Assert.True(Exists());
    }

    [Fact]
    public async Task The_question_is_in_english_and_the_whole_write_goes_into_it_as_written()
    {
        await Create("先跑迁移，再重启服务。", title: "部署脚本说明");

        var (body, _) = _models.LastDecision!.Value;
        var question = body["questions"]!.AsObject().Single().Value!.AsObject();
        Assert.Equal("noul", question["type"]!.GetValue<string>());
        Assert.StartsWith("The state is", question["instructions"]!.GetValue<string>());
        var state = body["state"]!.GetValue<string>();
        Assert.Contains("部署脚本说明", state);
        Assert.Contains("上线脚本", state);
        Assert.Contains("步骤", state);
        Assert.Contains("先跑迁移，再重启服务。", state);
    }

    [Fact]
    public async Task Configured_credential_thresholds_replace_the_defaults()
    {
        Configure("decision:\n  credential: { certain: 0.95, unlikely: 0.6 }\n");
        _models.IsTrue = _ => 0.5;

        var result = await Create("先跑迁移，再重启服务。");

        Assert.False(result.IsError, result.Text);
    }

    [Fact]
    public async Task Confirming_no_secret_writes_without_patterns_or_model()
    {
        _models.IsTrue = _ => 0.99;

        var result = await Create("示例 key：sk-proj-Abc123def456GHI789jkl012MNO345 是文档里的假值", confirm: true);

        Assert.False(result.IsError, result.Text);
        Assert.True(Exists());
        Assert.Equal(0, _models.DecisionCalls);
    }

    [Fact]
    public async Task Without_the_decision_model_a_write_the_patterns_pass_still_needs_confirming()
    {
        _models.DecisionDown = true;

        var refused = await Create("先跑迁移，再重启服务。");

        Assert.True(refused.IsError);
        Assert.Contains("decision model is unavailable", refused.Text);
        Assert.Contains("confirm_no_secret", refused.Text);
        Assert.False(Exists());

        var confirmed = await Create("先跑迁移，再重启服务。", confirm: true);
        Assert.False(confirmed.IsError, confirmed.Text);
        Assert.True(Exists());
    }

    [Fact]
    public async Task Without_a_decision_key_a_write_needs_confirming()
    {
        Configure(decisionKey: null);

        var result = await Create("先跑迁移，再重启服务。");

        Assert.True(result.IsError);
        Assert.Contains("confirm_no_secret", result.Text);
    }

    [Fact]
    public async Task Section_upserts_are_checked_in_the_section_name_and_body()
    {
        Assert.False((await Create("先跑迁移，再重启服务。")).IsError);

        var result = await Service.WritePageAsync(new WritePageRequest
        {
            Op = "upsert_section",
            Page = "my-app/deploy",
            Section = "凭据",
            Body = $"token: {GitHubToken}",
        });

        Assert.True(result.IsError);
        Assert.Contains("section 凭据", result.Text);
        Assert.DoesNotContain("## 凭据", File.ReadAllText(Path.Combine(_home.DefaultMemory, "pages", "my-app", "deploy.md")));
    }

    [Fact]
    public async Task Header_updates_are_checked_in_the_title_and_added_aliases()
    {
        Assert.False((await Create("先跑迁移，再重启服务。")).IsError);

        var result = await Service.WritePageAsync(new WritePageRequest
        {
            Op = "update_header",
            Page = "my-app/deploy",
            AddAliases = [$"{AwsKeyId} 那个账号"],
        });

        Assert.True(result.IsError);
        Assert.Contains("alias", result.Text);
        Assert.DoesNotContain(AwsKeyId, File.ReadAllText(Path.Combine(_home.DefaultMemory, "pages", "my-app", "deploy.md")));
    }

    [Fact]
    public async Task Deleting_a_section_takes_no_confirmation()
    {
        var result = await Service.WritePageAsync(new WritePageRequest
        {
            Op = "delete_section",
            Page = "my-app/deploy",
            Section = "步骤",
            Hash = "abcdef",
            ConfirmNoSecret = true,
        });

        Assert.True(result.IsError);
        Assert.Contains("Parameter confirm_no_secret does not apply to op delete_section.", result.Text);
    }
}
