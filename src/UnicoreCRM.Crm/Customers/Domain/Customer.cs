namespace UnicoreCRM.Crm.Customers.Domain;

/// <summary>Customers-owned commercial account aggregate.</summary>
internal sealed class Customer
{
    private Customer() { }

    internal Customer(string workspaceId, string ownerId, string relationshipType, string relationshipId,
        CustomerProfile profile, DateTimeOffset now)
    {
        WorkspaceId = workspaceId;
        CustomerId = $"customer_{Guid.NewGuid():N}";
        CustomerCode = $"CU-{Guid.NewGuid():N}".ToUpperInvariant();
        OwnerId = ownerId;
        RelationshipType = relationshipType;
        RelationshipId = relationshipId;
        Type = relationshipType == "CONTACT" ? "B2C" : "B2B";
        Status = "NEW";
        Version = 0;
        CreatedAt = now;
        UpdatedAt = now;
        Profile = profile;
        SyncQueryProjections();
    }

    internal string WorkspaceId { get; private set; } = null!;
    internal string CustomerId { get; private set; } = null!;
    internal string CustomerCode { get; private set; } = null!;
    internal string? OwnerId { get; private set; }
    internal string Type { get; private set; } = null!;
    internal string RelationshipType { get; private set; } = null!;
    internal string RelationshipId { get; private set; } = null!;
    internal string Status { get; private set; } = null!;
    internal string? Health { get; private set; }
    internal DateTimeOffset? FirstPurchaseAt { get; private set; }
    internal DateTimeOffset? LastPurchaseAt { get; private set; }
    internal long Version { get; private set; }
    internal DateTimeOffset CreatedAt { get; private set; }
    internal DateTimeOffset UpdatedAt { get; private set; }
    internal CustomerProfile Profile { get; private set; } = new();
    internal string SearchText { get; private set; } = null!;
    internal string? Segment { get; private set; }
    internal string? Tier { get; private set; }

    internal void Update(CustomerProfile profile, string? lifecycleTarget, DateTimeOffset now)
    {
        if (Status == "ARCHIVED") throw new InvalidOperationException("An archived Customer cannot be updated.");
        if (lifecycleTarget is not null)
        {
            var allowed = (Status, lifecycleTarget) is ("NEW", "ACTIVE") or ("ACTIVE", "INACTIVE") or ("INACTIVE", "ACTIVE");
            if (!allowed) throw new InvalidOperationException("The Customer lifecycle transition is not admitted.");
            Status = lifecycleTarget;
        }
        Profile = profile;
        SyncQueryProjections();
        UpdatedAt = now;
        Version++;
    }

    internal void Archive(DateTimeOffset now)
    {
        if (Status == "ARCHIVED") throw new InvalidOperationException("The Customer is already archived.");
        Status = "ARCHIVED";
        UpdatedAt = now;
        Version++;
    }

    private void SyncQueryProjections()
    {
        Segment = Normalize(Profile.Segment, 160);
        Tier = Normalize(Profile.Tier, 40);
        var search = string.Join(' ', new[] { CustomerCode, Segment, Tier }.Where(x => !string.IsNullOrWhiteSpace(x))).ToUpperInvariant();
        SearchText = search[..Math.Min(400, search.Length)];
    }

    private static string? Normalize(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim().ToUpperInvariant();
        return normalized[..Math.Min(maxLength, normalized.Length)];
    }
}

internal sealed record CustomerProfile
{
    public string? CalculatedHealth { get; init; }
    public string? ManualHealthOverride { get; init; }
    public string? OnboardingStatus { get; init; }
    public DateTimeOffset? OnboardingCompletedAt { get; init; }
    public string? CreatedFromEvidenceId { get; init; }
    public string? ConversionPolicyVersion { get; init; }
    public string? ConversionCorrelationId { get; init; }
    public string? SourceSystem { get; init; }
    public string? ExternalCustomerRef { get; init; }
    public string? Tier { get; init; }
    public string? ServiceLevel { get; init; }
    public int? CareCadenceDays { get; init; }
    public string? CareOwnerId { get; init; }
    public string? Segment { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
    public DateTimeOffset? NextCareAt { get; init; }
    public DateTimeOffset? LastCareAt { get; init; }
}

internal sealed class CustomerIdempotencyRecord
{
    private CustomerIdempotencyRecord() { }
    internal CustomerIdempotencyRecord(string scopeKey, string workspaceId, string operation, string actorId,
        string targetId, string key, string fingerprint, string responseJson, DateTimeOffset createdAt) =>
        (ScopeKey, WorkspaceId, Operation, ActorId, TargetId, IdempotencyKey, Fingerprint, ResponseJson, CreatedAt) =
        (scopeKey, workspaceId, operation, actorId, targetId, key, fingerprint, responseJson, createdAt);
    internal string ScopeKey { get; private set; } = null!;
    internal string WorkspaceId { get; private set; } = null!;
    internal string Operation { get; private set; } = null!;
    internal string ActorId { get; private set; } = null!;
    internal string TargetId { get; private set; } = null!;
    internal string IdempotencyKey { get; private set; } = null!;
    internal string Fingerprint { get; private set; } = null!;
    internal string ResponseJson { get; private set; } = null!;
    internal DateTimeOffset CreatedAt { get; private set; }
}

internal sealed class CustomerAuditRecord
{
    private CustomerAuditRecord() { }
    internal CustomerAuditRecord(string operation, string workspaceId, string actorId, string aggregateId,
        string requestId, string correlationId, long version, DateTimeOffset occurredAt)
    {
        AuditId = $"customer_audit_{Guid.NewGuid():N}"; Operation = operation; WorkspaceId = workspaceId;
        ActorId = actorId; AggregateId = aggregateId; RequestId = requestId; CorrelationId = correlationId;
        NewVersion = version; OccurredAt = occurredAt;
    }
    internal string AuditId { get; private set; } = null!;
    internal string Operation { get; private set; } = null!;
    internal string WorkspaceId { get; private set; } = null!;
    internal string ActorId { get; private set; } = null!;
    internal string AggregateId { get; private set; } = null!;
    internal string RequestId { get; private set; } = null!;
    internal string CorrelationId { get; private set; } = null!;
    internal long NewVersion { get; private set; }
    internal DateTimeOffset OccurredAt { get; private set; }
}

internal sealed class CustomerOutboxMessage
{
    private CustomerOutboxMessage() { }
    internal CustomerOutboxMessage(string type, string aggregateId, string workspaceId, string correlationId,
        string payloadJson, DateTimeOffset occurredAt)
    {
        EventId = $"customer_event_{Guid.NewGuid():N}"; EventType = type; AggregateId = aggregateId;
        WorkspaceId = workspaceId; CorrelationId = correlationId; PayloadJson = payloadJson; OccurredAt = occurredAt;
    }
    internal string EventId { get; private set; } = null!;
    internal string EventType { get; private set; } = null!;
    internal string AggregateId { get; private set; } = null!;
    internal string WorkspaceId { get; private set; } = null!;
    internal string CorrelationId { get; private set; } = null!;
    internal string PayloadJson { get; private set; } = null!;
    internal DateTimeOffset OccurredAt { get; private set; }
}

/// <summary>
/// Customers-owned immutable evidence for successful reads. AccessControl separately owns the
/// authorization-decision evidence.
/// </summary>
internal sealed class CustomerReadAuditRecord
{
    private CustomerReadAuditRecord() { }

    internal CustomerReadAuditRecord(
        string operation,
        string workspaceId,
        string actorId,
        string? customerId,
        string requestId,
        string correlationId,
        long? customerVersion,
        DateTimeOffset occurredAt)
    {
        AuditId = $"customer_read_audit_{Guid.NewGuid():N}";
        Operation = operation;
        WorkspaceId = workspaceId;
        ActorId = actorId;
        CustomerId = customerId;
        RequestId = requestId;
        CorrelationId = correlationId;
        CustomerVersion = customerVersion;
        OccurredAt = occurredAt;
    }

    internal string AuditId { get; private set; } = null!;
    internal string Operation { get; private set; } = null!;
    internal string WorkspaceId { get; private set; } = null!;
    internal string ActorId { get; private set; } = null!;
    internal string? CustomerId { get; private set; }
    internal string RequestId { get; private set; } = null!;
    internal string CorrelationId { get; private set; } = null!;
    internal long? CustomerVersion { get; private set; }
    internal DateTimeOffset OccurredAt { get; private set; }
}
