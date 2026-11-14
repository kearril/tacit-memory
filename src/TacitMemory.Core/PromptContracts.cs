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
public sealed record PromptDocumentView(PromptName Name, string Text, bool BuiltIn, string? Hash);

/// <summary>
/// A prompt document as a save or a return to the built-in default left it, which the service runs with from now on.
/// </summary>
/// <param name="Notices">What did not go as planned: a resident block that could not be rendered again, with the reason.</param>
public sealed record PromptSaved(PromptDocumentView Document, IReadOnlyList<string> Notices);

/// <summary>The body of a prompt document's save: the hash of the file it was read from (<c>null</c> when there was none) and the new text.</summary>
public sealed record PromptSave(string? Hash, string Text);

/// <summary>The four prompt documents, in the order of <see cref="PromptName"/>.</summary>
public sealed record PromptList(IReadOnlyList<PromptDocumentView> Documents);
