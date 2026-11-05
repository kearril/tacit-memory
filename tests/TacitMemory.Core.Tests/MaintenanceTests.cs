namespace TacitMemory.Core.Tests;

/// <summary>
/// A maintenance operation holds the memory to itself. The memory tools, the server instructions and the other maintenance
/// operations that arrive while it runs wait for it and run once it has finished. The embedding model is held in the
/// rebuild's vector phase, so each test decides when the rebuild ends. A wait that must not complete yet is checked after
/// a short pause; a broken queue would finish within that pause, so the check cannot pass by accident.
/// </summary>
public sealed class MaintenanceTests : IDisposable
{
    /// <summary>A bound on each await: a wait that never ends fails here instead of hanging.</summary>
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);

    /// <summary>How long a queued call is given to finish before the test checks that it has not.</summary>
    private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(300);

    private readonly TestHome _home = new();
    private readonly ModelDouble _models = new();
    private MemoryService? _service;

    public MaintenanceTests() => _home.InitReady(models: _models);

    public void Dispose()
    {
        _models.ReleaseEmbeddings();
        _service?.Dispose();
        _models.Dispose();
        _home.Dispose();
    }

    private MemoryService Service => _service ??= _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);

    private string RestorePage => Path.Combine(_home.DefaultMemory, "pages", "tacit-memory", "restore.md");

    private Task<ToolResult> CreatePage(string page, string title, string body) => Service.WritePageAsync(new WritePageRequest
    {
        Op = "create",
        Page = page,
        Title = title,
        Aliases = [title + "的坑", title + "问题"],
        Sections = [new("要点", body)],
    });

    private async Task SeedBackupPage()
    {
        var result = await CreatePage("tacit-memory/backup", "备份", "每日自动备份到 zip");
        Assert.False(result.IsError, result.Text);
    }

    /// <summary>Starts a rebuild and returns once it is in its vector phase, with its embedding request held.</summary>
    private async Task<Task<ReindexResult>> StartHeldRebuildAsync()
    {
        var reached = _models.EmbeddingCalls + 1;
        _models.HoldEmbeddings();
        var rebuild = Service.ReindexAsync();
        await _models.EmbeddingRequestsReached(reached).WaitAsync(Guard);
        return rebuild;
    }

    [Fact]
    public async Task A_write_a_recall_and_the_server_instructions_that_arrive_during_a_rebuild_wait_for_it_and_then_run()
    {
        await SeedBackupPage();
        var rebuild = await StartHeldRebuildAsync();

        var write = CreatePage("tacit-memory/restore", "恢复", "从备份恢复记忆仓库");
        var recall = Recall("备份");
        var instructions = Service.ServerInstructionsAsync();
        await Task.Delay(Pause);

        Assert.False(File.Exists(RestorePage), "The write reached the disk while the rebuild still held the memory.");
        Assert.False(recall.IsCompleted, "The recall returned while the rebuild still held the memory.");
        Assert.False(instructions.IsCompleted, "The server instructions were built while the rebuild still held the memory.");

        _models.ReleaseEmbeddings();

        Assert.False((await rebuild.WaitAsync(Guard)).VectorsPending);
        Assert.False((await write.WaitAsync(Guard)).IsError);
        Assert.Contains("备份", (await recall.WaitAsync(Guard)).Text);
        Assert.NotEmpty(await instructions.WaitAsync(Guard));
        Assert.True(File.Exists(RestorePage));
    }

    [Fact]
    public async Task A_second_rebuild_waits_for_the_first_and_both_finish()
    {
        await SeedBackupPage();
        var first = await StartHeldRebuildAsync();

        var second = Service.ReindexAsync();
        await Task.Delay(Pause);

        Assert.False(second.IsCompleted, "The second rebuild started while the first still held the memory.");
        _models.ReleaseEmbeddings();

        var done = await first.WaitAsync(Guard);
        var again = await second.WaitAsync(Guard);
        Assert.Equal(done.Units, again.Units);
        Assert.Equal(done.Vectors, again.Vectors);
    }

    private Task<ToolResult> Recall(string query) => Service.RecallAsync(new RecallRequest { Query = query });
}
