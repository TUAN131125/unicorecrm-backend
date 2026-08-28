namespace UnicoreCRM.Crm.Customers.Domain;

/// <summary>
/// Customers-owned durable read state. This slice exposes no creation or mutation path; controlled
/// verifier fixtures and future admitted Customers workflows are the only ways rows can be written.
/// </summary>
internal sealed class Customer
{
    private Customer() { }

    internal string WorkspaceId { get; private set; } = null!;
    internal string CustomerId { get; private set; } = null!;
    internal string CustomerCode { get; private set; } = null!;
    internal string Type { get; private set; } = null!;
    internal string RelationshipType { get; private set; } = null!;
    internal string RelationshipId { get; private set; } = null!;
    internal string Status { get; private set; } = null!;
    internal string Health { get; private set; } = null!;
    internal DateTimeOffset FirstPurchaseAt { get; private set; }
    internal DateTimeOffset LastPurchaseAt { get; private set; }
    internal long Version { get; private set; }
    internal DateTimeOffset CreatedAt { get; private set; }
    internal DateTimeOffset UpdatedAt { get; private set; }
    internal CustomerProfile Profile { get; private set; } = new();
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
