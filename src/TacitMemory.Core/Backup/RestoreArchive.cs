using System.IO.Compression;
using System.Text.Json;
using TacitMemory.Core.Localization;

namespace TacitMemory.Core.Backup;

/// <summary>
/// A backup zip opened for a restore. Opening it validates everything a restore relies on, before anything is changed:
/// every entry lies where a backup puts entries (<c>memory/</c>, <c>prompts/</c>, or <c>config.yaml</c>,
/// <c>bindings.json</c> and <c>manifest.json</c> at the root) with no <c>..</c>, no absolute path and no duplicate; the
/// manifest reads; every file it lists is in the zip; every file in the zip is listed; the zip holds <c>memory/.git/</c>;
/// and, when the restore takes the config, <c>config.yaml</c> is there. Extraction writes only below the folders it is given.
/// </summary>
internal sealed class RestoreArchive : IDisposable
{
    private const string ManifestName = "manifest.json";
    private const string ConfigName = "config.yaml";
    private const string BindingsName = "bindings.json";
    private const string MemoryPrefix = "memory/";
    private const string PromptsPrefix = "prompts/";
    private const string GitPrefix = "memory/.git/";
    private const string CacheFolder = ".cache/";

    private readonly FileStream _stream;
    private readonly ZipArchive _zip;
    private readonly Dictionary<string, ZipArchiveEntry> _entries;

    private RestoreArchive(FileStream stream, ZipArchive zip, Dictionary<string, ZipArchiveEntry> entries, BackupManifest manifest)
    {
        _stream = stream;
        _zip = zip;
        _entries = entries;
        Manifest = manifest;
    }

    /// <summary>The manifest, as the backup wrote it; its <c>Files</c> is never <c>null</c> once the archive has opened.</summary>
    public BackupManifest Manifest { get; }

    /// <summary>Opens and validates the backup at <paramref name="path"/>.</summary>
    /// <param name="requireConfig">Whether the restore also takes <c>config.yaml</c>, which the backup must then hold.</param>
    /// <exception cref="TacitException">The file is missing (<see cref="TacitError.RequestInvalid"/>), or the backup cannot be restored (<see cref="TacitError.RestoreInvalid"/>).</exception>
    public static RestoreArchive Open(string path, MemoryLanguage language, bool requireConfig)
    {
        if (!File.Exists(path))
            throw new TacitException(TacitError.RequestInvalid, RestoreMessages.FileMissing.Format(language, path));

        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        ZipArchive zip;
        try
        {
            zip = new ZipArchive(stream, ZipArchiveMode.Read);
        }
        catch (InvalidDataException e)
        {
            stream.Dispose();
            throw new TacitException(TacitError.RestoreInvalid, RestoreMessages.NotZip.Format(language, path), e);
        }

        try
        {
            var entries = CheckEntries(zip, path, language);
            var manifest = ReadManifest(entries, path, language);
            CheckFiles(manifest, entries, path, language);
            if (!entries.Keys.Any(name => name.StartsWith(GitPrefix, StringComparison.Ordinal)))
                throw new TacitException(TacitError.RestoreInvalid, RestoreMessages.NoRepository.Format(language, path));
            if (requireConfig && !entries.ContainsKey(ConfigName))
                throw new TacitException(TacitError.RestoreInvalid, RestoreMessages.NoConfig.Format(language, path));
            return new RestoreArchive(stream, zip, entries, manifest);
        }
        catch
        {
            zip.Dispose();
            stream.Dispose();
            throw;
        }
    }

    /// <summary>Extracts <c>memory/</c> into <paramref name="destination"/>, which is created. <c>.cache/</c> is never extracted.</summary>
    public void ExtractMemory(string destination)
    {
        Directory.CreateDirectory(destination);
        ExtractUnder(MemoryPrefix, destination, skipCache: true);
    }

    /// <summary>Extracts <c>prompts/</c> into <paramref name="destination"/>, which is created even when the backup holds none.</summary>
    public void ExtractPrompts(string destination)
    {
        Directory.CreateDirectory(destination);
        ExtractUnder(PromptsPrefix, destination, skipCache: false);
    }

    /// <summary>Extracts <c>config.yaml</c> to <paramref name="destination"/>, replacing a file there.</summary>
    public void ExtractConfig(string destination) => _entries[ConfigName].ExtractToFile(destination, overwrite: true);

    public void Dispose()
    {
        _zip.Dispose();
        _stream.Dispose();
    }

    private void ExtractUnder(string prefix, string destination, bool skipCache)
    {
        foreach (var (name, entry) in _entries)
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            var relative = name[prefix.Length..];
            if (skipCache && relative.StartsWith(CacheFolder, StringComparison.Ordinal))
                continue;
            var target = Path.Combine(destination, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: false);
        }
    }

    /// <summary>Every entry by its name, refusing any that lies outside the places a backup uses, and any repeated name.</summary>
    private static Dictionary<string, ZipArchiveEntry> CheckEntries(ZipArchive zip, string path, MemoryLanguage language)
    {
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName;
            if (!IsAllowedPlace(name) || !HasSafeSegments(name))
                throw new TacitException(TacitError.RestoreInvalid, RestoreMessages.EntryPathRefused.Format(language, path, name));
            if (name.EndsWith('/'))
                continue;
            if (!entries.TryAdd(name, entry))
                throw new TacitException(TacitError.RestoreInvalid, RestoreMessages.DuplicateEntry.Format(language, path, name));
        }

        return entries;
    }

    private static BackupManifest ReadManifest(Dictionary<string, ZipArchiveEntry> entries, string path, MemoryLanguage language)
    {
        if (!entries.TryGetValue(ManifestName, out var entry))
            throw new TacitException(TacitError.RestoreInvalid, RestoreMessages.NoManifest.Format(language, path));

        try
        {
            var manifest = JsonSerializer.Deserialize<BackupManifest>(ReadAll(entry), BackupManifest.Json)
                ?? throw new JsonException("the manifest is empty");
            if (manifest.MemoryHead is null)
                throw new JsonException("the manifest has no memory head");
            if (manifest.Files is null)
                throw new JsonException("the manifest has no list of files");
            return manifest;
        }
        catch (JsonException e)
        {
            throw new TacitException(TacitError.RestoreInvalid, RestoreMessages.ManifestUnreadable.Format(language, path, e.Message), e);
        }
    }

    /// <summary>The manifest's files and the zip's files must be the same set; the manifest itself is not listed.</summary>
    private static void CheckFiles(BackupManifest manifest, Dictionary<string, ZipArchiveEntry> entries, string path, MemoryLanguage language)
    {
        var listed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in manifest.Files!)
        {
            if (!IsAllowedPlace(file) || !HasSafeSegments(file) || file.EndsWith('/'))
                throw new TacitException(TacitError.RestoreInvalid, RestoreMessages.EntryPathRefused.Format(language, path, file));
            if (!entries.ContainsKey(file))
                throw new TacitException(TacitError.RestoreInvalid, RestoreMessages.MissingEntry.Format(language, path, file));
            listed.Add(file);
        }

        foreach (var name in entries.Keys)
        {
            if (name != ManifestName && !listed.Contains(name))
                throw new TacitException(TacitError.RestoreInvalid, RestoreMessages.UnlistedEntry.Format(language, path, name));
        }
    }

    /// <summary>Whether a backup puts an entry at this name: under <c>memory/</c> or <c>prompts/</c>, or one of the root files.</summary>
    private static bool IsAllowedPlace(string name) =>
        name is ManifestName or ConfigName or BindingsName
        || name.StartsWith(MemoryPrefix, StringComparison.Ordinal)
        || name.StartsWith(PromptsPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Whether every segment of the name is a plain name: no empty segment, no <c>.</c> or <c>..</c>, no backslash, no drive
    /// colon and no rooted path. A trailing <c>/</c> (a folder) is allowed.
    /// </summary>
    private static bool HasSafeSegments(string name)
    {
        if (name.Contains('\\') || name.Contains(':') || Path.IsPathRooted(name))
            return false;
        var segments = name.TrimEnd('/').Split('/');
        return segments.All(segment => segment.Length > 0 && segment is not "." and not "..");
    }

    private static byte[] ReadAll(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
