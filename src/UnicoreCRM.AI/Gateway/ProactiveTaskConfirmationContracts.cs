using System.Text.Json.Serialization;

namespace UnicoreCRM.AI.Gateway;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProactiveTaskConfirmationRequest(string? Title, string? AssigneeId, string? DueAt,
    string? Description = null, string? Priority = null);

public sealed record ProactiveTaskConfirmationResponse(string ItemId, string TaskId, long TaskVersion, string Outcome);
