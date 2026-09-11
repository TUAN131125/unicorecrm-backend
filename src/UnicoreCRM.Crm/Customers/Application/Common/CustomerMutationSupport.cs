using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UnicoreCRM.Crm.Customers.Contracts;
using UnicoreCRM.Crm.Customers.Domain;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Customers.Application.Common;

internal static class CustomerMutationSupport
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static string Fingerprint<T>(T value) => Hash(JsonSerializer.Serialize(value, Json));
    internal static string ScopeKey(TrustedWorkspaceContext trusted, string operation, string target, string key) =>
        Hash($"{trusted.WorkspaceId}\n{operation}\n{trusted.MemberId}\n{target}\n{key}");
    internal static CustomerMutationResponse Replay(CustomerIdempotencyRecord record, RecordAccessAuthorization access)
    {
        var response = JsonSerializer.Deserialize<CustomerMutationResponse>(record.ResponseJson, Json)
            ?? throw new InvalidOperationException("Invalid Customer replay record.");
        return response with { Outcome = "REPLAYED", Result = CustomerFieldSecurity.Project(response.Result, access) };
    }

    internal static CustomerMutationResponse Commit(ICustomersPersistence persistence, Customer customer,
        CustomerAccess access, CustomerCommandMetadata metadata, string operation, IReadOnlyList<string> eventTypes,
        string target, string fingerprint, DateTimeOffset now)
    {
        var trusted = access.Trusted;
        var audit = new CustomerAuditRecord(operation, trusted.WorkspaceId, trusted.MemberId, customer.CustomerId,
            metadata.RequestId, metadata.CorrelationId, customer.Version, now);
        var payload = JsonSerializer.Serialize(new { customerId = customer.CustomerId, relationshipRef = new { type = customer.RelationshipType, id = customer.RelationshipId }, resourceVersion = customer.Version }, Json);
        var messages = eventTypes.Select(eventType => new CustomerOutboxMessage(eventType, customer.CustomerId,
            trusted.WorkspaceId, metadata.CorrelationId, payload, now)).ToArray();
        var response = new CustomerMutationResponse($"command_{Guid.NewGuid():N}", metadata.CorrelationId,
            customer.CustomerId, "CUSTOMER", customer.Version, CustomerProjection.TimestampValue(now), "COMMITTED",
            CustomerFieldSecurity.Project(CustomerProjection.Document(customer), access.Authorization), [],
            messages.Select(message => message.EventId).ToArray(), [audit.AuditId]);
        persistence.AddAudit(audit);
        foreach (var message in messages) persistence.AddOutbox(message);
        persistence.AddIdempotency(new CustomerIdempotencyRecord(ScopeKey(trusted, operation, target, metadata.IdempotencyKey),
            trusted.WorkspaceId, operation, trusted.MemberId, target, metadata.IdempotencyKey, fingerprint,
            JsonSerializer.Serialize(response, Json), now));
        return response;
    }

    internal static CustomerProfile Profile(CreateCustomerRequest request) => new()
    {
        Segment = Trim(request.Segment), Tags = NormalizeTags(request.Tags), Tier = TrimUpper(request.Tier),
        ServiceLevel = TrimUpper(request.ServiceLevel)
    };
    internal static CustomerProfile Merge(CustomerProfile profile, UpdateCustomerRequest request) => profile with
    {
        Segment = request.Segment is null ? profile.Segment : Trim(request.Segment),
        Tags = request.Tags is null ? profile.Tags : NormalizeTags(request.Tags),
        Tier = request.Tier is null ? profile.Tier : TrimUpper(request.Tier),
        ServiceLevel = request.ServiceLevel is null ? profile.ServiceLevel : TrimUpper(request.ServiceLevel)
    };

    internal static string[] WrittenFields(CreateCustomerRequest request) =>
        new[] { "relationshipRef" }
            .Concat(OptionalProfileFields(request.Segment, request.Tags, request.Tier, request.ServiceLevel))
            .ToArray();

    internal static string[] WrittenFields(UpdateCustomerRequest request) =>
        OptionalProfileFields(request.Segment, request.Tags, request.Tier, request.ServiceLevel)
            .Concat(request.Status is null ? [] : new[] { "status" })
            .ToArray();

    private static IEnumerable<string> OptionalProfileFields(string? segment, IReadOnlyList<string>? tags,
        string? tier, string? serviceLevel)
    {
        if (segment is not null) yield return "segment";
        if (tags is not null) yield return "tags";
        if (tier is not null) yield return "tier";
        if (serviceLevel is not null) yield return "serviceLevel";
    }

    internal static CustomerOperationError? Validate(CreateCustomerRequest request)
    {
        var fields = ValidateProfile(request.Segment, request.Tags, request.Tier, request.ServiceLevel);
        if (request.RelationshipRef is null) fields["relationshipRef"] = ["relationshipRef is required."];
        else
        {
            if (request.RelationshipRef.Type is not ("CONTACT" or "ORGANIZATION_ACCOUNT")) fields["relationshipRef.type"] = ["relationshipRef.type is invalid."];
            if (!IsId(request.RelationshipRef.Id)) fields["relationshipRef.id"] = ["relationshipRef.id is invalid."];
        }
        return fields.Count == 0 ? null : CustomerErrors.Validation(fields);
    }
    internal static CustomerOperationError? Validate(UpdateCustomerRequest request)
    {
        var fields = ValidateProfile(request.Segment, request.Tags, request.Tier, request.ServiceLevel);
        if (request.Segment is null && request.Tags is null && request.Tier is null && request.ServiceLevel is null && request.Status is null)
            fields["body"] = ["At least one editable field is required."];
        if (request.Status is not null && request.Status is not ("ACTIVE" or "INACTIVE")) fields["status"] = ["status is not an admitted lifecycle target."];
        return fields.Count == 0 ? null : CustomerErrors.Validation(fields);
    }
    private static Dictionary<string, string[]> ValidateProfile(string? segment, IReadOnlyList<string>? tags, string? tier, string? serviceLevel)
    {
        var fields = new Dictionary<string, string[]>();
        if (segment?.Trim().Length > 160) fields["segment"] = ["segment must not exceed 160 characters."];
        if (tags is { Count: > 100 } || tags?.Any(x => string.IsNullOrWhiteSpace(x) || x.Trim().Length > 100) == true) fields["tags"] = ["tags must contain at most 100 non-empty values of at most 100 characters."];
        if (tier is not null && TrimUpper(tier) is not ("STANDARD" or "SILVER" or "GOLD" or "PLATINUM" or "STRATEGIC")) fields["tier"] = ["tier is invalid."];
        if (serviceLevel is not null && TrimUpper(serviceLevel) is not ("STANDARD" or "PRIORITY" or "PREMIUM" or "ENTERPRISE")) fields["serviceLevel"] = ["serviceLevel is invalid."];
        return fields;
    }
    private static IReadOnlyList<string> NormalizeTags(IReadOnlyList<string>? tags) => tags is null ? [] : tags.Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? TrimUpper(string? value) => Trim(value)?.ToUpperInvariant();
    private static bool IsId(string? value) => value is { Length: >= 1 and <= 128 } && char.IsLetterOrDigit(value[0]) && value.All(x => char.IsLetterOrDigit(x) || x is '.' or '_' or ':' or '-');
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
