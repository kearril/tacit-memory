namespace TacitMemory.Core;

/// <summary>
/// What <c>tacit reindex</c> did: the counts of the rebuilt index, the files it could not read, and the time taken.
/// <paramref name="Report"/> is the summary for the user, in the memory language.
/// </summary>
/// <param name="Files">Files the index holds after the rebuild: pages, activity months, and the persona and candidate files.</param>
/// <param name="Units">Units the index holds.</param>
/// <param name="Vectors">Units that have a vector.</param>
/// <param name="SkippedFiles">Files that are not valid UTF-8, so are not indexed, relative to the memory repository.</param>
/// <param name="VectorsPending">The embedding model could not be reached, so some units wait for a vector; the background fill completes them later.</param>
/// <param name="ElapsedMilliseconds">Time from the start of the rebuild to its end, not counting the wait for other work.</param>
public sealed record ReindexResult(
    int Files,
    int Units,
    int Vectors,
    IReadOnlyList<string> SkippedFiles,
    bool VectorsPending,
    long ElapsedMilliseconds,
    string Report);
