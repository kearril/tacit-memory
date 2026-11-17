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
/// <param name="Secrets">Where each secret's value comes from, as the file and the environment give it now.</param>
public sealed record ConfigView(
    string Hash, ConfigFile File, ConfigFile Running, ConfigFile Defaults, IReadOnlyList<ConfigKey> Keys, IReadOnlyList<SecretState> Secrets);

/// <summary>
/// Where one secret's value comes from, by its key path: its environment variable, which wins over the file, or
/// <c>config.yaml</c>; <c>null</c> when neither holds it. The value itself is fetched on its own, when asked for.
/// </summary>
public sealed record SecretState(string Path, SecretSource? Source, string Variable);

/// <summary>A secret's value as the service would use it: the environment variable's, else the file's; <c>null</c> when neither holds it.</summary>
public sealed record SecretShown(string? Value);

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
        static string Host(TacitConfig config) => config.ExtraAddress?.ToString() ?? ConfigKeys.LoopbackHost;

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
