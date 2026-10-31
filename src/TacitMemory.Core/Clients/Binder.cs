using System.Text;
using System.Text.Json.Nodes;
using TacitMemory.Core.Configuration;
using TacitMemory.Core.Injection;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Persona;

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
        var store = new BindingStore(options.TacitHome);
        var report = new StringBuilder();

        var created = store.Find(target.Client)?.CreatedContextFile ?? !File.Exists(target.ContextFile);
        var persona = PersonaDocument.Read(PersonaPath(config));
        if (persona is null)
        {
            report.AppendLine(Messages.PersonaUnreadable.Format(language, PersonaPath(config)));
        }
        else
        {
            var written = ContextFile.Put(target.ContextFile, ResidentBlock.Render(persona), language);
            report.AppendLine((written ? Messages.BlockWritten : Messages.BlockUnchanged).Format(language, target.ContextFile));
        }

        var server = new JsonObject { ["type"] = "http", ["url"] = $"http://127.0.0.1:{config.Port}/mcp" };
        var registered = McpJsonFile.Put(target.McpConfig, ServerName, server, language);
        report.AppendLine((registered ? Messages.McpRegistered : Messages.McpUnchanged).Format(language, target.McpConfig));

        store.Put(new BindingRecord(target.Client, target.ContextFile, created));
        report.Append(Messages.Connected.Format(language, target.Client));
        return new BindingResult(target.ContextFile, report.ToString());
    }

    public static BindingResult Disconnect(TacitOptions options, ClientKind client)
    {
        var language = LanguageOf(options);
        var target = Target(options, client);
        var store = new BindingStore(options.TacitHome);
        var report = new StringBuilder();

        var remaining = File.Exists(target.ContextFile) ? ContextFile.Remove(target.ContextFile, language) : null;
        if (remaining is not null)
        {
            report.AppendLine(Messages.BlockRemoved.Format(language, target.ContextFile));
            if (remaining.Length == 0 && store.Find(target.Client)?.CreatedContextFile == true)
                File.Delete(target.ContextFile);
        }

        if (McpJsonFile.Remove(target.McpConfig, ServerName, language))
            report.AppendLine(Messages.McpRemoved.Format(language, target.McpConfig));

        store.Remove(target.Client);
        report.Append(Messages.Disconnected.Format(language, target.Client));
        return new BindingResult(target.ContextFile, report.ToString());
    }

    /// <summary>Renders the current persona into every context file listed in <c>bindings.json</c> right now.</summary>
    public static IReadOnlyList<BlockRender> RenderAll(TacitOptions options, TacitConfig config)
    {
        var language = config.Language;
        var bindings = new BindingStore(options.TacitHome).Read();
        var persona = PersonaDocument.Read(PersonaPath(config));
        var block = persona is null ? null : ResidentBlock.Render(persona);
        var results = new List<BlockRender>();
        foreach (var binding in bindings)
        {
            var path = binding.ContextFile;
            if (block is null)
            {
                results.Add(new BlockRender(path, BlockState.PersonaUnreadable, Messages.PersonaUnreadable.Format(language, PersonaPath(config))));
            }
            else if (!File.Exists(path))
            {
                results.Add(new BlockRender(path, BlockState.FileMissing, Messages.ContextFileMissing.Format(language, path)));
            }
            else
            {
                try
                {
                    results.Add(ContextFile.Put(path, block, language)
                        ? new BlockRender(path, BlockState.Written, Messages.BlockWritten.Format(language, path))
                        : new BlockRender(path, BlockState.Unchanged, Messages.BlockUnchanged.Format(language, path)));
                }
                catch (TacitException e) when (e.Error == TacitError.MarkersBroken)
                {
                    results.Add(new BlockRender(path, BlockState.MarkersBroken, e.Message));
                }
            }
        }

        return results;
    }

    private static string PersonaPath(TacitConfig config) => Path.Combine(config.MemoryPath, "persona.md");

    /// <summary>The memory language for messages; <c>disconnect</c> must work even when the config does not load.</summary>
    private static MemoryLanguage LanguageOf(TacitOptions options)
    {
        try
        {
            return ConfigLoader.Load(options).Language;
        }
        catch (TacitException)
        {
            return LanguageCodes.FromUiCulture(options.UiCulture);
        }
    }

    private static ClientTarget Target(TacitOptions options, ClientKind client) => client switch
    {
        ClientKind.Omp => new ClientTarget(
            "omp",
            Path.Combine(options.UserHome, ".omp", "agent", "AGENTS.md"),
            Path.Combine(options.UserHome, ".omp", "agent", "mcp.json")),
        _ => throw new ArgumentOutOfRangeException(nameof(client), client, null),
    };

    private sealed record ClientTarget(string Client, string ContextFile, string McpConfig);
}
