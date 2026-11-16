using TacitMemory.Core.Upkeep;

namespace TacitMemory.Core.Tests;

internal sealed partial class TestHome
{
    /// <summary>
    /// The changes a test tells the services opened here of, in place of the file watchers; null, the default, watches the
    /// files. A test that changes a file by hand names the change here and then settles the service.
    /// </summary>
    public ManualSources? Sources { get; private set; }

    /// <summary>A home whose services hear of changes through <see cref="Sources"/> only.</summary>
    public static TestHome WithManualSources()
    {
        var home = new TestHome();
        home.UseManualSources();
        return home;
    }

    /// <summary>Makes the services opened from now on hear of changes through <see cref="Sources"/> only.</summary>
    public ManualSources UseManualSources() => Sources ??= new ManualSources();

    partial void WithSources(ref TacitOptions options)
    {
        if (Sources is not null)
            options = options with { Sources = Sources };
    }
}
