using System.Text.Json.Serialization;

namespace UnicoreCRM.Platform.AccessControl.Contracts;

/// <summary>
/// The admitted request body of <c>evaluateEffectiveRecordAccess</c>. It carries a resource
/// selector only. It deliberately has no Workspace, owner or team member, and unmapped members
/// are rejected, so a caller cannot supply an authoritative record fact: the trusted Workspace
/// comes from Workspace resolution and the owner reference from the owning module.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EvaluateEffectiveRecordAccessRequest(
    string? ResourceKey,
    string? RecordId = null,
    IReadOnlyList<string>? RequestedCommands = null,
    IReadOnlyList<string>? RequestedFields = null,
    bool? IncludeExport = null,
    bool? IncludeApproval = null);

public sealed record EffectiveAccessDecisionReasonDocument(
    string Code,
    string Effect,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Message = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Source = null);

public sealed record EffectiveRecordAccessDocument(
    string WorkspaceId,
    string ResourceKey,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RecordId,
    bool CanRead,
    bool CanUpdate,
    bool CanDelete,
    bool CanExport,
    bool CanApprove,
    IReadOnlyList<string> AllowedCommands,
    IReadOnlyDictionary<string, string> FieldAccess,
    IReadOnlyList<EffectiveAccessDecisionReasonDocument> DecisionReasons,
    DateTimeOffset EvaluatedAt,
    string Authority);
