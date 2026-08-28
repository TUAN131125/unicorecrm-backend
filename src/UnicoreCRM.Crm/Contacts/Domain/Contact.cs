namespace UnicoreCRM.Crm.Contacts.Domain;

/// <summary>
/// Contacts-owned durable read state. This slice has no mutation surface; controlled fixtures and
/// future admitted owner workflows are the only ways state can enter the table.
/// </summary>
internal sealed class Contact
{
    private Contact() { }

    internal string ContactId { get; private set; } = null!;
    internal string WorkspaceId { get; private set; } = null!;
    internal string? OwnerId { get; private set; }
    internal string FullName { get; private set; } = null!;
    internal string Status { get; private set; } = null!;
    internal long Version { get; private set; }
    internal DateTimeOffset CreatedAt { get; private set; }
    internal DateTimeOffset UpdatedAt { get; private set; }
    internal ContactProfile Profile { get; private set; } = new();
}

internal sealed record ContactProfile
{
    public string? Salutation { get; init; }
    public string? JobTitle { get; init; }
    public string? Department { get; init; }
    public string? RoleAtCompany { get; init; }
    public string? WorkEmail { get; init; }
    public string? PersonalEmail { get; init; }
    public string? MobilePhone { get; init; }
    public string? WorkPhone { get; init; }
    public string? OtherPhone { get; init; }
    public string? ZaloId { get; init; }
    public string? Facebook { get; init; }
    public string? PreferredContactChannel { get; init; }
    public string? Address { get; init; }
    public ContactPostalAddress? AddressDetails { get; init; }
    public string? Source { get; init; }
    public ContactCommunicationConsentProfile? Consent { get; init; }
    public bool? DoNotCall { get; init; }
    public bool? DoNotEmail { get; init; }
    public bool? DoNotSms { get; init; }
    public bool? DoNotZalo { get; init; }
    public bool? DoNotContact { get; init; }
    public string? DoNotContactReason { get; init; }
    public string? DecisionRole { get; init; }
    public string? RelationshipLevel { get; init; }
    public string? PainPoint { get; init; }
    public string? NeedSummary { get; init; }
    public string? Notes { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
    public IReadOnlyList<ContactOrganizationRelationship>? OrganizationRelationships { get; init; }
    public string? DisplayName { get; init; }
}

internal sealed record ContactPostalAddress(string Line1)
{
    public string? Line2 { get; init; }
    public string? Ward { get; init; }
    public string? District { get; init; }
    public string? Province { get; init; }
    public string? Country { get; init; }
    public string? PostalCode { get; init; }
    public string? Formatted { get; init; }
}

internal sealed record ContactCommunicationConsentLedgerEntry(
    string Id,
    string Channel,
    string Decision,
    string Source,
    DateTimeOffset OccurredAt)
{
    public string? ActorId { get; init; }
    public string? Evidence { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

internal sealed record ContactCommunicationConsentProfile(
    IReadOnlyDictionary<string, string> Current,
    IReadOnlyList<ContactCommunicationConsentLedgerEntry> Ledger,
    DateTimeOffset UpdatedAt)
{
    public string? LawfulBasis { get; init; }
}

internal sealed record ContactOrganizationRelationship(
    string Id,
    string OrganizationAccountId,
    string Role,
    bool IsPrimaryRepresentative,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset CreatedAt)
{
    public string? RoleTitle { get; init; }
    public string? Department { get; init; }
    public string? DecisionRole { get; init; }
    public DateTimeOffset? EffectiveTo { get; init; }
    public string? CreatedBy { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public string? UpdatedBy { get; init; }
    public string? EndedReason { get; init; }
}

/// <summary>
/// Contacts-owned immutable evidence for successful read operations. AccessControl retains the
/// separate authorization-decision record; this row satisfies the operation's READ_ACCESS_LOG.
/// </summary>
internal sealed class ContactReadAuditRecord
{
    private ContactReadAuditRecord() { }

    internal ContactReadAuditRecord(
        string operation,
        string workspaceId,
        string actorId,
        string? contactId,
        string requestId,
        string correlationId,
        long? contactVersion,
        DateTimeOffset occurredAt)
    {
        AuditId = $"contact_read_audit_{Guid.NewGuid():N}";
        Operation = operation;
        WorkspaceId = workspaceId;
        ActorId = actorId;
        ContactId = contactId;
        RequestId = requestId;
        CorrelationId = correlationId;
        ContactVersion = contactVersion;
        OccurredAt = occurredAt;
    }

    internal string AuditId { get; private set; } = null!;
    internal string Operation { get; private set; } = null!;
    internal string WorkspaceId { get; private set; } = null!;
    internal string ActorId { get; private set; } = null!;
    internal string? ContactId { get; private set; }
    internal string RequestId { get; private set; } = null!;
    internal string CorrelationId { get; private set; } = null!;
    internal long? ContactVersion { get; private set; }
    internal DateTimeOffset OccurredAt { get; private set; }
}
