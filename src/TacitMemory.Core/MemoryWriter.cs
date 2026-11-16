using Microsoft.Extensions.Logging;
using TacitMemory.Core.Git;
using TacitMemory.Core.Index;
using TacitMemory.Core.Injection;
using TacitMemory.Core.Logging;
using TacitMemory.Core.Pages;
using TacitMemory.Core.Tools;

namespace TacitMemory.Core;

/// <summary>The kind of memory file a write changes, which decides what follows it.</summary>
internal enum WrittenSource
{
    Page,
    Activities,
    Persona,
    Candidates,
}

/// <summary>
/// One write: the new text of one memory file and the subject of its commit, without the <c>(with user edits)</c> suffix,
/// which the writer adds. <paramref name="Page"/> names the page when the source is <see cref="WrittenSource.Page"/>.
/// </summary>
internal sealed record MemoryWrite(string RelativePath, string Text, bool HasBom, string Subject, WrittenSource Source, PageId? Page = null);

/// <summary>
/// How a write ended: whether the file was written, whether its commit went through, and the notes to add to the agent's
/// reply, in order (why the commit failed, then that the embedding model is down), empty when there are none.
/// </summary>
internal sealed record WriteOutcome(bool Written, bool Committed, IReadOnlyList<string> Notes)
{
    public static readonly WriteOutcome Unchanged = new(false, true, []);

    /// <summary>The tool's reply: <paramref name="reply"/> with the notes, marked as an error when the commit failed.</summary>
    public ToolResult Reply(string reply)
    {
        var text = string.Join("\n\n", [reply, .. Notes]);
        return Committed ? ToolText.Ok(text) with { Written = Written } : new ToolResult(text, IsError: true, Written: Written);
    }
}

/// <summary>
/// Writes one memory file and everything that follows from it. The file is replaced atomically and committed alone; a file
/// that held changes the user had not committed is committed with them, and the subject says so. Once the file is written,
/// whether or not the commit went through, the index follows it, the units the agent can retrieve get their vectors, and
/// <paramref name="written"/> hears which kind of source changed, for the follow-up that can wait. Callers serialize writes.
/// </summary>
internal sealed class MemoryWriter(
    TacitConfig config, ServiceLog log, MemoryIndex index, VectorSync vectors, Action<WrittenSource> written)
{
    private readonly GitCli _git = new(config.MemoryPath, config.Language);

    public async Task<WriteOutcome> WriteAsync(MemoryWrite write, CancellationToken cancellationToken)
    {
        var path = MemoryLayout.Full(config.MemoryPath, write.RelativePath);
        var exists = File.Exists(path);
        if (exists && TextFile.Read(path) is { } current && current.Text == write.Text && current.HasBom == write.HasBom)
            return WriteOutcome.Unchanged;

        // Checked before the write: a file that already had user edits gets that noted in its commit subject.
        var state = exists ? await _git.FileStateAsync(write.RelativePath, cancellationToken) : FileState.Untracked;
        TextFile.Write(path, write.Text, write.HasBom);

        var notes = new List<string>();
        var subject = exists && state != FileState.Committed ? $"{write.Subject} (with user edits)" : write.Subject;
        var committed = true;
        try
        {
            await _git.CommitFileAsync(subject, write.RelativePath, tracked: state != FileState.Untracked, cancellationToken);
        }
        catch (TacitException e) when (e.Error == TacitError.GitFailed)
        {
            log.Notify(LogLevel.Warning, e.Message);
            committed = false;
            notes.Add($"Not committed: {write.RelativePath} is saved, but git failed ({e.Message}). Nothing to redo: it is committed with the next write to this file.");
        }

        // The index follows the file right away; the watcher's later event finds the hash unchanged.
        switch (write.Source)
        {
            case WrittenSource.Page:
                index.SyncPage(write.Page!);
                break;
            case WrittenSource.Activities:
                index.SyncActivities();
                break;
            default:
                index.SyncJudgments();
                break;
        }
        // Pages and activities are what recall returns, so they get their vectors before the reply; persona entries and
        // candidates get theirs when a judgment next looks for neighbours.
        if (write.Source is WrittenSource.Page or WrittenSource.Activities && !await vectors.FillAsync(cancellationToken))
            notes.Add($"Note: the embedding model is unavailable, so until it is back this {(write.Source == WrittenSource.Page ? "page" : "activity")} is found by full-text search only.");

        written(write.Source);
        return new WriteOutcome(true, committed, notes);
    }
}
