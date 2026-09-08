namespace UnicoreCRM.Crm.Contacts.Domain;

/// <summary>
/// Contacts-owned durable state. Public Contact commands and the Lead qualification participant
/// are the admitted writers; both use the same aggregate and persistence boundary.
/// </summary>
internal sealed class Contact
{
    private Contact() { }

    /// <summary>
    /// Creates the Contact admitted by the frozen Lead qualification participant contract. Contacts
    /// assigns the identity (LAW-08); no caller may supply or influence it.
    /// </summary>
    internal Contact(
        string workspaceId,
        string? ownerId,
        string fullName,
        string status,
        ContactProfile profile,
        DateTimeOffset now)
    {
        ContactId = ContactIds.New("contact");
        WorkspaceId = workspaceId;
        OwnerId = ownerId;
        FullName = fullName;
        Status = status;
        Version = 0;
        CreatedAt = now;
        UpdatedAt = now;
        Profile = profile;
        SyncEmailIdentityProjections();
    }

    internal string ContactId { get; private set; } = null!;
    internal string WorkspaceId { get; private set; } = null!;
    internal string? OwnerId { get; private set; }
    internal string FullName { get; private set; } = null!;
    internal string Status { get; private set; } = null!;
    internal long Version { get; private set; }
    internal DateTimeOffset CreatedAt { get; private set; }
    internal DateTimeOffset UpdatedAt { get; private set; }
    internal DateTimeOffset? ArchivedAt { get; private set; }
    internal ContactProfile Profile { get; private set; } = new();

    /// <summary>
    /// Queryable projections of <c>Profile.WorkEmail</c> and <c>Profile.PersonalEmail</c> under the
    /// frozen normalization. The profile is persisted as a single JSON column, so the addresses
    /// inside it cannot be used in a SQL predicate; the Workspace-wide duplicate guard has to seek
    /// an index rather than deserialize and filter in memory, and that needs real columns. This is
    /// the same reason - and the same rule - as <c>Lead.ScopeOwnerId</c>: derived state kept in step
    /// with the profile, never an independent fact, and never projected onto the wire.
    /// </summary>
    internal string? NormalizedWorkEmail { get; private set; }
    internal string? NormalizedPersonalEmail { get; private set; }

    private void SyncEmailIdentityProjections()
    {
        NormalizedWorkEmail = ContactEmailIdentity.Normalize(Profile.WorkEmail);
        NormalizedPersonalEmail = ContactEmailIdentity.Normalize(Profile.PersonalEmail);
    }

    internal void Update(string? ownerId, string fullName, ContactProfile profile, DateTimeOffset now)
    {
        if (ArchivedAt is not null)
            throw new InvalidOperationException("An archived Contact cannot be updated.");
        OwnerId = ownerId;
        FullName = fullName;
        // The public profile contract deliberately does not own consent/address-detail or
        // cross-module relationship writes yet. A profile update must preserve those existing
        // authoritative facts rather than clearing them as collateral damage.
        Profile = profile with
        {
            AddressDetails = Profile.AddressDetails,
            Consent = Profile.Consent,
            DoNotCall = Profile.DoNotCall,
            DoNotEmail = Profile.DoNotEmail,
            DoNotSms = Profile.DoNotSms,
            DoNotZalo = Profile.DoNotZalo,
            DoNotContact = Profile.DoNotContact,
            DoNotContactReason = Profile.DoNotContactReason,
            OrganizationRelationships = Profile.OrganizationRelationships
        };
        UpdatedAt = now;
        Version++;
        SyncEmailIdentityProjections();
    }

    internal void Archive(DateTimeOffset now)
    {
        if (ArchivedAt is not null)
            throw new InvalidOperationException("The Contact is already archived.");
        ArchivedAt = now;
        Status = "archived";
        UpdatedAt = now;
        Version++;
    }
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
