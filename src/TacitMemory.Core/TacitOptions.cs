using System.Globalization;
using TacitMemory.Core.Configuration;
using TacitMemory.Core.Upkeep;

namespace TacitMemory.Core;

/// <summary>Where TacitMemory keeps its files and which machine settings it reads.</summary>
public sealed record TacitOptions
{
    /// <summary>The TacitMemory home directory: <c>config.yaml</c>, <c>prompts/</c>, <c>bindings.json</c>.</summary>
    public required string TacitHome { get; init; }

    /// <summary>The user's home directory; client files and <c>~</c> in configured paths resolve against it.</summary>
    public required string UserHome { get; init; }

    /// <summary>The system UI culture, used to pick the memory language when none is given.</summary>
    public CultureInfo UiCulture { get; init; } = CultureInfo.CurrentUICulture;

    /// <summary>The clock; dates written into memory are its local date.</summary>
    public TimeProvider Time { get; init; } = TimeProvider.System;

    /// <summary>
    /// The clock of the auxiliary model requests: their timeouts, retry pauses and budget. Null gives them <see cref="Time"/>.
    /// </summary>
    public TimeProvider? ModelTime { get; init; }

    /// <summary>
    /// The values of the secret environment variables, by name (see <c>SecretVariables</c>). <see cref="FromEnvironment"/>
    /// fills it from the machine; it is empty otherwise, so nothing is read from the environment unless asked.
    /// </summary>
    public IReadOnlyDictionary<string, string> Secrets { get; init; } = new Dictionary<string, string>();

    /// <summary>The clock the auxiliary model requests run on.</summary>
    internal TimeProvider ModelClock => ModelTime ?? Time;

    /// <summary>Tells the service of changes to its sources in place of the file watchers; for tests. Null watches the files.</summary>
    internal ManualSources? Sources { get; init; }

    /// <summary>Today's date on this machine.</summary>
    internal DateOnly Today => DateOnly.FromDateTime(Time.GetLocalNow().DateTime);

    /// <summary>
    /// Test seam: awaited by the automatic backup once it is packing, before it writes the archive. Null outside tests, and
    /// never part of the public contract.
    /// </summary>
    internal Func<Task>? BeforeBackupPack { get; init; }

    /// <summary>
    /// Options for this machine: <c>TACIT_HOME</c> from the environment variable of that name, otherwise
    /// <c>~/.tacit</c>, and the secret environment variables that are set.
    /// </summary>
    public static TacitOptions FromEnvironment()
    {
        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var tacitHome = Environment.GetEnvironmentVariable("TACIT_HOME");
        return new TacitOptions
        {
            TacitHome = string.IsNullOrWhiteSpace(tacitHome) ? Path.Combine(userHome, ".tacit") : Path.GetFullPath(tacitHome),
            UserHome = userHome,
            Secrets = SecretVariables.All
                .Select(name => (Name: name, Value: Environment.GetEnvironmentVariable(name)))
                .Where(variable => !string.IsNullOrWhiteSpace(variable.Value))
                .ToDictionary(variable => variable.Name, variable => variable.Value!),
        };
    }
}
