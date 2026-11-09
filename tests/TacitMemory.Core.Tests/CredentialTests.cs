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

    private MemoryService Service => _service ??= _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

    private void Configure(string extra = "", string decisionKey = "test-key")
    {
        _home.InitReady(models: _models);
        File.WriteAllText(Path.Combine(_home.TacitHome, "config.yaml"), (_models.ModelsYaml(decisionKey: decisionKey) + extra).ReplaceLineEndings("\n"));
    }

    private Task<ToolResult> Create(string body, bool? confirm = null, string title = "部署脚本") => Service.WritePageAsync(new WritePageRequest
    {
        Op = "create",
        Type = ToolValues.PageTypes.Work,
        Page = "my-app/deploy",
        Title = title,
        Aliases = ["上线脚本", "发布流程"],
        Sections = [new("步骤", body)],
        ConfirmNoSecret = confirm,
    });

    private bool Exists(string page = "my-app/deploy") => File.Exists(Path.Combine(_home.DefaultMemory, "pages", page + ".md"));

    // Secret-shaped samples are assembled at run time so no scanner mistakes this file for a leak.
    private static readonly string OpenAiKey = "sk-" + "proj-Abc123def456GHI789jkl012MNO345";

    private static readonly string GitHubToken = "gh" + "p_aB3dE5fG7hI9jK1lM3nO5pQ7rS9tU1vW3xY5";

    private static readonly string AwsKeyId = "AK" + "IAIOSFODNN7EXAMPLE";

    private static readonly string PrivateKey = "-----BEGIN " + "OPENSSH PRIVATE KEY-----\nb3BlbnNzaC1rZXktdjEAAAAA\n-----END OPENSSH PRIVATE KEY-----";

    private static readonly string PasswordAssign = "pass" + "word=hunter2xyz";

    private static readonly string LongBase64 = "dGhpcyBpcyBhIHNlY3JldCB2YWx1ZSB0aGF0IHNo" + "b3VsZCBub3QgbGVhaw1234XYZ";

    private static readonly string BearerHeader = "Authorization: Bear" + "er 5Tq8Lm2Vx7Wn4Rz9Kp1J";

    private static readonly string UrlCredentials = "postgres://admin:" + "S3cretPass9@db.local:5432/memo";

    private static readonly string Jwt = "eyJhbGciOiJIUzI1NiJ9" + ".eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U";

    private static readonly string SlackToken = "xo" + "xb-1234567890-abcdefghijKL";

    private static readonly string GitLabToken = "glp" + "at-aB3dE5fG7hI9jK1lM3nO";

    private static readonly string GoogleKey = "AI" + "zaSyA1b2C3d4E5f6G7h8I9j0K1l2M3n4O5p6Q";

    private static readonly string AliyunKeyId = "LT" + "AI5tQwErTyUiOp";

    private static readonly string SendGridKey = "S" + "G.ngeVfQFYQlKU0ufo8x5d1A.TwL2iGABf9DHoTf-09kqeF8tAmbihYzrnopKc-1s5cr";

    private static readonly string DockerToken = "dckr" + "_pat_Xq8LmZ2vR7tW9yK4pN1bH6sJ3dF0";

    private static readonly string NotionSecret = "sec" + "ret_9Kc2Lm7Pq0Rx3Tv5Wz8Nb1Hs4Yd6Uf";

    private static readonly string StripeKey = "sk" + "_live_51H8xYzAbCdEfGhIjKlMnOpQr";

    public static TheoryData<string, string> Secrets => new()
    {
        { $"OpenAI 的 key 是 {OpenAiKey}", "sk-pro" },
        { $"令牌 {GitHubToken} 别外传", "ghp_aB" },
        { $"AWS 用 {AwsKeyId} 登录", "AKIAIO" },
        { PrivateKey, "-----B" },
        { $"连接串里写了 {PasswordAssign}", "passwo" },
        { $"数据是 {LongBase64}", "dGhpcy" },
        { $"curl -H \"{BearerHeader}\" 调接口", "Bearer" },
        { $"连接串 {UrlCredentials}", "postgr" },
        { "备用登录码 mail_pass = " + "7731-XKTQ-9920-ZPLM，邮箱二次验证用", "mail_p" },
        { "返回体 {\"access_token\": \"" + "a1B2c3D4e5F6g7H8\"}", "access" },
        { "请求头 x-api-key: " + "9fGh3KlZxCvBnM7a 别外传", "x-api-" },
        { "浏览器里的 session=" + "8f2Kq9Lm4Xw7Zt1Vb6Nc 复制给同事", "sessio" },
        { $"登录后拿到 {Jwt} 存起来", "eyJhbG" },
        { $"Slack 机器人用 {SlackToken}", "xoxb-1" },
        { $"CI 里配了 {GitLabToken}", "glpat-" },
        { $"地图服务 {GoogleKey}", "AIzaSy" },
        { $"阿里云 AccessKeyId {AliyunKeyId}", "LTAI5t" },
        { $"邮件服务 {SendGridKey}", "SG.nge" },
        { $"Docker Hub 用 {DockerToken} 登录", "dckr_p" },
        { $"Notion 集成的 {NotionSecret}", "secret" },
        { $"支付用 {StripeKey}", "sk_liv" },
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
    [InlineData("请求头示例：Authorization: Bearer <token>，实际值从环境变量读取")]
    [InlineData("API_KEY=xxxxxxxxxxxxxxxx 只是占位符")]
    [InlineData("JSON 示例：{\"token\": \"<redacted>\", \"user\": \"alice\"}")]
    [InlineData("配置项 apiKeyEnv: OPENAI_API_KEY 指向环境变量")]
    [InlineData("会话超时 session: 30 分钟，bypass: 2024-11-01 起停用")]
    [InlineData("数据库地址 postgres://db.local:5432/memo，不带账号")]
    [InlineData("token 字段统一叫 accessToken，这是命名规范")]
    [InlineData("路径 src/TacitMemory.Core/Pages/PageTools.cs")]
    public async Task Hashes_identifiers_placeholders_and_paths_pass_the_patterns(string body)
    {
        var result = await Create(body);

        Assert.False(result.IsError, result.Text);
        Assert.True(Exists());
    }

    [Fact]
    public async Task A_write_the_patterns_pass_never_reaches_the_decision_model()
    {
        var result = await Create("服务器 root 口令记在 1Password 里，先跑迁移再重启。");

        Assert.False(result.IsError, result.Text);
        Assert.True(Exists());
        Assert.Equal(0, _models.DecisionCalls);
    }

    [Fact]
    public async Task Without_the_decision_model_a_write_the_patterns_pass_goes_ahead()
    {
        _models.DecisionDown = true;

        var result = await Create("先跑迁移，再重启服务。");

        Assert.False(result.IsError, result.Text);
        Assert.True(Exists());
    }

    [Fact]
    public async Task Confirming_no_secret_writes_past_the_patterns()
    {
        var result = await Create($"示例 key：{OpenAiKey} 是文档里的假值", confirm: true);

        Assert.False(result.IsError, result.Text);
        Assert.True(Exists());
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
