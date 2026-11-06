namespace TacitMemory.Core.Configuration;

/// <summary>
/// What the status report reads from a <c>config.yaml</c> that may not load: the port the service is probed on and the
/// language its messages are written in. Each is the value the file gives when it can be read, otherwise its default.
/// </summary>
/// <param name="Port">The port in <c>server.port</c>, or 18888 when the file does not give a valid one.</param>
/// <param name="Language">The <c>language</c> key, or the system UI language when it is absent or not a valid value.</param>
public sealed record LooseSettings(int Port, MemoryLanguage Language);
