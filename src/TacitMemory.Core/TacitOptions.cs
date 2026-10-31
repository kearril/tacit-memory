using System.Globalization;

namespace TacitMemory.Core;

/// <summary>Where TacitMemory keeps its files and which machine settings it reads.</summary>
public sealed record TacitOptions
{
    /// <summary>The TacitMemory home directory: <c>config.yaml</c>, <c>rules.md</c>, <c>bindings.json</c>.</summary>
    public required string TacitHome { get; init; }

    /// <summary>The user's home directory; client files and <c>~</c> in configured paths resolve against it.</summary>
    public required string UserHome { get; init; }

    /// <summary>The system UI culture, used to pick the memory language when none is given.</summary>
    public CultureInfo UiCulture { get; init; } = CultureInfo.CurrentUICulture;

    /// <summary>
    /// Options for this machine: <c>TACIT_HOME</c> from the environment variable of that name, otherwise
    /// <c>~/.tacit</c>.
    /// </summary>
    public static TacitOptions FromEnvironment()
    {
        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var tacitHome = Environment.GetEnvironmentVariable("TACIT_HOME");
        return new TacitOptions
        {
            TacitHome = string.IsNullOrWhiteSpace(tacitHome) ? Path.Combine(userHome, ".tacit") : Path.GetFullPath(tacitHome),
            UserHome = userHome,
        };
    }
}
