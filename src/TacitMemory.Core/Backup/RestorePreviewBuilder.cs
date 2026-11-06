using System.Globalization;
using TacitMemory.Core.Configuration;
using TacitMemory.Core.Localization;

namespace TacitMemory.Core.Backup;

/// <summary>
/// The preview of a restore: what the backup holds, what the restore replaces, and whether the backup's <c>config.yaml</c>
/// loads when it would be restored. Nothing is changed; the backup's config is checked from a temporary copy.
/// </summary>
internal static class RestorePreviewBuilder
{
    public static async Task<RestorePreview> BuildAsync(
        TacitOptions options, TacitConfig config, string path, RestoreArchive archive, bool withConfig, CancellationToken cancellationToken)
    {
        var language = config.Language;
        var manifest = archive.Manifest;
        var unknown = RestoreMessages.PreviewUnknown.In(language);
        var commit = await MemoryCommit.ReadAsync(config.MemoryPath, language, cancellationToken);

        var replaces = new List<string> { RestoreMessages.ReplacesMemory.Format(language, config.MemoryPath, commit.Hash[..7], commit.Subject) };
        if (withConfig)
        {
            replaces.Add(RestoreMessages.ReplacesConfig.In(language));
            replaces.Add(RestoreMessages.ReplacesPrompts.In(language));
        }

        var generated = manifest.GeneratedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture) ?? unknown;
        var lines = new List<PreviewLine>
        {
            Plain(RestoreMessages.PreviewBackup.Format(language, path)),
            Plain(RestoreMessages.PreviewGenerated.Format(language, generated)),
            Plain(RestoreMessages.PreviewVersion.Format(language, manifest.TacitVersion ?? unknown)),
            Plain(RestoreMessages.PreviewKind.Format(language, manifest.Kind ?? unknown)),
            Plain(RestoreMessages.PreviewHead.Format(language, manifest.MemoryHead)),
        };
        if (manifest.MemoryHeadSubject is not null)
            lines.Add(Plain(RestoreMessages.PreviewHeadSubject.Format(language, manifest.MemoryHeadSubject)));
        lines.Add(Plain(RestoreMessages.PreviewFiles.Format(language, manifest.Files!.Count)));
        lines.Add(Plain(RestoreMessages.PreviewReplacesTitle.In(language)));
        lines.AddRange(replaces.Select(text => new PreviewLine(text, Replaces: true)));
        lines.Add(Plain(RestoreMessages.BindingsKept.In(language)));

        if (withConfig && ConfigProblem(options, archive) is { } problem)
            lines.Add(Plain(RestoreMessages.PreviewConfigProblem.Format(language, problem)));

        return new RestorePreview(
            path, manifest.GeneratedAt, manifest.TacitVersion, manifest.Kind, manifest.MemoryHead!, manifest.MemoryHeadSubject,
            manifest.Files!.Count, withConfig, replaces, lines, RestoreMessages.Prompt.In(language), RestoreMessages.Cancelled.In(language));
    }

    private static PreviewLine Plain(string text) => new(text, Replaces: false);

    /// <summary>
    /// The errors of the backup's <c>config.yaml</c> when it does not load, or <c>null</c>. Only the config's own errors
    /// count here: the prompts are the live ones and are checked when the restore applies them.
    /// </summary>
    private static string? ConfigProblem(TacitOptions options, RestoreArchive archive)
    {
        var copy = Path.Combine(Path.GetTempPath(), $"tacit-restore-config-{Guid.NewGuid():N}.yaml");
        try
        {
            archive.ExtractConfig(copy);
            ConfigLoader.Read(options, copy);
            return null;
        }
        catch (TacitException e) when (e.Error == TacitError.ConfigInvalid)
        {
            return e.Message;
        }
        finally
        {
            if (File.Exists(copy))
                File.Delete(copy);
        }
    }
}
