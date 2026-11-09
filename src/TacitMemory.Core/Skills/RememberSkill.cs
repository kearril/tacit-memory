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
        1. Review this session for what is worth recording, by the "Worth recording" rules.
        2. Before writing a detail page, search with recall. Update the page that covers the topic with write_page (upsert_section, using the hash recall shows); create a page only when no existing page fits.
        3. Write one activity with write_recent: what the user did and worked out, in one line, with the project when it belongs to one. If this session already wrote the same matter, rewrite that activity with replaces instead of adding another: its short code is in the earlier reply. An activity that is not added because today already has one on the same matter comes back with that activity's code and the next step.
        4. Persona: write each preference or working rule you observed in this session as the {MemorySkill.Name} skill describes. Use op=propose for what you observed, and op=add only for what the user explicitly asked you to remember. Only the user sets a red line. If something contradicts a persona entry, propose nothing and ask the user first.
        5. When done, list what you wrote, one line each: pages by page ID, activities and persona entries by short code, candidates by their text. Do not ask for confirmation before writing, except as step 5 says.
        """;

    /// <summary>The whole file. It depends on the code alone, so every installation gets the same text.</summary>
    public static string Compose() => CommandSkill.Compose(Name, Description, Body);
}
