using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using TacitMemory.Core.Configuration;
using TacitMemory.Core.Git;
using TacitMemory.Core.Localization;

namespace TacitMemory.Core.Backup;

/// <summary>
/// The daily automatic backup. After a successful write, if <c>TACIT_HOME/backups/</c> holds no automatic backup for
/// today, it zips the memory repository (with <c>.git</c>, without <c>.cache</c>), the config files, the prompt documents and a manifest,
/// then keeps only the newest <c>backup.keep</c> automatic backups. It runs in the background, outside the write
/// queue; a failure only tells the user, and never changes the write that triggered it.
/// </summary>
internal sealed partial class AutomaticBackup(TacitOptions options, TacitConfig config, Action<string> notice)
{
    private static readonly string[] ConfigFiles = ["config.yaml", "bindings.json"];

    private static readonly JsonSerializerOptions ManifestJson = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly string _directory = Path.Combine(options.TacitHome, "backups");

    /// <summary>The last scheduled run. Each run chains onto the one before, so runs never overlap.</summary>
    private Task _completion = Task.CompletedTask;

    /// <summary>Completes when the most recently scheduled run has finished, whether or not it made a backup.</summary>
    internal Task Completion => _completion;

    /// <summary>Starts a backup run in the background and returns at once. Does nothing when backups are off.</summary>
    public void ScheduleIfDue()
    {
        if (config.Backup.Keep == 0)
            return;
        _completion = _completion.ContinueWith(_ => RunIfDueAsync(), TaskScheduler.Default).Unwrap();
    }

    /// <summary>Nothing may escape: nobody awaits this run except tests, and a failure must not disturb anything.</summary>
    private async Task RunIfDueAsync()
    {
        try
        {
            if (File.Exists(Path.Combine(_directory, FileNameFor(options.Today))))
                return;

            await CreateAsync();
            Prune();
            if (config.Models.Decision.ApiKey is not null)
                notice(Messages.BackupPlaintextKey.Format(config.Language, Path.Combine(options.TacitHome, "config.yaml")));
        }
        catch (Exception e)
        {
            notice(Messages.BackupFailed.Format(config.Language, e.Message));
        }
    }

    /// <summary>Writes the archive under a temporary name and renames it, so a half-written file never counts as a backup.</summary>
    private async Task CreateAsync()
    {
        Directory.CreateDirectory(_directory);
        var target = Path.Combine(_directory, FileNameFor(options.Today));
        var partial = target + ".partial";
        var head = (await new GitCli(config.MemoryPath, config.Language)
            .RunAsync(CancellationToken.None, "rev-parse", "HEAD")).Trim();

        try
        {
            var entries = new List<string>();
            using (var stream = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                foreach (var relative in MemoryFiles(config.MemoryPath, ""))
                {
                    AddFile(zip, Path.Combine(config.MemoryPath, relative), "memory/" + relative);
                    entries.Add("memory/" + relative);
                }
                foreach (var name in ConfigFiles)
                {
                    var path = Path.Combine(options.TacitHome, name);
                    if (!File.Exists(path))
                        continue;
                    AddFile(zip, path, name);
                    entries.Add(name);
                }
                var prompts = PromptFiles.Folder(options.TacitHome);
                if (Directory.Exists(prompts))
                {
                    foreach (var relative in MemoryFiles(prompts, ""))
                    {
                        AddFile(zip, Path.Combine(prompts, relative), "prompts/" + relative);
                        entries.Add("prompts/" + relative);
                    }
                }

                var manifest = new Manifest(options.Time.GetUtcNow(), TacitVersion(), head, entries);
                using var manifestStream = zip.CreateEntry("manifest.json").Open();
                JsonSerializer.Serialize(manifestStream, manifest, ManifestJson);
            }

            File.Move(partial, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(partial))
                File.Delete(partial);
        }
    }

    /// <summary>Deletes the oldest automatic backups beyond <c>backup.keep</c>. File names sort by date.</summary>
    private void Prune()
    {
        var backups = Directory.EnumerateFiles(_directory)
            .Where(path => AutomaticName().IsMatch(Path.GetFileName(path)))
            .Order(StringComparer.Ordinal)
            .ToList();
        foreach (var stale in backups.Take(Math.Max(0, backups.Count - config.Backup.Keep)))
            File.Delete(stale);
    }

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

    private static string FileNameFor(DateOnly day) => $"auto-{day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.zip";

    private static string TacitVersion() =>
        typeof(AutomaticBackup).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(AutomaticBackup).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    [GeneratedRegex(@"^auto-\d{4}-\d{2}-\d{2}\.zip$")]
    private static partial Regex AutomaticName();

    /// <summary>The manifest inside every backup.</summary>
    private sealed record Manifest(DateTimeOffset GeneratedAt, string TacitVersion, string MemoryHead, IReadOnlyList<string> Files);
}
