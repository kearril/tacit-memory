using TacitMemory.Core.Clients;
using TacitMemory.Core.Injection;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Skills;

namespace TacitMemory.Core.Status;

/// <summary>
/// The bindings column: for each binding in <c>bindings.json</c>, whether its context file exists and is UTF-8, its
/// resident block's markers are complete and unique, and its skill link points at the memory skill. It reads the files
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

        var skillFolder = MemorySkill.Folder(options.TacitHome);
        var items = bindings.Select(binding => new BindingCheck(binding.Client, binding.ContextFile, ProblemOf(binding, skillFolder, language))).ToList();
        return new BindingsStatus(title, items.Select(item => LineOf(item, language)).ToList(), items);
    }

    /// <summary>The first problem of a binding, in the order a user would fix them; <c>null</c> when it is healthy.</summary>
    private static BindingProblem? ProblemOf(BindingRecord binding, string skillFolder, MemoryLanguage language)
    {
        try
        {
            if (!File.Exists(binding.ContextFile))
                return BindingProblem.ContextFileMissing;

            var text = TextFile.Read(binding.ContextFile);
            if (text is null)
                return BindingProblem.ContextFileNotUtf8;

            if (ContextFile.FindBlock(binding.ContextFile, text.Value.Text, language) is null)
                return BindingProblem.MarkersIncomplete;
        }
        catch (TacitException e) when (e.Error == TacitError.MarkersBroken)
        {
            return BindingProblem.MarkersIncomplete;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return BindingProblem.ContextFileUnreadable;
        }

        return SkillLink.Points(binding.SkillLink, skillFolder) ? null : BindingProblem.SkillLinkNotMemorySkill;
    }

    private static string LineOf(BindingCheck item, MemoryLanguage language) => item.Problem switch
    {
        null => ManagementApiMessages.BindingOk.Format(language, item.Client, item.ContextFile),
        BindingProblem.ContextFileMissing => ManagementApiMessages.BindingContextFileMissing.Format(language, item.Client, item.ContextFile),
        BindingProblem.ContextFileNotUtf8 => ManagementApiMessages.BindingContextFileNotUtf8.Format(language, item.Client, item.ContextFile),
        BindingProblem.ContextFileUnreadable => ManagementApiMessages.BindingContextFileUnreadable.Format(language, item.Client, item.ContextFile),
        BindingProblem.MarkersIncomplete => ManagementApiMessages.BindingMarkersIncomplete.Format(language, item.Client, item.ContextFile),
        _ => ManagementApiMessages.BindingSkillLinkNotMemorySkill.Format(language, item.Client, item.ContextFile),
    };
}
