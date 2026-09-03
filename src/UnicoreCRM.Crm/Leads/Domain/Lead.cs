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

    public long Version { get; private set; }

    internal void ReplaceProfile(LeadProfile profile, DateTimeOffset now)
    {
        Profile = profile;
        ScopeOwnerId = profile.OwnerId;
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
        Touch(now);
        return true;
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}

internal enum LeadWorkState { New, Contacting, Verifying, Closed }

/// <summary>
/// The admitted terminal outcomes. <c>Opportunity</c> and <c>DirectSale</c> are declared by the
/// contract but are not added here: their workflows have no implemented downstream participant, and
/// an unreachable enum member would misrepresent what this owner can actually produce.
/// </summary>
internal enum LeadQualificationOutcome { Disqualified, Nurture }

internal static class LeadRelationshipTypes
{
    internal const string Contact = "CONTACT";
}
internal enum LeadTransitionResult { Succeeded, InvalidTransition, ProfileIncomplete }
