namespace TacitMemory.Core;

/// <summary>The four prompt documents in <c>TACIT_HOME/prompts/</c>, each named after its file.</summary>
public enum PromptName
{
    /// <summary><c>instructions.md</c>: the core rules of the server instructions.</summary>
    Instructions,

    /// <summary><c>rules.md</c>: the recording criteria.</summary>
    Rules,

    /// <summary><c>resident.md</c>: the shell and the guidance of the resident block.</summary>
    Resident,

    /// <summary><c>skill.md</c>: the user's writing conventions at the end of the memory skill.</summary>
    Skill,
}

/// <summary>
/// One prompt document for the prompts page: the text its file holds, or the built-in default when there is no file.
/// <see cref="Hash"/> names the file's content and is <c>null</c> without a file; a save based on it is refused once the
/// file has changed.
/// </summary>
/// <param name="BuiltIn">Whether the file is absent, so the built-in default applies; the default of <c>skill.md</c> is empty.</param>
/// <param name="Measure">The length of <see cref="Text"/> against its limit, as a check of it gives.</param>
/// <param name="Problem">
/// Why the service keeps running the document's previous text instead of what its file holds: the refusal of the file as it
/// was last read. <c>null</c> when the service runs the file, or the built-in text.
/// </param>
public sealed record PromptDocumentView(PromptName Name, string Text, bool BuiltIn, string? Hash, PromptMeasure Measure, string? Problem);

/// <summary>
/// How much of its limit a prompt document's text takes, counted as it is sent. For <c>resident.md</c> and
/// <c>skill.md</c> it is the text's own length and limit. <c>instructions.md</c> and <c>rules.md</c> have no limit of their
/// own: they are counted in the fixed part of the server instructions, beside the other one as the service runs it and
/// the parts the code provides, so the length is that part's and the limit its limit.
/// </summary>
/// <param name="Length">Characters, with LF line breaks; the core rules without the comments that fill their lines, and the recording criteria as they are laid out when sent.</param>
/// <param name="ActivityRoom">
/// For the fixed part only: the characters the server instructions have left for recent activity, their total limit less
/// the fixed part. Below zero when the fixed part is over its limit.
/// </param>
/// <param name="Items">For <c>rules.md</c> only: the items in each of its two sections.</param>
public sealed record PromptMeasure(int Length, int Limit, int? ActivityRoom, PromptItems? Items);

/// <summary>The items in the two sections of <c>rules.md</c>, and the most each may hold.</summary>
public sealed record PromptItems(int WorthRecording, int NotRecorded, int Limit);

/// <summary>A prompt document's text checked without being saved: its measure, and every refusal a save would give.</summary>
public sealed record PromptCheck(PromptMeasure Measure, IReadOnlyList<string> Problems);

/// <summary>The body of a prompt document's check: the text to check.</summary>
public sealed record PromptDraft(string Text);

/// <summary>
/// A prompt document as a save or a return to the built-in default left it, which the service runs with from now on.
/// </summary>
/// <param name="Notices">What did not go as planned: a resident block that could not be rendered again, with the reason.</param>
public sealed record PromptSaved(PromptDocumentView Document, IReadOnlyList<string> Notices);

/// <summary>The body of a prompt document's save: the hash of the file it was read from (<c>null</c> when there was none) and the new text.</summary>
public sealed record PromptSave(string? Hash, string Text);

/// <summary>The four prompt documents, in the order of <see cref="PromptName"/>.</summary>
public sealed record PromptList(IReadOnlyList<PromptDocumentView> Documents);
