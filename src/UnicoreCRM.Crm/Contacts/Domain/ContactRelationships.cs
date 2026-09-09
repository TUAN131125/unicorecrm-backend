namespace UnicoreCRM.Crm.Contacts.Domain;

internal static class ContactRelationshipRoles
{
    internal static readonly HashSet<string> Organization = new(StringComparer.Ordinal)
    {
        "employee", "executive", "decision_maker", "buyer", "finance", "technical", "advisor", "partner", "other"
    };

    internal static readonly HashSet<string> Customer = new(StringComparer.Ordinal)
    {
        "primary_contact", "billing", "decision_maker", "end_user", "technical", "support", "other"
    };
}

internal sealed class ContactOrganizationRelationship
{
    private ContactOrganizationRelationship() { }

    internal ContactOrganizationRelationship(
        string workspaceId, string contactId, string organizationId, string role,
        bool isPrimaryAffiliation, DateTimeOffset effectiveFrom, string actorId, DateTimeOffset now)
    {
        RelationshipId = ContactIds.New("contact_org_relationship");
        WorkspaceId = workspaceId;
        ContactId = contactId;
        OrganizationId = organizationId;
        Role = role;
        IsPrimaryAffiliation = isPrimaryAffiliation;
        EffectiveFrom = effectiveFrom;
        CreatedAt = now;
        CreatedBy = actorId;
        UpdatedAt = now;
        UpdatedBy = actorId;
    }

    internal string RelationshipId { get; private set; } = null!;
    internal string WorkspaceId { get; private set; } = null!;
    internal string ContactId { get; private set; } = null!;
    internal string OrganizationId { get; private set; } = null!;
    internal string Role { get; private set; } = null!;
    internal bool IsPrimaryAffiliation { get; private set; }
    internal DateTimeOffset EffectiveFrom { get; private set; }
    internal DateTimeOffset? EffectiveTo { get; private set; }
    internal string? EndedReason { get; private set; }
    internal DateTimeOffset CreatedAt { get; private set; }
    internal string CreatedBy { get; private set; } = null!;
    internal DateTimeOffset UpdatedAt { get; private set; }
    internal string UpdatedBy { get; private set; } = null!;
    internal string? LegacyEvidenceJson { get; private set; }

    internal void Update(string role, bool isPrimaryAffiliation, string actorId, DateTimeOffset now)
    {
        EnsureActive();
        Role = role;
        IsPrimaryAffiliation = isPrimaryAffiliation;
        UpdatedAt = now;
        UpdatedBy = actorId;
    }

    internal void ClearPrimary(string actorId, DateTimeOffset now)
    {
        EnsureActive();
        IsPrimaryAffiliation = false;
        UpdatedAt = now;
        UpdatedBy = actorId;
    }

    internal void End(DateTimeOffset effectiveTo, string endedReason, string actorId, DateTimeOffset now)
    {
        EnsureActive();
        EffectiveTo = effectiveTo;
        EndedReason = endedReason;
        IsPrimaryAffiliation = false;
        UpdatedAt = now;
        UpdatedBy = actorId;
    }

    private void EnsureActive()
    {
        if (EffectiveTo is not null) throw new InvalidOperationException("The Organization relationship has ended.");
    }
}

internal sealed class ContactCustomerRelationship
{
    private ContactCustomerRelationship() { }

    internal ContactCustomerRelationship(
        string workspaceId, string contactId, string customerId, string role,
        DateTimeOffset effectiveFrom, string actorId, DateTimeOffset now)
    {
        RelationshipId = ContactIds.New("contact_customer_relationship");
        WorkspaceId = workspaceId;
        ContactId = contactId;
        CustomerId = customerId;
        Role = role;
        EffectiveFrom = effectiveFrom;
        CreatedAt = now;
        CreatedBy = actorId;
        UpdatedAt = now;
        UpdatedBy = actorId;
    }

    internal string RelationshipId { get; private set; } = null!;
    internal string WorkspaceId { get; private set; } = null!;
    internal string ContactId { get; private set; } = null!;
    internal string CustomerId { get; private set; } = null!;
    internal string Role { get; private set; } = null!;
    internal DateTimeOffset EffectiveFrom { get; private set; }
    internal DateTimeOffset? EffectiveTo { get; private set; }
    internal string? EndedReason { get; private set; }
    internal DateTimeOffset CreatedAt { get; private set; }
    internal string CreatedBy { get; private set; } = null!;
    internal DateTimeOffset UpdatedAt { get; private set; }
    internal string UpdatedBy { get; private set; } = null!;

    internal void Update(string role, string actorId, DateTimeOffset now)
    {
        EnsureActive();
        Role = role;
        UpdatedAt = now;
        UpdatedBy = actorId;
    }

    internal void End(DateTimeOffset effectiveTo, string endedReason, string actorId, DateTimeOffset now)
    {
        EnsureActive();
        EffectiveTo = effectiveTo;
        EndedReason = endedReason;
        UpdatedAt = now;
        UpdatedBy = actorId;
    }

    private void EnsureActive()
    {
        if (EffectiveTo is not null) throw new InvalidOperationException("The Customer relationship has ended.");
    }
}
