using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using TacitMemory.Core.Configuration;
using TacitMemory.Core.Git;
using TacitMemory.Core.Localization;

namespace TacitMemory.Core.Backup;

/// <summary>What a backup is for. The manifest's <c>kind</c> names it, as <see cref="BackupArchive"/> writes it.</summary>
internal enum BackupKind
{
    Auto,
    Manual,
    PreRestore,
    Cloud,
}

/// <summary>The memory repository's HEAD commit: its hash and its subject line.</summary>
internal sealed record MemoryCommit(string Hash, string Subject)
{
    /// <summary>Reads HEAD of the repository at <paramref name="memoryPath"/>; the one place a backup and a restore preview read it.</summary>
    public static async Task<MemoryCommit> ReadAsync(string memoryPath, MemoryLanguage language, CancellationToken cancellationToken)
    {
        var git = new GitCli(memoryPath, language);
        var hash = (await git.RunAsync(cancellationToken, "rev-parse", "HEAD")).Trim();
        var subject = (await git.RunAsync(cancellationToken, "log", "-1", "--format=%s", hash)).Trim();
        return new MemoryCommit(hash, subject);
    }
}

/// <summary>File names and time stamps of the backups, so every kind is named the same way.</summary>
internal static class BackupNames
{
    /// <summary>The local time as <c>yyyy-MM-dd-HHmmss</c>, the stamp of the manual and the safety backups.</summary>
    public static string Stamp(TacitOptions options) =>
        options.Time.GetLocalNow().ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture);
}

/// <summary>
/// The one packager every backup goes through, whatever its kind: the memory repository (with <c>.git</c>, without
/// <c>.cache</c>), <c>config.yaml</c>, <c>bindings.json</c>, the prompt documents and the manifest. It writes
/// <c>destination.partial</c> and renames it, so a half-written file never counts as a backup. It never overwrites: a
/// destination that already exists is refused. It does not lock the repository; a caller that needs a state no write
/// is changing packs under the write gate.
/// </summary>
internal sealed class BackupArchive(TacitOptions options, TacitConfig config)
{
    private static readonly string[] ConfigFiles = ["config.yaml", "bindings.json"];

    /// <summary>Writes an archive of the given kind to <paramref name="destination"/> and returns its size in bytes.</summary>
    /// <exception cref="TacitException">The destination already exists (<see cref="TacitError.BackupExists"/>).</exception>
    public async Task<long> WriteAsync(BackupKind kind, string destination, CancellationToken cancellationToken)
    {
        if (File.Exists(destination))
            throw new TacitException(TacitError.BackupExists, BackupMessages.BackupExists.Format(config.Language, destination));

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var partial = destination + ".partial";
        var commit = await MemoryCommit.ReadAsync(config.MemoryPath, config.Language, cancellationToken);

        try
        {
            var entries = new List<string>();
            using (var stream = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                foreach (var relative in MemoryFiles(config.MemoryPath, ""))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AddFile(zip, Path.Combine(config.MemoryPath, relative), "memory/" + relative);
                    entries.Add("memory/" + relative);
                }
                foreach (var name in ConfigFiles)
                {
                    var path = Path.Combine(options.TacitHome, name);
                    if (!File.Exists(path))
                        continue;
                    cancellationToken.ThrowIfCancellationRequested();
                    AddFile(zip, path, name);
                    entries.Add(name);
                }
                var prompts = PromptFiles.Folder(options.TacitHome);
                if (Directory.Exists(prompts))
                {
                    foreach (var relative in MemoryFiles(prompts, ""))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        AddFile(zip, Path.Combine(prompts, relative), "prompts/" + relative);
                        entries.Add("prompts/" + relative);
                    }
                }

                var manifest = new BackupManifest(options.Time.GetUtcNow(), ProductVersion.Current, KindName(kind), commit.Hash, commit.Subject, entries);
                using var manifestStream = zip.CreateEntry("manifest.json").Open();
                JsonSerializer.Serialize(manifestStream, manifest, BackupManifest.Json);
            }

            File.Move(partial, destination, overwrite: false);
            return new FileInfo(destination).Length;
        }
        finally
        {
            if (File.Exists(partial))
                File.Delete(partial);
        }
    }

    private static string KindName(BackupKind kind) => kind switch
    {
        BackupKind.Auto => "auto",
        BackupKind.Manual => "manual",
        BackupKind.PreRestore => "pre-restore",
        BackupKind.Cloud => "remote",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>
    /// Every file under <paramref name="directory"/> as a relative path with <c>/</c> separators, skipping any
    /// <c>.cache</c> directory.
    /// </summary>
    private static IEnumerable<string> MemoryFiles(string directory, string prefix)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            var name = Path.GetFileName(path);
            if (Directory.Exists(path))
            {
                if (name == ".cache")
                    continue;
                foreach (var nested in MemoryFiles(path, prefix + name + "/"))
                    yield return nested;
            }
            else
            {
                yield return prefix + name;
            }
        }
    }

    private static void AddFile(ZipArchive zip, string path, string entryName)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        // Git may hold its files open while another write commits; read them the way git itself shares them.
        using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var destination = entry.Open();
        source.CopyTo(destination);
    }
}
