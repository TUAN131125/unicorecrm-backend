using System.Text.Json.Serialization;

namespace UnicoreCRM.Communications.Contracts;

public sealed record ChannelProviderDocument(string Key, string Name, string ChannelType,
    string MetadataDraft, string LiveConnection, string Inbound, string Outbound);
public sealed record ChannelAccountDocument(string Id, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? ProviderKey, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? ChannelType,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? DisplayName, string Status, long ConfigurationVersion, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] DateTimeOffset? CreatedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] DateTimeOffset? UpdatedAt, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? ExternalAccountKey = null, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] DateTimeOffset? LastVerifiedAt = null);
public sealed record ChannelSummary(int Total, int Drafts, int Connected, int Disabled, int Unverified, int ProviderTypes);
public sealed record ChannelAccountPage(IReadOnlyList<ChannelAccountDocument> Items, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? NextCursor,
    int TotalCount, ChannelSummary Summary);
public sealed record ChannelMutationDocument(ChannelAccountDocument Account, string AuditId,
    DateTimeOffset OccurredAt, string CorrelationId, bool Replayed = false);
