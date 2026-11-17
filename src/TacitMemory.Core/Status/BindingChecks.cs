using TacitMemory.Core.Clients;
using TacitMemory.Core.Injection;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Skills;

namespace TacitMemory.Core.Status;

/// <summary>
/// The bindings column: for each binding in <c>bindings.json</c>, whether its context file exists and is UTF-8, its
/// resident block's markers are complete and unique, and each skill link in its skills folder points at its tacit skill.
/// A context file or skills folder that several bindings share is checked once, for the first binding that names it; the
/// others only say which bindings they share a path with. It reads the files and writes none of them: nothing is
/// rendered, so a check never changes what the client sees.
/// </summary>
internal static class BindingChecks
{
    public static BindingsStatus Produce(TacitOptions options, MemoryLanguage language)
    {
        var title = StatusMessages.BindingsTitle.In(language);
        IReadOnlyList<BindingRecord> bindings;
        try
        {
            bindings = new BindingStore(options.TacitHome, language).Read();
        }
        catch (TacitException e)
        {
            return new BindingsStatus(title, [StatusMessages.BindingsUnreadable.Format(language, e.Message)], []);
        }

        if (bindings.Count == 0)
            return new BindingsStatus(title, [StatusMessages.BindingsNone.In(language)], []);

        var items = Checks(bindings, options.TacitHome, language);
        return new BindingsStatus(title, items.Select(item => LineOf(item, language)).ToList(), items);
    }

    /// <summary>One check per binding, in record order. Each path is checked once, by the first binding that names it.</summary>
    private static List<BindingCheck> Checks(IReadOnlyList<BindingRecord> bindings, string tacitHome, MemoryLanguage language)
    {
        var checkedContextFiles = new HashSet<string>(BindingPath.Comparer);
        var checkedSkillsFolders = new HashSet<string>(BindingPath.Comparer);
        var items = new List<BindingCheck>();
        foreach (var binding in bindings)
        {
            var contextProblem = checkedContextFiles.Add(binding.ContextFile) ? ContextFileProblem(binding.ContextFile, language) : null;
            var (skillProblem, skillLink) = checkedSkillsFolders.Add(binding.SkillsFolder)
                ? SkillsFolderProblem(binding.SkillsFolder, tacitHome)
                : (null, null);
            var sharedWith = SharedPaths.SharedWith(bindings, binding);

            // The first problem in the order a user would fix them; a skill link is named only when it is the one reported.
            items.Add(contextProblem is not null
                ? new BindingCheck(binding.Client, binding.ContextFile, binding.SkillsFolder, contextProblem, null, sharedWith)
                : new BindingCheck(binding.Client, binding.ContextFile, binding.SkillsFolder, skillProblem, skillLink, sharedWith));
        }
        return items;
    }

    /// <summary>The context file's problem: missing, not UTF-8, unreadable, or its markers are incomplete; <c>null</c> when it is fine.</summary>
    private static BindingProblem? ContextFileProblem(string contextFile, MemoryLanguage language)
    {
        try
        {
            if (!File.Exists(contextFile))
                return BindingProblem.ContextFileMissing;

            var text = TextFile.Read(contextFile);
            if (text is null)
                return BindingProblem.ContextFileNotUtf8;

            return ContextFile.FindBlock(contextFile, text.Value.Text, language) is null ? BindingProblem.MarkersIncomplete : null;
        }
        catch (TacitException e) when (e.Error == TacitError.MarkersBroken)
        {
            return BindingProblem.MarkersIncomplete;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return BindingProblem.ContextFileUnreadable;
        }
    }

    /// <summary>The first skill link in <paramref name="skillsFolder"/> that does not point at its tacit skill, with the link; nothing when all do.</summary>
    private static (BindingProblem? Problem, string? Link) SkillsFolderProblem(string skillsFolder, string tacitHome)
    {
        foreach (var (link, skillFolder) in SkillSet.Links(skillsFolder, tacitHome))
        {
            if (!SkillLink.Points(link, skillFolder))
                return (BindingProblem.SkillLinkNotTacit, link);
        }
        return (null, null);
    }

    private static string LineOf(BindingCheck item, MemoryLanguage language)
    {
        var line = item.Problem switch
        {
            null => StatusMessages.BindingOk.Format(language, item.Client, item.ContextFile, item.SkillsFolder),
            BindingProblem.ContextFileMissing => StatusMessages.BindingContextFileMissing.Format(language, item.Client, item.ContextFile, item.SkillsFolder),
            BindingProblem.ContextFileNotUtf8 => StatusMessages.BindingContextFileNotUtf8.Format(language, item.Client, item.ContextFile, item.SkillsFolder),
            BindingProblem.ContextFileUnreadable => StatusMessages.BindingContextFileUnreadable.Format(language, item.Client, item.ContextFile, item.SkillsFolder),
            BindingProblem.MarkersIncomplete => StatusMessages.BindingMarkersIncomplete.Format(language, item.Client, item.ContextFile, item.SkillsFolder),
            _ => StatusMessages.BindingSkillLinkNotTacit.Format(language, item.Client, item.ContextFile, item.SkillsFolder, item.SkillLink),
        };
        return item.SharedWith.Count == 0 ? line : line + StatusMessages.BindingSharedWith.Format(language, string.Join(", ", item.SharedWith));
    }
}
