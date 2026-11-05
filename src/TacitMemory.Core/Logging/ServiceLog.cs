using Microsoft.Extensions.Logging;
using TacitMemory.Core.Localization;

namespace TacitMemory.Core.Logging;

/// <summary>
/// How events reach the log. Each message is written in the memory language. A notice is also shown to the user through
/// <paramref name="show"/>, when there is one, at the level it is logged at. Callers pass only what an event is about:
/// page IDs, short codes, counts, times, paths and system error text. Never memory text, queries, prompts or secrets.
/// </summary>
internal sealed class ServiceLog(ILogger logger, MemoryLanguage language, Action<string>? show = null)
{
    /// <summary>Writes an event to the log only.</summary>
    public void Record(LogLevel level, Localized message, params object?[] args) => Write(level, message.Format(language, args));

    /// <summary>Writes an event to the log only; for text that is already in the memory language.</summary>
    public void Record(LogLevel level, string text) => Write(level, text);

    /// <summary>Shows a notice to the user and writes it to the log.</summary>
    public void Notify(LogLevel level, Localized message, params object?[] args) => Notify(level, message.Format(language, args));

    /// <summary>Shows a notice to the user and writes it to the log; for text that is already in the memory language.</summary>
    public void Notify(LogLevel level, string text)
    {
        show?.Invoke(text);
        Write(level, text);
    }

    private void Write(LogLevel level, string text) => logger.Log(level, "{Text}", text);
}
