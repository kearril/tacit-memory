namespace TacitMemory.Core;

/// <summary>A source comment as the memory page shows it: the day it was written and the user's words, either of which may be left out.</summary>
/// <param name="Date">The day, written yyyy-MM-dd; <c>null</c> when the comment names none.</param>
/// <param name="Quote">The user's words; <c>null</c> when the comment keeps none.</param>
public sealed record SourceView(string? Date, string? Quote);

/// <summary>A source comment in a page section's Markdown: the comment as written, and what it records.</summary>
/// <param name="Comment">The comment as written, <c>&lt;!-- … --&gt;</c>, without the spaces around it.</param>
public sealed record SourceCommentView(string Comment, SourceView Source);
