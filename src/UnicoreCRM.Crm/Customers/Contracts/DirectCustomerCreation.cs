using System.Text.Json.Serialization;
using UnicoreCRM.BuildingBlocks;

namespace UnicoreCRM.Crm.Customers.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateDirectCustomerRequest(string? Type, string? Name, string? Phone = null, string? Email = null,
    string? LegalName = null, string? TaxCode = null, string? Address = null, string? Notes = null,
    string? Segment = null, IReadOnlyList<string>? Tags = null, string? Tier = null, string? ServiceLevel = null,
    bool DifferentCustomerConfirmed = false, IReadOnlyList<RelationshipRefDocument>? ReviewedSources = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CheckCustomerPhoneRequest(string? Phone);

public sealed record CustomerPhoneCandidate(RelationshipRefDocument RelationshipRef, string DisplayName, bool Archived)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? CustomerId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? CustomerStatus { get; init; }
}
public sealed record CustomerPhoneCheckResponse(IReadOnlyList<CustomerPhoneCandidate> Candidates);

public sealed class CustomerSourceValidationException(IReadOnlyDictionary<string, string[]> fields) : Exception("Source profile validation failed.")
{
    public IReadOnlyDictionary<string, string[]> Fields { get; } = fields;
}

/// <summary>Source owner authorizes its own real create command and reads, under the caller's local transaction.</summary>
public interface IDirectCustomerSourceParticipant
{
    string BuyerType { get; }
    Task<IReadOnlyList<CustomerPhoneCandidate>> FindVisiblePhoneMatchesAsync(OwnerTransaction transaction,
        string normalizedPhone, string requestId, string correlationId, CancellationToken cancellationToken);
    Task<string> CreateAsync(OwnerTransaction transaction, CreateDirectCustomerRequest request, string idempotencyKey,
        string requestId, string correlationId, CancellationToken cancellationToken);
}
