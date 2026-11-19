namespace TacitMemory.Core.Tests;

/// <summary>
/// The automatic backup packs the repository inside the write queue: a write that arrives while the backup is packing
/// waits for it, so the two never touch the same files or git locks.
/// </summary>
public sealed class BackupRaceTests : IDisposable
{
    /// <summary>A bound on each await: a wait that never ends fails here instead of hanging.</summary>
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);

    /// <summary>How long a write must stay pending while the backup is packing to count as waiting for it.</summary>
    private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(500);

    private readonly TestHome _home = new();
    private readonly TaskCompletionSource _packing = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private MemoryService? _service;

    public BackupRaceTests() => _home.InitReady();

    public void Dispose()
    {
        // Release a pause a failing test left behind, so the service can shut down.
        _release.TrySetResult();
        _service?.Dispose();
        _home.Dispose();
    }

    private MemoryService Service => _service ??= new Tacit(_home.Options() with
    {
        BeforeBackupPack = () =>
        {
            _packing.TrySetResult();
            return _release.Task;
        },
    }).OpenService(_ => { }, NullLoggerFactory.Instance);

    private static WritePageRequest Create(string page) => new()
    {
        Op = "create",
        Type = ToolValues.PageTypes.Work,
        Page = page,
        Title = "数据库升级后扩展丢失",
        Aliases = ["全文检索插件失效", "升级后搜索不到中文"],
        Sections = [new("现象", "升级后中文搜索没有结果。")],
    };

    [Fact]
    public async Task A_write_that_arrives_while_the_backup_packs_waits_for_it_to_finish()
    {
        // The first write of the day schedules the automatic backup, which pauses while it packs.
        var first = await Service.Tools.WritePageAsync(Create("tacit-memory/first")).WaitAsync(Guard);
        Assert.False(first.IsError, first.Text);
        await _packing.Task.WaitAsync(Guard);

        var second = Service.Tools.WritePageAsync(Create("tacit-memory/second"));
        var finished = await Task.WhenAny(second, Task.Delay(Window));

        Assert.False(ReferenceEquals(finished, second), "The write finished while the backup was still packing.");
        _release.SetResult();
        var result = await second.WaitAsync(Guard);
        Assert.False(result.IsError, result.Text);
        await Service.SettledAsync().WaitAsync(Guard);
    }
}
