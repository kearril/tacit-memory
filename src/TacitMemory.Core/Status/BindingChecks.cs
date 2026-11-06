using TacitMemory.Core.Clients;
using TacitMemory.Core.Injection;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Skills;

namespace TacitMemory.Core.Status;

/// <summary>
/// The bindings column: for each binding in <c>bindings.json</c>, whether its context file exists and is UTF-8, its
/// resident block's markers are complete and unique, and each skill link in its skills folder points at its tacit skill. It reads the files
/// and writes none of them: nothing is rendered, so a check never changes what the client sees.
/// </summary>
internal static class BindingChecks
{
    public static BindingsStatus Produce(TacitOptions options, MemoryLanguage language)
    {
        var title = ManagementApiMessages.BindingsTitle.In(language);
        IReadOnlyList<BindingRecord> bindings;
        try
        {
            bindings = new BindingStore(options.TacitHome, language).Read();
        }
        catch (TacitException e)
        {
            return new BindingsStatus(title, [ManagementApiMessages.BindingsUnreadable.Format(language, e.Message)], []);
        }

        if (bindings.Count == 0)
            return new BindingsStatus(title, [ManagementApiMessages.BindingsNone.In(language)], []);

        var items = bindings.Select(binding => Check(binding, options.TacitHome, language)).ToList();
        return new BindingsStatus(title, items.Select(item => LineOf(item, language)).ToList(), items);
    }

    /// <summary>The first problem of a binding, in the order a user would fix them; no problem when it is healthy.</summary>
    private static BindingCheck Check(BindingRecord binding, string tacitHome, MemoryLanguage language)
    {
        BindingCheck Broken(BindingProblem problem) => new(binding.Client, binding.ContextFile, binding.SkillsFolder, problem, null, []);
        try
        {
            if (!File.Exists(binding.ContextFile))
                return Broken(BindingProblem.ContextFileMissing);

            var text = TextFile.Read(binding.ContextFile);
            if (text is null)
                return Broken(BindingProblem.ContextFileNotUtf8);

            if (ContextFile.FindBlock(binding.ContextFile, text.Value.Text, language) is null)
                return Broken(BindingProblem.MarkersIncomplete);
        }
        catch (TacitException e) when (e.Error == TacitError.MarkersBroken)
        {
            return Broken(BindingProblem.MarkersIncomplete);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Broken(BindingProblem.ContextFileUnreadable);
        }

        foreach (var (link, skillFolder) in SkillSet.Links(binding.SkillsFolder, tacitHome))
        {
            if (!SkillLink.Points(link, skillFolder))
                return new BindingCheck(binding.Client, binding.ContextFile, binding.SkillsFolder, BindingProblem.SkillLinkNotTacit, link, []);
        }
        return new BindingCheck(binding.Client, binding.ContextFile, binding.SkillsFolder, null, null, []);
    }

    private static string LineOf(BindingCheck item, MemoryLanguage language) => item.Problem switch
    {
        null => ManagementApiMessages.BindingOk.Format(language, item.Client, item.ContextFile, item.SkillsFolder),
        BindingProblem.ContextFileMissing => ManagementApiMessages.BindingContextFileMissing.Format(language, item.Client, item.ContextFile, item.SkillsFolder),
        BindingProblem.ContextFileNotUtf8 => ManagementApiMessages.BindingContextFileNotUtf8.Format(language, item.Client, item.ContextFile, item.SkillsFolder),
        BindingProblem.ContextFileUnreadable => ManagementApiMessages.BindingContextFileUnreadable.Format(language, item.Client, item.ContextFile, item.SkillsFolder),
        BindingProblem.MarkersIncomplete => ManagementApiMessages.BindingMarkersIncomplete.Format(language, item.Client, item.ContextFile, item.SkillsFolder),
        _ => ManagementApiMessages.BindingSkillLinkNotTacit.Format(language, item.Client, item.ContextFile, item.SkillsFolder, item.SkillLink),
    };
}
