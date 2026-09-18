using System.Text.Json.Serialization;

namespace UnicoreCRM.Communications.Contracts;

/// <summary>
/// Public wire representation of a sender profile. Provider credentials are never projected
/// through this contract.
/// </summary>
public sealed record SenderProfileDocument(
    string Id,
    string WorkspaceId,
    string DisplayName,
    string EmailAddress,
    string Type,
    bool IsDefault,
    bool IsEnabled,
    long Version,
    string CreatedAt,
    string UpdatedAt)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OwnerMembershipId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EmailConnectionId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReplyToAddress { get; init; }
}

/// <summary>
/// Public wire representation of an email provider connection. CredentialReference and every
/// provider token/secret are intentionally absent.
/// </summary>
public sealed record EmailConnectionDocument(
    string Id,
    string WorkspaceId,
    string Provider,
    string EmailAddress,
    string Status,
    string ConnectedAt,
    string UpdatedAt,
    long Version)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OwnerMembershipId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProviderAccountId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RevokedAt { get; init; }
}

public sealed record SenderProfileListResponse(
    IReadOnlyList<SenderProfileDocument> Items,
    string GeneratedAt);

public sealed record EmailConnectionListResponse(
    IReadOnlyList<EmailConnectionDocument> Items,
    string GeneratedAt);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateSenderProfileRequest(
    string? DisplayName,
    string? EmailAddress,
    string? Type)
{
    public string? OwnerMembershipId { get; init; }
    public string? ReplyToAddress { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateSenderProfileRequest
{
    public bool? IsDefault { get; init; }
    public bool? IsEnabled { get; init; }
    public string? ReplyToAddress { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RegisterEmailConnectionRequest(
    string? Provider,
    string? EmailAddress,
    string? CredentialReference)
{
    public string? OwnerMembershipId { get; init; }
    public string? ProviderAccountId { get; init; }
}
