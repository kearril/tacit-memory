namespace TacitMemory.Core.Configuration;

/// <summary>
/// The value each <c>config.yaml</c> key takes when the file leaves it out. The loader falls back to these, and the writer
/// leaves out a key that equals its default. The judgment thresholds and <c>retrieval.minRerankScore</c> have built-in
/// defaults elsewhere, and <c>memory.path</c> defaults to <c>memory</c> under <c>TACIT_HOME</c>.
/// </summary>
internal static class ConfigDefaults
{
    public const int Port = 18888;
    public const int Brief = 3000;
    public const int MinAliases = 2;
    public const int RemindAfterDays = 7;
    public const int RemindCandidates = 5;
    public const int BudgetSeconds = 20;
    public const int MaxConcurrency = 5;
    public const int HintItemLength = 200;
    public const int HintResidentLength = 6000;
    public const int Candidates = 30;
    public const int TopK = 5;
    public const int Keep = 7;

    /// <summary>The address <c>server.host</c> may name that means no extra listener.</summary>
    public const string Host = "127.0.0.1";

    public static string MemoryPath(string tacitHome) => Path.Combine(tacitHome, "memory");
}
