using System.Text;
using TacitMemory.Core.Configuration;
using TacitMemory.Core.Injection;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Persona;
using TacitMemory.Core.Skills;

namespace TacitMemory.Core.Clients;

/// <summary><c>connect</c>, <c>disconnect</c> and rendering the resident block into every bound context file.</summary>
internal static class Binder
{
    /// <summary>
    /// Binds a client. Every check runs before the first write, so a refusal changes no file: the name, the record of the
    /// same name, the two folders, the link names, the config and persona, and the markers of the context file.
    /// </summary>
    public static BindingResult Connect(TacitOptions options, ConnectRequest request)
    {
        var client = ClientName.Normalize(request.Client);
        var contextFile = Absolute(request.ContextFile, Messages.ContextFileNoun, options);
        var skillsFolder = Absolute(request.SkillsFolder, Messages.SkillsFolderNoun, options);
        var language = ConfigLoader.LanguageOf(options);
        var store = new BindingStore(options.TacitHome, language);

        var previous = store.Find(client);
        if (previous is not null && !(BindingPath.Same(previous.ContextFile, contextFile) && BindingPath.Same(previous.SkillsFolder, skillsFolder)))
            throw new TacitException(
                TacitError.BindingConflict, Messages.BindingConflict.Format(language, client, previous.ContextFile, previous.SkillsFolder));

        var contextFolder = Path.GetDirectoryName(contextFile) ?? contextFile;
        if (!Directory.Exists(contextFolder))
            throw new TacitException(TacitError.ContextFolderMissing, Messages.ContextFolderMissing.Format(language, contextFolder));
        if (!Directory.Exists(skillsFolder))
            throw new TacitException(TacitError.SkillsFolderMissing, Messages.SkillsFolderMissing.Format(language, skillsFolder));

        var links = SkillSet.Links(skillsFolder, options.TacitHome);
        foreach (var (link, skillFolder) in links)
            SkillLink.EnsureFree(link, skillFolder, language);

        var config = ConfigLoader.Load(options);
        var personaPath = PersonaPath(config);
        var persona = PersonaDocument.Read(personaPath)
            ?? throw new TacitException(TacitError.FileNotUtf8, Messages.PersonaUnreadable.Format(language, personaPath));
        ContextFile.Check(contextFile, language);
        var block = ResidentBlock.Render(persona, config.Prompts.Resident);

        var bindings = store.Read();
        var contextUsers = SharedPaths.ContextFileUsers(bindings, client, contextFile);
        var skillUsers = SharedPaths.SkillsFolderUsers(bindings, client, skillsFolder);
        // An inherited answer keeps the creator's: the last binding that uses an empty context file removes it.
        var createdContextFile = previous?.CreatedContextFile
            ?? (contextUsers.Count > 0 ? contextUsers[0].CreatedContextFile : !File.Exists(contextFile));
        var report = new StringBuilder();

        if (skillUsers.Count > 0)
            report.AppendLine(Messages.SkillsFolderShared.Format(language, skillsFolder, SharedPaths.Names(skillUsers)));
        foreach (var skill in SkillSet.Write(options.TacitHome, language, config.Prompts.Skill).Skills)
            report.AppendLine((skill.Written ? Messages.SkillWritten : Messages.SkillUnchanged).Format(language, skill.Path));

        if (contextUsers.Count > 0)
            report.AppendLine(Messages.ContextFileShared.Format(language, contextFile, SharedPaths.Names(contextUsers)));
        var written = ContextFile.Put(contextFile, block, language);
        report.AppendLine((written ? Messages.BlockWritten : Messages.BlockUnchanged).Format(language, contextFile));

        var created = new List<(string Link, string SkillFolder)>();
        try
        {
            foreach (var (link, skillFolder) in links)
            {
                var made = SkillLink.Put(link, skillFolder, language);
                if (made)
                    created.Add((link, skillFolder));
                report.AppendLine((made ? Messages.SkillLinked : Messages.SkillLinkUnchanged).Format(language, link));
            }

            store.Put(new BindingRecord(client, contextFile, skillsFolder, createdContextFile));
        }
        catch
        {
            // Removes the links this call made, so a failed link or an unwritable bindings.json leaves none behind.
            foreach (var (link, skillFolder) in created)
                SkillLink.Remove(link, skillFolder);
            throw;
        }

        var endpoint = new Uri($"http://127.0.0.1:{config.Port}/mcp");
        report.AppendLine(Messages.Connected.Format(language, client));
        report.AppendLine(Messages.McpEndpoint.Format(language, endpoint));
        report.AppendLine(Messages.McpTransport.In(language));
        report.AppendLine(Messages.McpServerName.In(language));
        report.Append(Messages.McpPortReminder.In(language));
        return new BindingResult(contextFile, endpoint, report.ToString());
    }

    /// <summary>
    /// Unbinds a client: removes its resident block and the links that point at tacit, then its record. The paths come from
    /// the record. A context file or skills folder that another binding still uses is kept and reported. A file or link that
    /// is already gone is reported and skipped; the record is removed either way.
    /// </summary>
    public static BindingResult Disconnect(TacitOptions options, string client)
    {
        var name = ClientName.Normalize(client);
        var language = ConfigLoader.LanguageOf(options);
        var store = new BindingStore(options.TacitHome, language);
        var bindings = store.Read();
        var binding = bindings.FirstOrDefault(existing => existing.Client == name)
            ?? throw new TacitException(TacitError.BindingNotFound, Messages.BindingNotFound.Format(language, name));
        var report = new StringBuilder();

        // A context file another binding still uses stays exactly as it is; the last binding that uses it removes the block.
        var contextUsers = SharedPaths.ContextFileUsers(bindings, name, binding.ContextFile);
        if (contextUsers.Count > 0)
            report.AppendLine(Messages.ContextFileStillUsed.Format(language, binding.ContextFile, SharedPaths.Names(contextUsers)));
        else if (!File.Exists(binding.ContextFile))
            report.AppendLine(Messages.ContextFileAbsent.Format(language, binding.ContextFile));
        else if (ContextFile.Remove(binding.ContextFile, language) is { } remaining)
        {
            report.AppendLine(Messages.BlockRemoved.Format(language, binding.ContextFile));
            if (remaining.Length == 0 && binding.CreatedContextFile)
                File.Delete(binding.ContextFile);
        }
        else
            report.AppendLine(Messages.ResidentBlockAbsent.Format(language, binding.ContextFile));

        // The links are removed from the skills folder the record names; the skill folders they point to are kept.
        // A skills folder another binding still uses keeps its links.
        var skillUsers = SharedPaths.SkillsFolderUsers(bindings, name, binding.SkillsFolder);
        foreach (var (link, skillFolder) in SkillSet.Links(binding.SkillsFolder, options.TacitHome))
        {
            if (skillUsers.Count > 0)
            {
                report.AppendLine(Messages.SkillLinkStillUsed.Format(language, link, SharedPaths.Names(skillUsers)));
                continue;
            }

            switch (SkillLink.Remove(link, skillFolder))
            {
                case SkillLink.RemoveOutcome.Removed:
                    report.AppendLine(Messages.SkillLinkRemoved.Format(language, link));
                    break;
                case SkillLink.RemoveOutcome.NotOurs:
                    report.AppendLine(Messages.SkillLinkForeign.Format(language, link));
                    break;
                default:
                    report.AppendLine(Messages.SkillLinkAbsent.Format(language, link));
                    break;
            }
        }

        store.Remove(name);
        report.Append(Messages.Disconnected.Format(language, name));
        return new BindingResult(binding.ContextFile, null, report.ToString());
    }

    /// <summary>Renders the current persona and <c>resident.md</c> into every context file listed in <c>bindings.json</c> right now.</summary>
    /// <exception cref="TacitException"><c>bindings.json</c> cannot be read.</exception>
    public static IReadOnlyList<BlockRender> RenderAll(TacitOptions options, TacitConfig config, PromptDocuments prompts)
    {
        var language = config.Language;
        var bindings = new BindingStore(options.TacitHome, language).Read();
        var personaPath = PersonaPath(config);
        var persona = PersonaDocument.Read(personaPath);
        var block = persona is null ? null : ResidentBlock.Render(persona, prompts.Resident);
        var results = new List<BlockRender>();
        // A context file that several bindings share renders once; the first binding's spelling of the path names it.
        foreach (var path in bindings.Select(binding => binding.ContextFile).Distinct(BindingPath.Comparer))
        {
            if (block is null)
            {
                results.Add(new BlockRender(path, BlockState.PersonaUnreadable, Messages.PersonaUnreadable.Format(language, personaPath)));
                continue;
            }

            if (!File.Exists(path))
            {
                results.Add(new BlockRender(path, BlockState.FileMissing, Messages.ContextFileMissing.Format(language, path)));
                continue;
            }

            try
            {
                results.Add(ContextFile.Put(path, block, language)
                    ? new BlockRender(path, BlockState.Written, Messages.BlockWritten.Format(language, path))
                    : new BlockRender(path, BlockState.Unchanged, Messages.BlockUnchanged.Format(language, path)));
            }
            catch (TacitException e) when (e.Error is TacitError.MarkersBroken or TacitError.FileNotUtf8)
            {
                var state = e.Error == TacitError.MarkersBroken ? BlockState.MarkersBroken : BlockState.FileNotUtf8;
                results.Add(new BlockRender(path, state, e.Message));
            }
        }

        return results;
    }

    private static string PersonaPath(TacitConfig config) => Path.Combine(config.MemoryPath, "persona.md");

    /// <summary>A path the user gave: <c>~</c> expanded and made absolute, the same rule the config's paths follow.</summary>
    /// <exception cref="TacitException">The path is empty.</exception>
    private static string Absolute(string path, Localized what, TacitOptions options)
    {
        var language = ConfigLoader.LanguageOf(options);
        if (string.IsNullOrWhiteSpace(path))
            throw new TacitException(TacitError.RequestInvalid, Messages.PathEmpty.Format(language, what.In(language)));
        return HomePaths.Expand(path, options.UserHome);
    }
}
