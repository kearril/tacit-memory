using TacitMemory.Core.Persona;

namespace TacitMemory.Core;

/// <summary>
/// The persona and its candidates as the memory page shows them, read from the files as they are now. Reading counts
/// nothing and changes no file. A missing file reads as empty; what a file holds in the wrong form is kept where it can be
/// read, and listed among that file's problems.
/// </summary>
/// <param name="PersonaFile"><c>persona.md</c>: its path and problems.</param>
/// <param name="Global">The four global sections in their order, each present even when empty, then any section with a heading that is not one of them.</param>
/// <param name="Domains">The domain blocks, by label.</param>
/// <param name="Workstreams">The workstream blocks, by name.</param>
/// <param name="CandidatesFile"><c>candidates.md</c>: its path and problems.</param>
/// <param name="Candidates">The candidates, in file order.</param>
/// <param name="RejectionList">The rejected candidates, in file order.</param>
public sealed record PersonaReport(
    MemoryFileInfo PersonaFile,
    IReadOnlyList<PersonaSectionView> Global,
    IReadOnlyList<PersonaBlockView> Domains,
    IReadOnlyList<PersonaBlockView> Workstreams,
    MemoryFileInfo CandidatesFile,
    IReadOnlyList<CandidateView> Candidates,
    IReadOnlyList<CandidateView> RejectionList);

/// <summary>A memory file: where it is, and the problems consolidation reports for it, in the report's words.</summary>
/// <param name="Path">The file's full path, whether or not it exists.</param>
public sealed record MemoryFileInfo(string Path, IReadOnlyList<FileProblem> Problems);

/// <summary>One problem of a memory file.</summary>
/// <param name="Line">The line, from 1, when the problem is on one; <c>null</c> for a section or the whole file.</param>
/// <param name="Location">Where the problem is, as the consolidation report writes it.</param>
/// <param name="Kind">What kind of problem it is.</param>
/// <param name="Description">What is wrong.</param>
public sealed record FileProblem(int? Line, string Location, string Kind, string Description);

/// <summary>A section of the persona or of a scope block.</summary>
/// <param name="Section">Which of the four it is; <c>null</c> for a heading that is not one of them, or text before the first heading.</param>
/// <param name="Heading">The heading as written; <c>null</c> for text before the first heading.</param>
/// <param name="Entries">Its entries and the lines that are not entries, in file order.</param>
public sealed record PersonaSectionView(PersonaSection? Section, string? Heading, IReadOnlyList<PersonaEntryView> Entries);

/// <summary>An entry of the persona, or a line in its place that is not one.</summary>
/// <param name="Line">The entry's first line in the file, from 1.</param>
/// <param name="Code">The short code <c>write_persona</c> takes; <c>null</c> for a line that is not an entry.</param>
/// <param name="Text">The entry's first line without its marker, or the line as written.</param>
/// <param name="Lines">Its continuation lines that are not comments, trimmed, in order.</param>
/// <param name="Sources">Its source comments, in order.</param>
public sealed record PersonaEntryView(int Line, string? Code, string Text, IReadOnlyList<string> Lines, IReadOnlyList<SourceView> Sources);

/// <summary>A domain or workstream block of the persona.</summary>
/// <param name="Name">The domain's label or the workstream's name.</param>
/// <param name="Line">The block's heading line, from 1.</param>
/// <param name="Trigger">The domain's trigger; <c>null</c> for a workstream, or a domain without one.</param>
/// <param name="Sections">Its sections that are one of the four, in their order, then any other.</param>
public sealed record PersonaBlockView(string Name, int Line, string? Trigger, IReadOnlyList<PersonaSectionView> Sections);

/// <summary>A candidate for the persona, or a rejected one.</summary>
/// <param name="Title">The entry as it would be written into the persona.</param>
/// <param name="Section">The section it belongs to.</param>
/// <param name="Scope"><c>global</c>, <c>workstream:&lt;name&gt;</c> or <c>domain:&lt;label&gt;</c>.</param>
/// <param name="Occurrences">The days the user said it, in file order.</param>
public sealed record CandidateView(string Title, PersonaSection Section, string Scope, IReadOnlyList<CandidateOccurrenceView> Occurrences);

/// <summary>A day the user said a candidate.</summary>
/// <param name="Date">The day, written yyyy-MM-dd.</param>
/// <param name="Quote">The user's words.</param>
/// <param name="Rejected">Whether this is the day it was rejected.</param>
public sealed record CandidateOccurrenceView(string Date, string Quote, bool Rejected);
