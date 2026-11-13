using System.Globalization;
using TacitMemory.Core.Configuration;
using TacitMemory.Core.Localization;

namespace TacitMemory.Core;

/// <summary>
/// <c>config.yaml</c> for the settings page: the settings as the file holds them, as the service runs them and at their
/// defaults, each in the file's shape, with what each key accepts. Secrets carry no value here. <see cref="Hash"/> names
/// this content of the file; a save based on it is refused once the file has changed.
/// </summary>
/// <param name="File">The settings as <c>config.yaml</c> holds them now.</param>
/// <param name="Running">The settings the service runs with: they differ from <see cref="File"/> where a setting waits for a restart, or where the file was edited by hand since the service last loaded it.</param>
/// <param name="Defaults">The value each key takes when the file leaves it out; a key with no default is <c>null</c>.</param>
public sealed record ConfigView(string Hash, ConfigFile File, ConfigFile Running, ConfigFile Defaults, IReadOnlyList<ConfigKey> Keys);

/// <summary>What one key of <c>config.yaml</c> accepts, by its key path, such as <c>retrieval.topK</c>.</summary>
/// <param name="Min">The smallest number the key accepts; <c>null</c> for a key that is not a number.</param>
/// <param name="Max">The largest number the key accepts; <c>null</c> for a key that is not a number.</param>
/// <param name="Choices">The values the key accepts when they are fixed; <c>null</c> otherwise.</param>
/// <param name="Restart">Whether a saved change waits for the service to restart instead of taking effect at once.</param>
/// <param name="ReadOnly">Whether the settings page shows the key without letting it change.</param>
public sealed record ConfigKey(string Path, double? Min, double? Max, IReadOnlyList<string>? Choices, bool Restart, bool ReadOnly);

/// <summary>The result of a saved config: the hash of the file as it was written.</summary>
/// <param name="Restart">The settings saved with a value the service will take only when it restarts.</param>
public sealed record ConfigSaved(string Hash, IReadOnlyList<RestartChange> Restart);

/// <summary>
/// A setting that waits for a restart, by its key path, with the value the service runs and the value the file holds, both
/// as <c>config.yaml</c> would write them.
/// </summary>
public sealed record RestartChange(string Path, string Running, string Saved)
{
    /// <summary>The settings that wait for a restart and differ between <paramref name="running"/> and <paramref name="saved"/>, in a fixed order.</summary>
    internal static IReadOnlyList<RestartChange> Between(TacitConfig running, TacitConfig saved)
    {
        static string Host(TacitConfig config) => config.ExtraAddress?.ToString() ?? ConfigDefaults.Host;

        (string Path, string Running, string Saved)[] all =
        [
            ("server.port", running.Port.ToString(CultureInfo.InvariantCulture), saved.Port.ToString(CultureInfo.InvariantCulture)),
            ("server.host", Host(running), Host(saved)),
            ("memory.path", running.MemoryPath, saved.MemoryPath),
            ("language", running.Language.Code(), saved.Language.Code()),
            ("limits.page.minAliases",
                running.Limits.Page.MinAliases.ToString(CultureInfo.InvariantCulture),
                saved.Limits.Page.MinAliases.ToString(CultureInfo.InvariantCulture)),
        ];
        return [.. all.Where(item => item.Running != item.Saved).Select(item => new RestartChange(item.Path, item.Running, item.Saved))];
    }
}

/// <summary>One reason a config does not validate: the key path at fault and the message, in the memory language.</summary>
public sealed record ConfigProblem(string Path, string Message);
