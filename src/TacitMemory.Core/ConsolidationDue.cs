namespace TacitMemory.Core;

/// <summary>
/// Whether a consolidation is due, by the rule that brings the consolidation reminder at the end of the server instructions.
/// It only answers the question: a client decides when to ask and what to do with the answer.
/// </summary>
/// <param name="Due">Whether a consolidation is due.</param>
public sealed record ConsolidationDue(bool Due);
