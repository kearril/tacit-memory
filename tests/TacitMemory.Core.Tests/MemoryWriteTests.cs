namespace TacitMemory.Core.Tests;

/// <summary>
/// What every write does around the file it changes: the commit, the note when git fails, and the follow-up that runs once the
/// file is written whether or not the commit went through. The rules of each write tool are tested with that tool.
/// </summary>
public sealed class MemoryWriteTests : IDisposable
{
    private readonly TestHome _home = new();
    private readonly List<string> _notices = [];
    private readonly MemoryService _service;

    public MemoryWriteTests()
    {
        _home.InitReady();
        _service = _home.Tacit().OpenService(_notices.Add, NullLoggerFactory.Instance);
    }

    public void Dispose()
    {
        _service.Dispose();
        _home.Dispose();
    }

    private string Memory => _home.DefaultMemory;

    private string Git(params string[] args) => TestHome.Git(Memory, args);

    /// <summary>Makes every git command that writes the index fail, as a crashed git process leaves it.</summary>
    private string LockIndex()
    {
        var path = Path.Combine(Memory, ".git", "index.lock");
        File.WriteAllText(path, "");
        return path;
    }

    private static WritePageRequest Create(string page) => new()
    {
        Op = "create",
        Type = ToolValues.PageTypes.Incident,
        Page = page,
        Title = "数据库升级后扩展丢失",
        Aliases = ["全文检索插件失效", "升级后搜索不到中文"],
        Sections = [new("现象", "升级后中文搜索没有结果。"), new("根因", "PGroonga 扩展没有随升级重装。")],
    };

    private static WritePageRequest Upsert(string page, string section, string body) =>
        new() { Op = "upsert_section", Page = page, Section = section, Body = body };

    private string AutomaticBackup => Path.Combine(_home.TacitHome, "backups", $"auto-{TestHome.Today:yyyy-MM-dd}.zip");

    [Fact]
    public async Task A_page_whose_commit_fails_is_saved_searchable_and_backed_up_and_the_agent_is_told()
    {
        var head = Git("rev-parse", "HEAD").Trim();
        LockIndex();

        var result = await _service.WritePageAsync(Create("tacit-memory/db-upgrade"));

        Assert.True(result.IsError);
        Assert.Contains("Created page tacit-memory/db-upgrade.", result.Text);
        Assert.Contains("Not committed: pages/tacit-memory/db-upgrade.md is saved, but git failed (", result.Text);
        Assert.Contains("Nothing to redo: it is committed with the next write to this file.", result.Text);
        Assert.True(File.Exists(Path.Combine(Memory, "pages", "tacit-memory", "db-upgrade.md")));
        Assert.Equal(head, Git("rev-parse", "HEAD").Trim());
        Assert.Single(_notices, notice => notice.Contains("git", StringComparison.OrdinalIgnoreCase));

        var found = await _service.RecallAsync(new RecallRequest { Query = "PGroonga" });
        Assert.Contains("tacit-memory/db-upgrade", found.Text);

        await _service.SettledAsync();
        Assert.True(File.Exists(AutomaticBackup));
    }

    [Fact]
    public async Task A_write_that_was_not_committed_is_committed_with_the_next_write_to_that_file()
    {
        var index = LockIndex();
        await _service.WritePageAsync(Create("tacit-memory/db-upgrade"));
        File.Delete(index);

        var result = await _service.WritePageAsync(Upsert("tacit-memory/db-upgrade", "修复", "重装 PGroonga 扩展。"));

        Assert.False(result.IsError, result.Text);
        Assert.Equal("pages/tacit-memory/db-upgrade.md", Git("show", "--name-only", "--format=", "HEAD").Trim());
        var committed = Git("show", "HEAD:pages/tacit-memory/db-upgrade.md");
        Assert.Contains("PGroonga 扩展没有随升级重装。", committed);
        Assert.Contains("重装 PGroonga 扩展。", committed);
        Assert.Equal("", Git("status", "--porcelain").Trim());
    }

    [Fact]
    public async Task Changing_a_committed_page_commits_only_that_page_and_leaves_what_the_user_staged()
    {
        await _service.WritePageAsync(Create("tacit-memory/db-upgrade"));
        File.WriteAllText(Path.Combine(Memory, "notes.txt"), "staged by the user\n");
        Git("add", "notes.txt");

        var result = await _service.WritePageAsync(Upsert("tacit-memory/db-upgrade", "修复", "重装 PGroonga 扩展。"));

        Assert.False(result.IsError, result.Text);
        Assert.Equal("pages: upsert tacit-memory/db-upgrade 修复", Git("log", "-1", "--format=%s").Trim());
        Assert.Equal("pages/tacit-memory/db-upgrade.md", Git("show", "--name-only", "--format=", "HEAD").Trim());
        Assert.Equal("A  notes.txt", Git("status", "--porcelain").Trim());
    }

    [Fact]
    public async Task An_activity_whose_commit_fails_is_saved_searchable_and_backed_up_and_the_agent_is_told()
    {
        var head = Git("rev-parse", "HEAD").Trim();
        LockIndex();

        var result = await _service.WriteRecentAsync(new WriteRecentRequest { Text = "全文检索换成 PGroonga，因为中文分词更准。" });

        Assert.True(result.IsError);
        Assert.StartsWith("Added activity [", result.Text);
        Assert.Contains($"Not committed: recent/{TestHome.Today:yyyy-MM}.md is saved, but git failed (", result.Text);
        Assert.Contains("Nothing to redo: it is committed with the next write to this file.", result.Text);
        Assert.Contains("PGroonga", File.ReadAllText(Path.Combine(Memory, "recent", $"{TestHome.Today:yyyy-MM}.md")));
        Assert.Equal(head, Git("rev-parse", "HEAD").Trim());
        Assert.Single(_notices, notice => notice.Contains("git", StringComparison.OrdinalIgnoreCase));

        var found = await _service.RecallAsync(new RecallRequest { Query = "PGroonga", Kind = "activities" });
        Assert.Contains("PGroonga", found.Text);

        await _service.SettledAsync();
        Assert.True(File.Exists(AutomaticBackup));
    }

    [Theory]
    [InlineData("add", "persona.md")]
    [InlineData("propose", "candidates.md")]
    public async Task A_persona_write_whose_commit_fails_is_saved_and_backed_up_and_the_agent_is_told(string op, string file)
    {
        var head = Git("rev-parse", "HEAD").Trim();
        LockIndex();

        var result = await _service.WritePersonaAsync(new WritePersonaRequest
        {
            Op = op,
            Section = "preferences",
            Text = "回答先给结论，再给理由。",
            Source = "先说结论",
        });

        Assert.True(result.IsError);
        Assert.Contains($"Not committed: {file} is saved, but git failed (", result.Text);
        Assert.Contains("Nothing to redo: it is committed with the next write to this file.", result.Text);
        Assert.Contains("回答先给结论，再给理由。", File.ReadAllText(Path.Combine(Memory, file)));
        Assert.Equal(head, Git("rev-parse", "HEAD").Trim());
        Assert.Single(_notices, notice => notice.Contains("git", StringComparison.OrdinalIgnoreCase));

        await _service.SettledAsync();
        Assert.True(File.Exists(AutomaticBackup));
    }
}
