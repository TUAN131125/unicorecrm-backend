namespace UnicoreCRM.Crm.Organizations.Domain;

/// <summary>
/// Organizations-owned durable aggregate. Relationship membership remains Contacts-owned.
/// </summary>
internal sealed class Organization
{
    private Organization() { }

    internal Organization(string workspaceId, string ownerId, string displayName, string status, OrganizationProfile profile, DateTimeOffset now)
    {
        OrganizationId = $"organization_{Guid.NewGuid():N}";
        WorkspaceId = workspaceId;
        OwnerId = ownerId;
        DisplayName = displayName;
        Status = status;
        Version = 0;
        CreatedAt = now;
        UpdatedAt = now;
        Profile = profile;
        SyncQueryProjections();
    }

    internal string OrganizationId { get; private set; } = null!;
    internal string WorkspaceId { get; private set; } = null!;
    internal string DisplayName { get; private set; } = null!;
    internal string Status { get; private set; } = null!;
    internal string? OwnerId { get; private set; }
    internal long Version { get; private set; }
    internal DateTimeOffset CreatedAt { get; private set; }
    internal DateTimeOffset UpdatedAt { get; private set; }
    internal OrganizationProfile Profile { get; private set; } = new();
    internal string SearchText { get; private set; } = null!;
    internal string? Industry { get; private set; }
    internal string? SizeBand { get; private set; }

    internal void Update(string displayName, string status, OrganizationProfile profile, DateTimeOffset now)
    {
        if (Status == "archived") throw new InvalidOperationException("An archived Organization cannot be updated.");
        DisplayName = displayName;
        Status = status;
        Profile = profile;
        SyncQueryProjections();
        UpdatedAt = now;
        Version++;
    }

    internal void Archive(DateTimeOffset now)
    {
        if (Status == "archived") throw new InvalidOperationException("The Organization is already archived.");
        Status = "archived";
        UpdatedAt = now;
        Version++;
    }

    private void SyncQueryProjections()
    {
        Industry = Normalize(Profile.Industry, 160);
        SizeBand = Normalize(Profile.SizeBand, 80);
        var searchText = string.Join(' ', new[] { DisplayName, Profile.LegalName, Profile.TaxCode, Profile.Domain }
            .Where(value => !string.IsNullOrWhiteSpace(value))).ToUpperInvariant();
        SearchText = searchText[..Math.Min(800, searchText.Length)];
    }

    private static string? Normalize(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim().ToUpperInvariant();
        return normalized[..Math.Min(maxLength, normalized.Length)];
    }
}

internal sealed record OrganizationProfile
{
    public string? LegalName { get; init; }
    public string? TaxCode { get; init; }
    public string? Domain { get; init; }
    public string? Website { get; init; }
    public string? Industry { get; init; }
    public string? SizeBand { get; init; }
    public int? EmployeeCount { get; init; }
    public decimal? AnnualRevenue { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? Address { get; init; }
    public OrganizationPostalAddress? AddressDetails { get; init; }
    public string? Source { get; init; }
    // Relationship membership is never persisted in this profile; Contacts owns the C6 ledger.
    public string? PrimaryContactId { get; init; }
    public IReadOnlyList<string>? ContactRefs { get; init; }
    public string? RelationshipLevel { get; init; }
    public string? Notes { get; init; }
    public string? ExternalRef { get; init; }
}

internal sealed record OrganizationPostalAddress(string Line1)
{
    public string? Line2 { get; init; }
    public string? Ward { get; init; }
    public string? District { get; init; }
    public string? Province { get; init; }
    public string? Country { get; init; }
    public string? PostalCode { get; init; }
    public string? Formatted { get; init; }
}

/// <summary>
/// Organizations-owned immutable evidence for successful read operations. AccessControl retains
/// the separate authorization-decision record.
/// </summary>
internal sealed class OrganizationReadAuditRecord
{
    private OrganizationReadAuditRecord() { }

    internal OrganizationReadAuditRecord(
        string operation,
        string workspaceId,
        string actorId,
        string? organizationId,
        string requestId,
        string correlationId,
        long? organizationVersion,
        DateTimeOffset occurredAt)
    {
        AuditId = $"organization_read_audit_{Guid.NewGuid():N}";
        Operation = operation;
        WorkspaceId = workspaceId;
        ActorId = actorId;
        OrganizationId = organizationId;
        RequestId = requestId;
        CorrelationId = correlationId;
        OrganizationVersion = organizationVersion;
        OccurredAt = occurredAt;
    }

    internal string AuditId { get; private set; } = null!;
    internal string Operation { get; private set; } = null!;
    internal string WorkspaceId { get; private set; } = null!;
    internal string ActorId { get; private set; } = null!;
    internal string? OrganizationId { get; private set; }
    internal string RequestId { get; private set; } = null!;
    internal string CorrelationId { get; private set; } = null!;
    internal long? OrganizationVersion { get; private set; }
    internal DateTimeOffset OccurredAt { get; private set; }
}
