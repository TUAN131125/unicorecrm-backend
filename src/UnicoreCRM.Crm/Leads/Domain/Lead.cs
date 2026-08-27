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
internal enum LeadQualificationOutcome { Disqualified }
internal enum LeadTransitionResult { Succeeded, InvalidTransition, ProfileIncomplete }
