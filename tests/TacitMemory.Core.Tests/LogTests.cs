using Microsoft.Extensions.Logging;

namespace TacitMemory.Core.Tests;

/// <summary>
/// What the service writes to its log, read through a logger factory that keeps every event. A tool call names its page
/// or short code and never carries the text it sends; no event holds a secret; the startup, the cooldown of a model that
/// finally failed and the automatic backup are logged; a notice is both shown and logged.
/// </summary>
public sealed class LogTests : IDisposable
{
    // Markers are written into the request text, so a test can search the whole log for them.
    private const string BodyMarker = "正文原话 body-marker-4471";
    private const string TitleMarker = "title-marker-2281";
    private const string QueryMarker = "query-marker-8812";
    private const string ActivityMarker = "activity-marker-5530";
    private const string PersonaMarker = "persona-marker-6604";
    private const string SecretKey = "sk-log-test-secret-7f3a9c";

    private readonly TestHome _home = new();
    private readonly ModelDouble _models = new();
    private readonly CapturingLoggerFactory _logs = new();
    private MemoryService? _service;

    public LogTests() => Configure();

    public void Dispose()
    {
        _service?.Dispose();
        _models.Dispose();
        _home.Dispose();
    }

    private MemoryService Service => _service ??= _home.Tacit().OpenService(_ => { }, _logs);

    private void Configure(string decisionKey = "test-key", string? embeddingKey = null, string? rerankerKey = null)
    {
        _home.InitReady(models: _models);
        File.WriteAllText(
            Path.Combine(_home.TacitHome, "config.yaml"),
            _models.ModelsYaml(decisionKey: decisionKey, embeddingKey: embeddingKey, rerankerKey: rerankerKey).ReplaceLineEndings("\n"));
    }

    private static WritePageRequest Create(string page, string title = "部署脚本") => new()
    {
        Op = "create",
        Type = ToolValues.PageTypes.Work,
        Page = page,
        Title = title,
        Aliases = ["上线脚本", "发布流程"],
        Sections = [new("步骤", BodyMarker)],
    };

    private IEnumerable<LogEntry> EventsOf(string tool) => _logs.Entries.Where(entry => entry.Message.Contains(tool, StringComparison.Ordinal));

    [Fact]
    public async Task A_page_write_is_logged_with_its_page_ID_and_outcome_and_never_its_text()
    {
        var result = await Service.WritePageAsync(Create("my-app/deploy", TitleMarker));

        Assert.False(result.IsError);
        var line = Assert.Single(EventsOf("write_page")).Message;
        Assert.Contains("my-app/deploy", line, StringComparison.Ordinal);
        Assert.Contains("已写入", line, StringComparison.Ordinal);
        Assert.DoesNotContain(BodyMarker, _logs.AllText, StringComparison.Ordinal);
        Assert.DoesNotContain(TitleMarker, _logs.AllText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_recall_is_logged_with_its_page_and_never_its_query()
    {
        await Service.WritePageAsync(Create("my-app/deploy"));

        await Service.RecallAsync(new RecallRequest { Page = "my-app/deploy" });
        await Service.RecallAsync(new RecallRequest { Query = QueryMarker });

        var reads = EventsOf("recall").Select(entry => entry.Message).ToList();
        Assert.Equal(2, reads.Count);
        Assert.Contains(reads, line => line.Contains("my-app/deploy", StringComparison.Ordinal));
        Assert.DoesNotContain(QueryMarker, _logs.AllText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Recent_and_persona_writes_are_logged_with_the_short_code_they_name_and_never_their_text()
    {
        await Service.WriteRecentAsync(new WriteRecentRequest
        {
            Text = ActivityMarker,
            Workstream = "my-app",
            Replaces = "abc123",
        });
        await Service.WritePersonaAsync(new WritePersonaRequest
        {
            Op = "delete",
            Section = "preferences",
            Text = PersonaMarker,
            Hash = "def456",
        });

        Assert.Contains(EventsOf("write_recent"), entry => entry.Message.Contains("abc123", StringComparison.Ordinal));
        Assert.Contains(EventsOf("write_persona"), entry => entry.Message.Contains("def456", StringComparison.Ordinal));
        Assert.DoesNotContain(ActivityMarker, _logs.AllText, StringComparison.Ordinal);
        Assert.DoesNotContain(PersonaMarker, _logs.AllText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_model_that_finally_fails_is_logged_as_a_warning_with_its_cooldown()
    {
        await Service.WritePageAsync(Create("tacit-memory/db-upgrade", "数据库升级后扩展丢失"));
        _models.DecisionDown = true;

        await Service.WritePageAsync(Create("tacit-memory/pg-search", "升级后搜不到中文"));

        Assert.Contains(_logs.Entries, entry => entry.Level == LogLevel.Warning
            && entry.Message.Contains("decision", StringComparison.Ordinal)
            && entry.Message.Contains("30", StringComparison.Ordinal));
    }

    [Fact]
    public async Task No_event_holds_any_model_key()
    {
        const string EmbeddingKey = "sk-log-embedding-secret-2d8e";
        const string RerankerKey = "sk-log-reranker-secret-9b1c";
        Configure(decisionKey: SecretKey, embeddingKey: EmbeddingKey, rerankerKey: RerankerKey);
        _models.EmbeddingDown = true;
        _models.RerankDown = true;
        await Service.WritePageAsync(Create("tacit-memory/db-upgrade", "数据库升级后扩展丢失"));
        _models.EmbeddingDown = false;
        _home.Clock.Advance(TimeSpan.FromMinutes(1));
        _models.DecisionDown = true;

        await Service.WritePageAsync(Create("tacit-memory/pg-search", "升级后搜不到中文"));
        await Service.RecallAsync(new RecallRequest { Query = "升级" });

        Assert.Contains(_logs.Entries, entry => entry.Message.Contains("embedding", StringComparison.Ordinal));
        Assert.Contains(_logs.Entries, entry => entry.Message.Contains("reranker", StringComparison.Ordinal));
        Assert.Contains(_logs.Entries, entry => entry.Message.Contains("decision", StringComparison.Ordinal));
        Assert.DoesNotContain(SecretKey, _logs.AllText, StringComparison.Ordinal);
        Assert.DoesNotContain(EmbeddingKey, _logs.AllText, StringComparison.Ordinal);
        Assert.DoesNotContain(RerankerKey, _logs.AllText, StringComparison.Ordinal);
    }

    [Fact]
    public void A_management_API_call_is_logged_with_its_method_route_status_and_time()
    {
        Service.RecordApiCall("GET", "/api/backups", 404, TimeSpan.FromMilliseconds(3));
        Service.RecordApiCall("GET", "/api/logs", 500, TimeSpan.FromMilliseconds(3));
        Service.RecordApiCall("POST", "/api/backup", 200, TimeSpan.FromMilliseconds(3));
        Service.RecordApiCall("PUT", "/api/config", 400, TimeSpan.FromMilliseconds(3));

        var calls = _logs.Entries.Select(entry => entry.Message).Where(message => message.StartsWith("管理 API", StringComparison.Ordinal)).ToList();
        Assert.Equal(
            ["管理 API GET /api/backups 返回 404，耗时 3 毫秒", "管理 API GET /api/logs 返回 500，耗时 3 毫秒",
             "管理 API POST /api/backup 返回 200，耗时 3 毫秒", "管理 API PUT /api/config 返回 400，耗时 3 毫秒"],
            calls);
    }

    [Fact]
    public void The_startup_is_logged_with_the_version_the_port_and_the_memory_path()
    {
        _ = Service;

        var startup = Assert.Single(_logs.Entries, entry => entry.Message.Contains(ProductVersion.Current, StringComparison.Ordinal)).Message;
        Assert.Contains(Service.Config.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), startup, StringComparison.Ordinal);
        Assert.Contains(Service.Config.MemoryPath, startup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_notice_is_shown_to_the_user_and_logged_at_its_level_and_so_is_the_backup()
    {
        Configure(decisionKey: "plain-key-value");
        var notices = new List<string>();
        using var service = _home.Tacit().OpenService(notices.Add, _logs);

        await service.WritePageAsync(Create("my-app/plain-key"));
        await service.SettledAsync();

        Assert.Contains(notices, notice => notice.Contains("明文密钥", StringComparison.Ordinal));
        Assert.Contains(_logs.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("明文密钥", StringComparison.Ordinal));
        Assert.Contains(_logs.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("自动备份已写入", StringComparison.Ordinal));
    }
}
