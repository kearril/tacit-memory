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
public sealed record ConfigSaved(string Hash);

/// <summary>One reason a config does not validate: the key path at fault and the message, in the memory language.</summary>
public sealed record ConfigProblem(string Path, string Message);
