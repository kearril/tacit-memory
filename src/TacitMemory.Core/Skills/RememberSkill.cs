namespace TacitMemory.Core.Skills;

/// <summary>
/// The <c>tacit-remember</c> memory command: the steps for writing the session into memory. It carries no data: an
/// activity written earlier in the session is in the context, a same-topic reply names today's activity on that matter
/// with its code.
/// </summary>
internal static class RememberSkill
{
    public const string Name = "tacit-remember";

    private const string Description =
        "Write this session into the TacitMemory memory now: pages, one activity, persona candidates.";

    private static readonly string Body = $"""
        # Remember this session

        Remember this session in the TacitMemory memory now. Write without asking the user first, except where a step says to ask.
        {CommandSkill.UserText}

        ## Steps
        1. Go through this session against the "Worth recording" rules; skip low-value details when nothing lasting happened.
        2. Pages: `recall` first. Update the page that covers the matter with write_page upsert_section (using the hash recall shows), or use op=update_header to add aliases or related links; create one with its type only when no existing page fits.
        3. Activities with write_recent: one for each workstream this session advanced, with its workstream set (leave workstream unset when none matches; never invent a name), each a single conclusion and its reason. If this session already wrote an activity for a workstream, rewrite it with `replaces`, using its short code from the earlier reply.
        4. Persona: for each correction or stated rule, ask the user once whether to keep it as a lasting rule, then `add` after they agree. Propose an observed habit with `op=propose` only when it repeated or clearly applies across sessions, not for a one-off workaround. If something contradicts a persona entry, ask the user first.
        5. List what you wrote, one line each: pages by page ID, activities and persona entries by short code, candidates by their text.
        """;

    /// <summary>The whole file. It depends on the code alone, so every installation gets the same text.</summary>
    public static string Compose() => CommandSkill.Compose(Name, Description, Body);
}
