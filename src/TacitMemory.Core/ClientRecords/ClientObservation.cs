namespace TacitMemory.Core;

/// <summary>
/// What the service learned from one request of a client: its name and version, the protocol revision the request used, the
/// capabilities it declared (such as <c>sampling</c>, <c>elicitation</c> or <c>roots</c>) and the extension identifiers it
/// declared. Plain values only: the host reads them from the SDK and hands them to <see cref="MemoryService.RecordClient"/>.
/// </summary>
/// <param name="ProtocolVersion">The protocol revision of the request, such as <c>2026-07-28</c>.</param>
public sealed record ClientObservation(
    string Name,
    string Version,
    string ProtocolVersion,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> Extensions);
