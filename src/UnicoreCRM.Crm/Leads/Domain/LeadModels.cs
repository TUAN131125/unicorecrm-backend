namespace UnicoreCRM.Crm.Leads.Domain;

internal sealed class Lead
{
    private Lead() { }

    internal Lead(string workspaceId, LeadProfile profile, DateTimeOffset now)
    {
        LeadId = LeadIds.New("lead");
        WorkspaceId = workspaceId;
        Profile = profile;
        WorkState = LeadWorkState.New;
        Score = 0;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public string LeadId { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public LeadProfile Profile { get; private set; } = null!;
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

internal sealed record LeadProfile(
    string DisplayName,
    string? Salutation,
    string? Title,
    string? Department,
    string? Phone,
    string? WorkPhone,
    string? OtherPhone,
    string? Email,
    string? PersonalEmail,
    string? ZaloId,
    string? Facebook,
    string? PreferredChannel,
    bool? DoNotCall,
    bool? DoNotEmail,
    string? CompanyName,
    string? CompanySize,
    string? Industry,
    string? BusinessType,
    string? Website,
    string? TaxCode,
    string? CompanyAddress,
    string? Country,
    string? Province,
    string? District,
    string? Ward,
    string? ContactAddress,
    string Source,
    string? CampaignId,
    string OwnerId,
    string? AssignedTeam,
    string? DecisionRole,
    string? Priority,
    IReadOnlyList<LeadInterestedProduct> InterestedProducts,
    LeadMoney EstimatedValue,
    string? BudgetRange,
    string? PurchaseTimeline,
    string? PainPoint,
    DateTimeOffset? NextFollowUpAt,
    string? FollowUpNote,
    IReadOnlyList<string> Tags,
    string? Description,
    string? InternalNotes,
    IReadOnlyList<LeadCustomField> CustomFields)
{
    internal bool HasProgressiveProfile() =>
        DisplayName.Length != 0
        && OwnerId.Length != 0
        && new[] { Phone, WorkPhone, OtherPhone, Email, PersonalEmail, ZaloId, Facebook }
            .Any(value => !string.IsNullOrWhiteSpace(value));

    internal LeadProfile WithVerification(LeadVerificationProfile verification) => this with
    {
        CompanyName = verification.CompanyName ?? CompanyName,
        PainPoint = verification.PainPoint ?? PainPoint,
        NextFollowUpAt = verification.NextFollowUpAt ?? NextFollowUpAt
    };
}

internal sealed record LeadMoney(string Amount, string Currency);
internal sealed record LeadInterestedProduct(
    string Id,
    string ProductId,
    string ProductNameSnapshot,
    string InterestLevel,
    int? EstimatedQuantity,
    LeadMoney? ExpectedBudget,
    string? Note,
    DateTimeOffset CreatedAt);
internal sealed record LeadCustomField(
    string FieldKey,
    string ValueType,
    string? StringValue,
    string? DecimalValue,
    bool? BooleanValue,
    IReadOnlyList<string>? StringArrayValue);
internal sealed record LeadVerificationProfile(string? CompanyName, string? PainPoint, DateTimeOffset? NextFollowUpAt);

internal sealed class LeadIdempotencyRecord
{
    private LeadIdempotencyRecord() { }

    internal LeadIdempotencyRecord(
        string scopeKey,
        string workspaceId,
        string operation,
        string actorId,
        string targetId,
        string idempotencyKey,
        string fingerprint,
        string responseJson,
        DateTimeOffset createdAt)
    {
        ScopeKey = scopeKey;
        WorkspaceId = workspaceId;
        Operation = operation;
        ActorId = actorId;
        TargetId = targetId;
        IdempotencyKey = idempotencyKey;
        Fingerprint = fingerprint;
        ResponseJson = responseJson;
        CreatedAt = createdAt;
    }

    public string ScopeKey { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public string Operation { get; private set; } = null!;
    public string ActorId { get; private set; } = null!;
    public string TargetId { get; private set; } = null!;
    public string IdempotencyKey { get; private set; } = null!;
    public string Fingerprint { get; private set; } = null!;
    public string ResponseJson { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
}

internal sealed class LeadAuditRecord
{
    private LeadAuditRecord() { }

    internal LeadAuditRecord(
        string operation,
        string workspaceId,
        string actorId,
        string? aggregateId,
        string requestId,
        string correlationId,
        string outcome,
        long? priorVersion,
        long? newVersion,
        DateTimeOffset occurredAt,
        string actorType = "Member",
        string? delegatedSubjectId = null,
        string? sourceReference = null)
    {
        AuditId = LeadIds.New("audit");
        Operation = operation;
        WorkspaceId = workspaceId;
        ActorId = actorId;
        AggregateId = aggregateId;
        RequestId = requestId;
        CorrelationId = correlationId;
        Outcome = outcome;
        PriorVersion = priorVersion;
        NewVersion = newVersion;
        OccurredAt = occurredAt;
        ActorType = actorType;
        DelegatedSubjectId = delegatedSubjectId;
        SourceReference = sourceReference;
    }

    public string AuditId { get; private set; } = null!;
    public string Operation { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public string ActorId { get; private set; } = null!;
    public string? AggregateId { get; private set; }
    public string RequestId { get; private set; } = null!;
    public string CorrelationId { get; private set; } = null!;
    public string Outcome { get; private set; } = null!;
    public long? PriorVersion { get; private set; }
    public long? NewVersion { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string ActorType { get; private set; } = null!;
    public string? DelegatedSubjectId { get; private set; }
    public string? SourceReference { get; private set; }
}

internal sealed class LeadOutboxMessage
{
    private LeadOutboxMessage() { }

    internal LeadOutboxMessage(
        string eventType,
        string aggregateId,
        string workspaceId,
        string correlationId,
        string payloadJson,
        DateTimeOffset occurredAt)
    {
        EventId = LeadIds.New("event");
        EventType = eventType;
        AggregateId = aggregateId;
        WorkspaceId = workspaceId;
        CorrelationId = correlationId;
        PayloadJson = payloadJson;
        OccurredAt = occurredAt;
    }

    public string EventId { get; private set; } = null!;
    public string EventType { get; private set; } = null!;
    public string AggregateId { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public string CorrelationId { get; private set; } = null!;
    public string PayloadJson { get; private set; } = null!;
    public DateTimeOffset OccurredAt { get; private set; }
}

internal enum LeadWorkState { New, Contacting, Verifying, Closed }
internal enum LeadQualificationOutcome { Disqualified }
internal enum LeadTransitionResult { Succeeded, InvalidTransition, ProfileIncomplete }

internal static class LeadIds
{
    internal static string New(string prefix) => $"{prefix}_{Guid.NewGuid():N}";
}
