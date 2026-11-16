namespace TacitMemory.Core.Tests;

/// <summary>
/// Waits for what the file watchers deliver, for the few tests that check the watchers themselves. The other tests tell
/// the service of a change through <see cref="TestHome.Sources"/> and settle it, which needs no waiting.
/// </summary>
internal static class Watchers
{
    /// <summary>How long a watcher event may take to arrive.</summary>
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);

    /// <summary>Waits until the condition holds, settling the service each round so a refresh the event scheduled runs at once.</summary>
    public static Task Eventually(MemoryService service, Func<bool> condition) =>
        Eventually(service, () => Task.FromResult(condition()));

    /// <summary>Waits until the condition holds, settling the service each round so a refresh the event scheduled runs at once.</summary>
    public static async Task Eventually(MemoryService service, Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + Guard;
        while (true)
        {
            await service.SettledAsync();
            if (await condition())
                return;
            Assert.True(DateTime.UtcNow < deadline, $"the watcher's change did not show within {Guard.TotalSeconds} s");
            await Task.Delay(50);
        }
    }
}
