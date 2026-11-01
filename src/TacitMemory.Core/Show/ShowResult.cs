namespace TacitMemory.Core.Show;

/// <summary>What <c>tacit show</c> prints from the facade. The tool descriptions are not a target: see <see cref="Tacit.ShowTools"/>.</summary>
public enum ShowTarget
{
    /// <summary>The server instructions a new session receives.</summary>
    Instructions,

    /// <summary>The shell and the four guidance paragraphs of the resident block.</summary>
    Resident,

    /// <summary>The memory skill, <c>SKILL.md</c>, as the agent reads it.</summary>
    Skill,

    /// <summary>The <c>remember</c> prompt as it would be generated now.</summary>
    Remember,
}

/// <summary>Where a part of what the agent receives comes from.</summary>
public enum ShowSource
{
    /// <summary>Written in the code, or generated from the memory repository as it stands now.</summary>
    Code,

    /// <summary>The built-in default, used because the user has no file that replaces it.</summary>
    BuiltInDefault,

    /// <summary>A document the user wrote in <c>TACIT_HOME/prompts/</c>.</summary>
    UserFile,
}

/// <summary>One part of what the agent receives.</summary>
/// <param name="Key">A stable name for the part.</param>
/// <param name="Source">Where the part comes from.</param>
/// <param name="Path">The file a <see cref="ShowSource.UserFile"/> part was read from.</param>
/// <param name="Text">The part's text, as the agent receives it.</param>
/// <param name="Limit">The part's own limit in characters, when it has one.</param>
public sealed record ShowPart(string Key, ShowSource Source, string? Path, string Text, int? Limit = null)
{
    /// <summary>The length of <see cref="Text"/> in characters.</summary>
    public int Length => Text.Length;
}

/// <summary>A length in characters, and the limit it must stay within.</summary>
public sealed record ShowLimit(int Length, int Limit);

/// <summary>What <see cref="Tacit.Show"/> returns.</summary>
/// <param name="Report">
/// The text to print. With <c>useDefault</c> it is the built-in text alone, so it can be saved as the file that replaces
/// the default; otherwise it is the parts with their sources and lengths, and it names what a running service adds.
/// </param>
/// <param name="Parts">The parts in the order the agent receives them.</param>
/// <param name="Fixed">The fixed part of the server instructions against its limit, when the target has one.</param>
/// <param name="Whole">The whole output against its limit, when the target has one.</param>
public sealed record ShowResult(string Report, IReadOnlyList<ShowPart> Parts, ShowLimit? Fixed = null, ShowLimit? Whole = null);
