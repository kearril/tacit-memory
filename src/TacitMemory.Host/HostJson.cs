using System.Text.Json.Serialization;
using TacitMemory.Core;
using TacitMemory.Host.Api;

namespace TacitMemory.Host;

/// <summary>
/// The JSON contracts of the management API and of the memory tools' parameters, generated when the project builds. The API's
/// options, the framework's own (which the OpenAPI document is made from) and the tools' take their contracts from here; a
/// type missing here fails at its first use.
/// </summary>
[JsonSerializable(typeof(ApiError))]
[JsonSerializable(typeof(StatusReport))]
[JsonSerializable(typeof(ActivityReport))]
[JsonSerializable(typeof(ConsolidationDue))]
[JsonSerializable(typeof(ServiceLogDays))]
[JsonSerializable(typeof(ServiceLogDay))]
[JsonSerializable(typeof(BackupList))]
[JsonSerializable(typeof(PromptList))]
[JsonSerializable(typeof(PromptSave))]
[JsonSerializable(typeof(PromptSaved))]
[JsonSerializable(typeof(PromptDraft))]
[JsonSerializable(typeof(PromptCheck))]
[JsonSerializable(typeof(ProbeReport))]
[JsonSerializable(typeof(ConfigView))]
[JsonSerializable(typeof(ConfigSave))]
[JsonSerializable(typeof(ConfigSaved))]
[JsonSerializable(typeof(BindingList))]
[JsonSerializable(typeof(ConnectRequest))]
[JsonSerializable(typeof(BindingDone))]
[JsonSerializable(typeof(SessionStatus))]
[JsonSerializable(typeof(PasswordBody))]
[JsonSerializable(typeof(AccessPasswordBody))]
[JsonSerializable(typeof(ReindexResult))]
[JsonSerializable(typeof(BackupBody))]
[JsonSerializable(typeof(BackupResult))]
[JsonSerializable(typeof(RestoreBody))]
[JsonSerializable(typeof(RestorePreview))]
[JsonSerializable(typeof(RestoreResult))]
[JsonSerializable(typeof(RecallTraceRequest))]
[JsonSerializable(typeof(RecallTraceReport))]
[JsonSerializable(typeof(PersonaReport))]
[JsonSerializable(typeof(PageList))]
[JsonSerializable(typeof(PageView))]
[JsonSerializable(typeof(WorkstreamList))]
[JsonSerializable(typeof(WorkstreamBriefView))]
[JsonSerializable(typeof(ActivityMonths))]
[JsonSerializable(typeof(ActivityMonthView))]
[JsonSerializable(typeof(PageSectionInput[]))]
[JsonSerializable(typeof(string[]))]
public sealed partial class HostJson : JsonSerializerContext;
