using System.Text;
using System.Text.Json.Nodes;
using TacitMemory.Core.Configuration;
using TacitMemory.Core.Injection;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Persona;
using TacitMemory.Core.Skills;

namespace TacitMemory.Core.Clients;

/// <summary><c>connect</c>, <c>disconnect</c> and rendering the resident block into every bound context file.</summary>
internal static class Binder
{
    private const string ServerName = "tacit";

    public static BindingResult Connect(TacitOptions options, ClientKind client)
    {
        var config = ConfigLoader.Load(options);
        var language = config.Language;
        var target = Target(options, client);
        var store = new BindingStore(options.TacitHome, language);
        var previous = store.Find(target.Client);
        var personaPath = PersonaPath(config);
        var persona = PersonaDocument.Read(personaPath)
            ?? throw new TacitException(TacitError.FileNotUtf8, Messages.PersonaUnreadable.Format(language, personaPath));

        var createdContextFile = previous?.CreatedContextFile ?? !File.Exists(target.ContextFile);
        var createdMcpConfig = previous?.CreatedMcpConfig ?? !File.Exists(target.McpConfig);
        var skillFolder = MemorySkill.Folder(options.TacitHome);
        var report = new StringBuilder();

        var skill = MemorySkill.Write(options.TacitHome, config.Prompts.Skill);
        report.AppendLine((skill.Written ? Messages.SkillWritten : Messages.SkillUnchanged).Format(language, skill.Path));

        // The link is made before the other files, so a refused link changes nothing else.
        var linkCreated = SkillLink.Put(target.SkillLink, skillFolder, language);
        report.AppendLine((linkCreated ? Messages.SkillLinked : Messages.SkillLinkUnchanged).Format(language, target.SkillLink));

        try
        {
            var written = ContextFile.Put(target.ContextFile, ResidentBlock.Render(persona, config.Prompts.Resident), language);
            report.AppendLine((written ? Messages.BlockWritten : Messages.BlockUnchanged).Format(language, target.ContextFile));

            var server = new JsonObject { ["type"] = "http", ["url"] = $"http://127.0.0.1:{config.Port}/mcp" };
            var registered = McpJsonFile.Put(target.McpConfig, ServerName, server, language);
            report.AppendLine((registered ? Messages.McpRegistered : Messages.McpUnchanged).Format(language, target.McpConfig));
            store.Put(new BindingRecord(target.Client, target.ContextFile, target.SkillLink, createdContextFile, createdMcpConfig));
        }
        catch
        {
            // Removes the link this call made, so a refused file or an unwritable bindings.json leaves no link behind.
            if (linkCreated)
                SkillLink.Remove(target.SkillLink, skillFolder);
            throw;
        }

        report.Append(Messages.Connected.Format(language, target.Client));
        return new BindingResult(target.ContextFile, report.ToString());
    }

    public static BindingResult Disconnect(TacitOptions options, ClientKind client)
    {
        var language = ConfigLoader.LanguageOf(options);
        var target = Target(options, client);
        var store = new BindingStore(options.TacitHome, language);
        var binding = store.Find(target.Client);
        var report = new StringBuilder();

        if (ContextFile.Remove(target.ContextFile, language) is { } remaining)
        {
            report.AppendLine(Messages.BlockRemoved.Format(language, target.ContextFile));
            if (remaining.Length == 0 && binding?.CreatedContextFile == true)
                File.Delete(target.ContextFile);
        }

        if (McpJsonFile.Remove(target.McpConfig, ServerName, language) is { } root)
        {
            report.AppendLine(Messages.McpRemoved.Format(language, target.McpConfig));
            if (binding?.CreatedMcpConfig == true && root.Count == 1 && root["mcpServers"] is JsonObject { Count: 0 })
                File.Delete(target.McpConfig);
        }

        // The link is removed where connect made it; the skill folder it points to is kept.
        var linkPath = binding?.SkillLink ?? target.SkillLink;
        switch (SkillLink.Remove(linkPath, MemorySkill.Folder(options.TacitHome)))
        {
            case SkillLink.RemoveOutcome.Removed:
                report.AppendLine(Messages.SkillLinkRemoved.Format(language, linkPath));
                break;
            case SkillLink.RemoveOutcome.NotOurs:
                report.AppendLine(Messages.SkillLinkForeign.Format(language, linkPath));
                break;
        }

        store.Remove(target.Client);
        report.Append(Messages.Disconnected.Format(language, target.Client));
        return new BindingResult(target.ContextFile, report.ToString());
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
        foreach (var path in bindings.Select(binding => binding.ContextFile))
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

    private static ClientTarget Target(TacitOptions options, ClientKind client) => client switch
    {
        ClientKind.Omp => new ClientTarget(
            "omp",
            Path.Combine(options.UserHome, ".omp", "agent", "AGENTS.md"),
            Path.Combine(options.UserHome, ".omp", "agent", "mcp.json"),
            Path.Combine(options.UserHome, ".omp", "agent", "skills", MemorySkill.Name)),
        _ => throw new ArgumentOutOfRangeException(nameof(client), client, null),
    };

    private sealed record ClientTarget(string Client, string ContextFile, string McpConfig, string SkillLink);
}
