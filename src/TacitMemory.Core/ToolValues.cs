namespace TacitMemory.Core;

/// <summary>
/// The values the memory tools accept for their enumerated parameters. Core checks requests against these constants,
/// and the MCP server lists them in the tools' input schemas, so each value is written once, here. The constants are
/// <c>const</c> so that attributes and <c>switch</c> labels can use them.
/// </summary>
public static class ToolValues
{
    /// <summary>The <c>op</c> values of <c>write_page</c>.</summary>
    public static class WritePageOps
    {
        public const string Create = "create";
        public const string UpsertSection = "upsert_section";
        public const string DeleteSection = "delete_section";
        public const string UpdateHeader = "update_header";
    }

    /// <summary>The <c>op</c> values of <c>write_persona</c>.</summary>
    public static class WritePersonaOps
    {
        public const string Add = "add";
        public const string Propose = "propose";
        public const string Replace = "replace";
        public const string Delete = "delete";
    }

    /// <summary>The <c>persona_section</c> values of <c>write_persona</c>.</summary>
    public static class PersonaSections
    {
        public const string RedLines = "red_lines";
        public const string WorkingRules = "working_rules";
        public const string Preferences = "preferences";
        public const string Profile = "profile";
    }

    /// <summary>The <c>kind</c> values of <c>recall</c>.</summary>
    public static class RecallKinds
    {
        public const string Pages = "pages";
        public const string Activities = "activities";
    }
}
