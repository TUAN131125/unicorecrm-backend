using System.Text.Json.Serialization;

namespace UnicoreCRM.AI.Gateway;

public sealed record ProactiveItemView(
    string ItemId,
    string CustomerId,
    string CustomerLabel,
    string Severity,
    string ReasonCode,
    string Status,
    DateTimeOffset FirstDetectedAt,
    DateTimeOffset LastDetectedAt,
    DateTimeOffset? SeenAt,
    DateTimeOffset? SnoozedUntil,
    long Version);

public sealed record ProactiveItemPage(IReadOnlyList<ProactiveItemView> Items, string? NextCursor);
public sealed record ProactiveConfigurationView(bool Enabled, long Version, DateTimeOffset? LastEvaluationAt, DateTimeOffset? NextEvaluationAt, DateTimeOffset? UpdatedAt);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SaveProactiveConfigurationRequest(bool Enabled);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SnoozeProactiveItemRequest(DateTimeOffset SnoozedUntil);
public sealed record ProactiveMutationResponse(ProactiveItemView Item);
