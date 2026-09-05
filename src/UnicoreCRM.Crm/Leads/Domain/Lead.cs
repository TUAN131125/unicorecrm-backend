namespace UnicoreCRM.Crm.Leads.Domain;

internal sealed class Lead
{
    private Lead() { }

    internal Lead(string workspaceId, LeadProfile profile, DateTimeOffset now)
    {
        LeadId = LeadIds.New("lead");
        WorkspaceId = workspaceId;
        Profile = profile;
        ScopeOwnerId = profile.OwnerId;
        SearchText = BuildSearchText(LeadId, profile);
        PhoneSearchText = BuildPhoneSearchText(profile);
        WorkState = LeadWorkState.New;
        Score = 0;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public string LeadId { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public LeadProfile Profile { get; private set; } = null!;

    /// <summary>
    /// A queryable projection of <c>Profile.OwnerId</c>. The profile is persisted as a single JSON
    /// column, so the owner inside it cannot be used in a SQL predicate; the AccessControl record
    /// scope has to be pushed into the query rather than filtered in memory, and that needs a real
    /// column. It is derived state kept in step with the profile, never an independent fact.
    /// </summary>
    public string ScopeOwnerId { get; private set; } = null!;
    /// <summary>
    /// A normalized query projection containing the Lead identifier and display name. Optional
    /// protected fields are deliberately not copied here, so list search cannot become a
    /// field-security existence oracle.
    /// </summary>
    public string SearchText { get; private set; } = null!;
    /// <summary>
    /// A separate normalized primary-phone projection. The list query includes it only when the
    /// caller may read the phone field, preserving field-security while keeping phone search in SQL.
    /// </summary>
    public string PhoneSearchText { get; private set; } = string.Empty;
    public LeadWorkState WorkState { get; private set; }
    public LeadQualificationOutcome? QualificationOutcome { get; private set; }
    public int Score { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? DisqualifiedAt { get; private set; }
    public string? DisqualifiedBy { get; private set; }
    public string? DisqualificationReason { get; private set; }
    public string? DisqualificationEvidence { get; private set; }

    /// <summary>
    /// The frozen conversion reference for a positively qualified Lead, projected onto the wire as
    /// <c>LeadDocument.relationshipRef</c>. It is a scalar reference to a foreign owner's aggregate:
    /// it creates no EF navigation, no foreign key and no Contacts persistence access.
    ///
    /// There is deliberately no <c>QualifiedAt</c> or <c>QualifiedBy</c> here. <c>LeadDocument</c>
    /// declares neither and is <c>additionalProperties: false</c>, so qualification time and actor
    /// are authoritative in the Leads command audit record instead. The asymmetry with
    /// <c>DisqualifiedAt</c>/<c>DisqualifiedBy</c> is contract-driven and must not be "fixed" here.
    /// </summary>
    public string? RelationshipType { get; private set; }
    public string? RelationshipId { get; private set; }
    /// <summary>
    /// The Deal produced by OPPORTUNITY qualification. This is the canonical LeadDocument.dealRef
    /// scalar, not an EF navigation and not the older redundant qualifiedDealId property.
    /// </summary>
    public string? DealRef { get; private set; }

    public long Version { get; private set; }

    internal void ReplaceProfile(LeadProfile profile, DateTimeOffset now)
    {
        Profile = profile;
        ScopeOwnerId = profile.OwnerId;
        SearchText = BuildSearchText(LeadId, profile);
        PhoneSearchText = BuildPhoneSearchText(profile);
        Touch(now);
    }

    internal LeadTransitionResult Advance(
        LeadWorkState target,
        LeadVerificationProfile verification,
        DateTimeOffset now)
    {
        if (WorkState == LeadWorkState.Closed
            || target == LeadWorkState.Closed
            || (WorkState != target
                && (WorkState, target) is not (LeadWorkState.New, LeadWorkState.Contacting)
                and not (LeadWorkState.Contacting, LeadWorkState.Verifying)))
        {
            return LeadTransitionResult.InvalidTransition;
        }

        var nextProfile = target == LeadWorkState.Verifying ? Profile.WithVerification(verification) : Profile;
        if (!nextProfile.HasProgressiveProfile())
            return LeadTransitionResult.ProfileIncomplete;

        Profile = nextProfile;
        ScopeOwnerId = nextProfile.OwnerId;
        WorkState = target;
        QualificationOutcome = null;
        Touch(now);
        return LeadTransitionResult.Succeeded;
    }

    internal bool Disqualify(string reason, string evidence, string actorId, DateTimeOffset now)
    {
        if (WorkState == LeadWorkState.Closed)
            return false;
        WorkState = LeadWorkState.Closed;
        QualificationOutcome = LeadQualificationOutcome.Disqualified;
        DisqualifiedAt = now;
        DisqualifiedBy = actorId;
        DisqualificationReason = reason;
        DisqualificationEvidence = evidence;
        Touch(now);
        return true;
    }

    /// <summary>
    /// The terminal positive-qualification transition. Only a VERIFYING Lead may close positively,
    /// and the progressive profile is re-evaluated here rather than assumed from the work state,
    /// because <c>replaceLeadProfile</c> can leave a VERIFYING Lead incomplete.
    ///
    /// It is terminal: <see cref="Reopen"/> admits only a DISQUALIFIED closed Lead, so a positively
    /// qualified Lead can never be reopened.
    /// </summary>
    internal bool QualifyForNurture(string contactId, DateTimeOffset now)
    {
        if (WorkState != LeadWorkState.Verifying || !Profile.HasProgressiveProfile())
            return false;

        WorkState = LeadWorkState.Closed;
        QualificationOutcome = LeadQualificationOutcome.Nurture;
        RelationshipType = LeadRelationshipTypes.Contact;
        RelationshipId = contactId;
        DealRef = null;
        Touch(now);
        return true;
    }

    internal bool QualifyForOpportunity(string contactId, string dealId, DateTimeOffset now)
    {
        if (WorkState != LeadWorkState.Verifying || !Profile.HasProgressiveProfile())
            return false;

        WorkState = LeadWorkState.Closed;
        QualificationOutcome = LeadQualificationOutcome.Opportunity;
        RelationshipType = LeadRelationshipTypes.Contact;
        RelationshipId = contactId;
        DealRef = dealId;
        Touch(now);
        return true;
    }

    internal bool Reopen(DateTimeOffset now)
    {
        if (WorkState != LeadWorkState.Closed
            || QualificationOutcome != LeadQualificationOutcome.Disqualified
            || !Profile.HasProgressiveProfile())
        {
            return false;
        }

        WorkState = LeadWorkState.Contacting;
        QualificationOutcome = null;
        DisqualifiedAt = null;
        DisqualifiedBy = null;
        DisqualificationReason = null;
        DisqualificationEvidence = null;
        RelationshipType = null;
        RelationshipId = null;
        DealRef = null;
        Touch(now);
        return true;
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }

    private static string BuildSearchText(string leadId, LeadProfile profile) =>
        string.Join('\n', leadId, profile.DisplayName).ToUpperInvariant();

    private static string BuildPhoneSearchText(LeadProfile profile) =>
        profile.Phone is null
            ? string.Empty
            : string.Join('\n', profile.Phone, string.Concat(profile.Phone.Where(char.IsDigit))).ToUpperInvariant();
}

internal enum LeadWorkState { New, Contacting, Verifying, Closed }

/// <summary>
/// The implemented terminal outcomes. Direct Sale remains absent because its downstream workflow is
/// not implemented.
/// </summary>
internal enum LeadQualificationOutcome { Disqualified, Nurture, Opportunity }

internal static class LeadRelationshipTypes
{
    internal const string Contact = "CONTACT";
}
internal enum LeadTransitionResult { Succeeded, InvalidTransition, ProfileIncomplete }
