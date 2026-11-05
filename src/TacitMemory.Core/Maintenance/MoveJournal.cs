namespace TacitMemory.Core.Maintenance;

/// <summary>
/// Moves files and folders and remembers each move, so a sequence of moves that fails part way can be undone in reverse
/// order. A move never overwrites: a destination that exists makes the move fail.
/// </summary>
internal sealed class MoveJournal
{
    private readonly List<(string From, string To)> _moves = [];

    /// <summary>Moves a file or a folder and records the move.</summary>
    public void Move(string from, string to)
    {
        if (Directory.Exists(from))
            Directory.Move(from, to);
        else
            File.Move(from, to, overwrite: false);
        _moves.Add((from, to));
    }

    /// <summary>
    /// Puts every recorded move back, latest first. A move that cannot be put back does not stop the others; the failures
    /// are reported together once all of them were tried.
    /// </summary>
    /// <exception cref="IOException">Some moves could not be put back; the message names them.</exception>
    public void Undo()
    {
        var failures = new List<string>();
        for (var i = _moves.Count - 1; i >= 0; i--)
        {
            var (from, to) = _moves[i];
            try
            {
                if (Directory.Exists(to))
                    Directory.Move(to, from);
                else
                    File.Move(to, from, overwrite: false);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                failures.Add($"{to} -> {from}: {e.Message}");
            }
        }

        _moves.Clear();
        if (failures.Count > 0)
            throw new IOException(string.Join("; ", failures));
    }

    /// <summary>Forgets the recorded moves, so they can no longer be undone; called once the moves are final.</summary>
    public void Forget() => _moves.Clear();

    /// <summary>
    /// Deletes a folder the restore made. Read-only flags are cleared first, because git writes read-only pack files and a
    /// plain delete fails on them.
    /// </summary>
    public static void DeleteTree(string path)
    {
        if (!Directory.Exists(path))
            return;
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(path, recursive: true);
    }
}
