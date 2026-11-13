using System.Text;
using TacitMemory.Core.Consolidation;
using TacitMemory.Core.Configuration;
using TacitMemory.Core.Injection;
using TacitMemory.Core.Localization;
using TacitMemory.Core.Recent;
using TacitMemory.Core.Skills;

namespace TacitMemory.Core.Show;

/// <summary>
/// What <c>tacit show</c> prints. It reads the sources the service reads, so the output is what a session would receive
/// (except the runtime status). Loading the config may create <c>prompts/</c> and rewrite a missing <c>rules.md</c>, as the
/// service does at startup; nothing else is written.
/// </summary>
internal static class Viewer
{
    private static readonly Localized SourceCode = new("代码", "code");
    private static readonly Localized SourceBuiltIn = new("内置默认", "built-in default");
    private static readonly Localized SourceFile = new("你的文件 {0}", "your file {0}");
    private static readonly Localized SourceLine = new("[{0}] 来源：{1}，{2} 字符", "[{0}] source: {1}, {2} characters");
    private static readonly Localized Empty = new("（空）", "(empty)");

    private static readonly Localized InstructionsTitle = new(
        "服务端说明：固定部分 {0}/{1}，总长 {2}/{3}（字符）",
        "Server instructions: fixed {0}/{1}, total {2}/{3} (characters)");
    private static readonly Localized InstructionsNote = new(
        "注：运行中的服务还会加入状态提示（调用失败的辅助模型、非 UTF-8 文件），此处不显示。",
        "Note: a running service also adds a status part (auxiliary models whose last call failed, files the index skips); it is not shown here.");

    private static readonly Localized OpeningLabel = new("开场", "Opening");
    private static readonly Localized CoreRulesLabel = new("核心规则", "Core rules");
    private static readonly Localized GeneralRulesLabel = new("通用规则", "General rules");
    private static readonly Localized LanguageLabel = new("语言规范", "Language norms");
    private static readonly Localized RecordingCriteriaLabel = new("值得记标准", "Recording criteria");
    private static readonly Localized RecentLabel = new("最近 7 天活动", "Activities of the last seven days");
    private static readonly Localized ConsolidationLabel = new("整理提示", "Consolidation reminder");

    private static readonly Localized ResidentTitle = new(
        "[常驻区块外壳与四节引导语] 来源：{0}，{1}/{2} 字符",
        "[Resident block shell and four guidance sections] source: {0}, {1}/{2} characters");

    private static readonly Localized SkillTitle = new(
        "记忆 skill 的完整内容（SKILL.md）：{0} 字符",
        "The memory skill as the agent reads it (SKILL.md): {0} characters");
    private static readonly Localized SkillSources = new(
        "来源：frontmatter 与系统部分为代码；{0}",
        "source: the frontmatter and the system part are code; {0}");
    private static readonly Localized SkillConventions = new(
        "你的写法约定 {0}，{1}/{2} 字符",
        "your writing conventions {0}, {1}/{2} characters");
    private static readonly Localized SkillNoConventions = new(
        "没有写法约定，因为 prompts/skill.md 不存在",
        "no writing conventions, because prompts/skill.md does not exist");

    private static readonly Localized CommandTitle = new(
        "{0} skill 的完整内容（SKILL.md）：{1} 字符",
        "The {0} skill as the agent reads it (SKILL.md): {1} characters");
    private static readonly Localized CommandSource = new(
        "来源：代码；用户触发时附带的文字随它一并送达",
        "source: code; any text the user sends with the command reaches the agent alongside it");

    private static readonly Localized ToolsTitle = new(
        "工具说明（来源：代码，TacitMemory.Host 的工具定义）：{0} 个",
        "Tool descriptions (source: code, the tool definitions in TacitMemory.Host): {0}");
    private static readonly Localized ToolParameters = new("参数：", "Parameters:");

    /// <summary>The built-in text alone, for the targets that have one. A memory command has no file that replaces it, so it has none.</summary>
    public static ShowResult Default(ShowTarget target) => target switch
    {
        ShowTarget.Instructions => Bare(ShowSource.BuiltInDefault, ServerInstructionText.DefaultInstructions),
        ShowTarget.Resident => Bare(ShowSource.BuiltInDefault, ResidentFile.Compose(ResidentBlock.Default)),
        ShowTarget.Skill => Bare(ShowSource.Code, MemorySkill.SystemPart),
        _ => throw new ArgumentException("A memory command has no built-in text that a file replaces.", nameof(target)),
    };

    public static async Task<ShowResult> Show(
        TacitOptions options, TacitConfig config, ShowTarget target, CancellationToken cancellationToken = default)
    {
        return target switch
        {
            ShowTarget.Instructions => await Instructions(options, config, cancellationToken),
            ShowTarget.Resident => Resident(options, config),
            ShowTarget.Skill => Skill(options, config),
            ShowTarget.Remember => Command(RememberSkill.Name, RememberSkill.Compose(), config.Language),
            ShowTarget.Consolidation => Command(ConsolidationSkill.Name, ConsolidationSkill.Compose(config.Language), config.Language),
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
        };
    }

    /// <summary>Each tool with its parameters, in the language of the memory.</summary>
    public static ShowResult Tools(MemoryLanguage language, IReadOnlyList<ToolDescription> tools)
    {
        var body = new StringBuilder();
        foreach (var tool in tools)
        {
            body.Append("\n\n## ").Append(tool.Name).Append('\n').Append(tool.Description);
            if (tool.Parameters.Count == 0)
                continue;
            body.Append('\n').Append(ToolParameters.In(language));
            foreach (var parameter in tool.Parameters)
            {
                body.Append("\n- ").Append(parameter.Name).Append(" (").Append(parameter.Type).Append(')');
                if (parameter.Description.Length > 0)
                    body.Append(": ").Append(parameter.Description);
            }
        }
        var heading = ToolsTitle.Format(language, tools.Count);
        return new ShowResult(heading + body, [new ShowPart("tools", ShowSource.Code, null, body.ToString().Trim())]);
    }

    private static ShowResult Bare(ShowSource source, string text) =>
        new(text, [new ShowPart("text", source, null, text)]);

    private static async Task<ShowResult> Instructions(TacitOptions options, TacitConfig config, CancellationToken cancellationToken)
    {
        var language = config.Language;
        var recent = RecentSummary.Read(config.MemoryPath, options.Today);
        // The service's text without its runtime status, cut to the same limits, with the consolidation reminder as it would be sent now.
        var consolidation = await ConsolidationReminder.RenderAsync(config, options.Today, cancellationToken);
        var set = ServerInstructionText.Assemble(language, config.Prompts, [], [], recent, consolidation);

        var sections = set.Fixed
            .Select(part => (Label: LabelOf(part.Part), Shown: InstructionShowPart(part, config, options.TacitHome)))
            .ToList();
        if (set.Recent.Length > 0)
            sections.Add((RecentLabel, new ShowPart("recent", ShowSource.Code, null, set.Recent)));
        if (set.Consolidation.Length > 0)
            sections.Add((ConsolidationLabel, new ShowPart("consolidation", ShowSource.Code, null, set.Consolidation)));

        var heading = InstructionsTitle.Format(
            language, set.FixedText.Length, ServerInstructionText.FixedLimit, set.Text.Length, ServerInstructionText.TotalLimit);
        var texts = sections.Select(section => Section(language, section.Label.In(language), section.Shown)).ToList();
        texts.Insert(0, heading + "\n" + InstructionsNote.In(language));
        return new ShowResult(
            string.Join("\n\n", texts),
            sections.Select(section => section.Shown).ToList(),
            Fixed: new ShowLimit(set.FixedText.Length, ServerInstructionText.FixedLimit),
            Whole: new ShowLimit(set.Text.Length, ServerInstructionText.TotalLimit));
    }

    private static ShowPart InstructionShowPart(InstructionText part, TacitConfig config, string tacitHome)
    {
        var key = KeyOf(part.Part);
        return part.Part switch
        {
            InstructionPart.CoreRules when config.Prompts.Instructions is null => new ShowPart(key, ShowSource.BuiltInDefault, null, part.Text),
            InstructionPart.CoreRules => new ShowPart(key, ShowSource.UserFile, PromptFiles.InstructionsPath(tacitHome), part.Text),
            InstructionPart.RecordingCriteria => new ShowPart(key, ShowSource.UserFile, PromptFiles.RulesPath(tacitHome), part.Text),
            _ => new ShowPart(key, ShowSource.Code, null, part.Text),
        };
    }

    /// <summary>The name a part has in <c>tacit show</c>'s output.</summary>
    private static string KeyOf(InstructionPart part) => part switch
    {
        InstructionPart.Opening => "opening",
        InstructionPart.CoreRules => "instructions",
        InstructionPart.GeneralRules => "general",
        InstructionPart.Language => "language",
        InstructionPart.RecordingCriteria => "criteria",
        _ => throw new ArgumentOutOfRangeException(nameof(part), part, null),
    };

    private static Localized LabelOf(InstructionPart part) => part switch
    {
        InstructionPart.Opening => OpeningLabel,
        InstructionPart.CoreRules => CoreRulesLabel,
        InstructionPart.GeneralRules => GeneralRulesLabel,
        InstructionPart.Language => LanguageLabel,
        InstructionPart.RecordingCriteria => RecordingCriteriaLabel,
        _ => throw new ArgumentOutOfRangeException(nameof(part), part, null),
    };

    private static ShowResult Resident(TacitOptions options, TacitConfig config)
    {
        var language = config.Language;
        var resident = config.Prompts.Resident;
        var document = resident ?? ResidentBlock.Default;
        var source = resident is null ? ShowSource.BuiltInDefault : ShowSource.UserFile;
        var path = resident is null ? null : PromptFiles.ResidentPath(options.TacitHome);
        var text = ResidentFile.Compose(document);
        // The limit counts the file, which can hold more than its composed block (blank lines), so show that length.
        var length = path is null ? text.Length : ResidentFile.FileLength(path);

        var parts = new List<ShowPart> { new("shell", source, path, document.Shell) };
        parts.AddRange(ResidentFile.Sections.Select(section =>
            new ShowPart(section.Name, source, path, ResidentFile.GuidanceOf(document, section.Part) ?? "")));
        var heading = ResidentTitle.Format(language, SourceText(language, source, path), length, ResidentFile.MaxLength);
        return new ShowResult(
            heading + "\n\n" + text,
            parts,
            Whole: new ShowLimit(length, ResidentFile.MaxLength));
    }

    private static ShowResult Skill(TacitOptions options, TacitConfig config)
    {
        var language = config.Language;
        var conventions = config.Prompts.Skill;
        var full = MemorySkill.Compose(conventions).TrimEnd('\n');

        var parts = new List<ShowPart> { new("system", ShowSource.Code, null, MemorySkill.SystemPart) };
        var conventionsLine = SkillNoConventions.In(language);
        if (conventions is not null)
        {
            var path = PromptFiles.SkillPath(options.TacitHome);
            parts.Add(new ShowPart("conventions", ShowSource.UserFile, path, conventions, PromptFiles.SkillLimit));
            conventionsLine = SkillConventions.Format(language, path, conventions.Length, PromptFiles.SkillLimit);
        }

        var heading = SkillTitle.Format(language, full.Length) + "\n" + SkillSources.Format(language, conventionsLine);
        return new ShowResult(heading + "\n\n" + full, parts);
    }

    private static ShowResult Command(string name, string file, MemoryLanguage language)
    {
        var text = file.TrimEnd('\n');
        var heading = CommandTitle.Format(language, name, text.Length) + "\n" + CommandSource.In(language);
        return new ShowResult(heading + "\n\n" + text, [new ShowPart(name, ShowSource.Code, null, text)]);
    }

    /// <summary>A part under its label, its source and its length; an empty part says so.</summary>
    private static string Section(MemoryLanguage language, string label, ShowPart part)
    {
        var body = new StringBuilder(SourceLine.Format(language, label, SourceText(language, part.Source, part.Path), part.Length));
        body.Append('\n').Append(part.Text.Length == 0 ? Empty.In(language) : part.Text);
        return body.ToString();
    }

    private static string SourceText(MemoryLanguage language, ShowSource source, string? path) => source switch
    {
        ShowSource.Code => SourceCode.In(language),
        ShowSource.BuiltInDefault => SourceBuiltIn.In(language),
        _ => SourceFile.Format(language, path),
    };
}
