namespace UnicoreCRM.Crm.Deals.Domain;

internal sealed class Deal
{
    private Deal() { }

    internal Deal(
        string workspaceId,
        DealProfile profile,
        string stageCode,
        DealStageCategory stageCategory,
        DealForecastCategory forecastCategory,
        DateTimeOffset now)
    {
        DealId = DealIds.New("deal");
        WorkspaceId = workspaceId;
        Profile = profile;
        StageCode = stageCode;
        StageCategory = stageCategory;
        ForecastCategory = forecastCategory;
        StageEnteredAt = now;
        ForecastHistory = [];
        CreatedAt = now;
        UpdatedAt = now;
    }

    public string DealId { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public DealProfile Profile { get; private set; } = null!;
    public string StageCode { get; private set; } = null!;
    public DealStageCategory StageCategory { get; private set; }
    public DealForecastCategory ForecastCategory { get; private set; }
    public IReadOnlyList<DealForecastHistory> ForecastHistory { get; private set; } = [];
    public DateTimeOffset StageEnteredAt { get; private set; }
    public DateTimeOffset? NextActionAt { get; private set; }
    public string? NextActionSummary { get; private set; }
    public string? NextActionType { get; private set; }
    public string? NextActionId { get; private set; }
    public string? WinEvidenceType { get; private set; }
    public string? WinEvidenceSourceId { get; private set; }
    public DateTimeOffset? WinEvidenceOccurredAt { get; private set; }
    public DateTimeOffset? WonAt { get; private set; }
    public DateTimeOffset? LostAt { get; private set; }
    public DateOnly? ActualCloseDate { get; private set; }
    public string? LostReason { get; private set; }
    public string? LostReasonNote { get; private set; }
    public DealRecycleDecision? RecycleDecision { get; private set; }
    public bool? RecycleEligible { get; private set; }
    public DateTimeOffset? RevisitAt { get; private set; }
    public DateTimeOffset? ArchivedAt { get; private set; }
    public string? ArchiveReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public long Version { get; private set; }

    internal bool IsTerminal => StageCategory is DealStageCategory.Won or DealStageCategory.Lost;
    internal bool IsArchived => ArchivedAt is not null;

    internal bool ReplaceProfile(DealProfile profile, DateTimeOffset now)
    {
        if (IsArchived)
            return false;
        Profile = profile;
        Touch(now);
        return true;
    }

    internal DealTransitionResult ChangeStage(
        string stageCode,
        DealStageCategory category,
        DealForecastCategory forecastCategory,
        string actorId,
        DateTimeOffset now)
    {
        if (IsArchived || IsTerminal)
            return DealTransitionResult.LifecycleConflict;
        if (category is not DealStageCategory.Open || string.Equals(StageCode, stageCode, StringComparison.Ordinal))
            return DealTransitionResult.InvalidTransition;

        AddForecastHistory(Profile.ExpectedCloseDate, Profile.OpportunityScore, forecastCategory, actorId, now);
        StageCode = stageCode;
        StageCategory = category;
        ForecastCategory = forecastCategory;
        StageEnteredAt = now;
        Touch(now);
        return DealTransitionResult.Succeeded;
    }

    internal bool AssignOwner(string ownerId, DateTimeOffset now)
    {
        if (IsArchived)
            return false;
        Profile = Profile with { OwnerId = ownerId };
        Touch(now);
        return true;
    }

    internal bool UpdateForecast(
        DateOnly expectedCloseDate,
        string opportunityScore,
        DealForecastCategory forecastCategory,
        string actorId,
        DateTimeOffset now)
    {
        if (IsArchived || IsTerminal)
            return false;
        AddForecastHistory(expectedCloseDate, opportunityScore, forecastCategory, actorId, now);
        Profile = Profile with { ExpectedCloseDate = expectedCloseDate, OpportunityScore = opportunityScore };
        ForecastCategory = forecastCategory;
        Touch(now);
        return true;
    }

    internal bool UpdateNextAction(DateTimeOffset nextActionAt, string? summary, string? taskId, DateTimeOffset now)
    {
        if (IsArchived || IsTerminal)
            return false;
        NextActionAt = nextActionAt;
        NextActionSummary = summary ?? NextActionSummary;
        NextActionType = taskId is null ? "MANUAL" : "TASK";
        NextActionId = taskId;
        Touch(now);
        return true;
    }

    internal bool MarkWon(string evidenceType, string sourceId, DateTimeOffset occurredAt, DateTimeOffset now)
    {
        if (IsArchived || IsTerminal)
            return false;
        StageCode = "WON";
        StageCategory = DealStageCategory.Won;
        ForecastCategory = DealForecastCategory.Commit;
        StageEnteredAt = occurredAt;
        WinEvidenceType = evidenceType;
        WinEvidenceSourceId = sourceId;
        WinEvidenceOccurredAt = occurredAt;
        WonAt = occurredAt;
        ActualCloseDate = DateOnly.FromDateTime(occurredAt.UtcDateTime);
        ClearNextAction();
        Touch(now);
        return true;
    }

    internal bool MarkLost(
        string reason,
        string? note,
        DealRecycleDecision recycleDecision,
        DateTimeOffset? revisitAt,
        DateTimeOffset now)
    {
        if (IsArchived || IsTerminal)
            return false;
        StageCode = "LOST";
        StageCategory = DealStageCategory.Lost;
        StageEnteredAt = now;
        LostReason = reason;
        LostReasonNote = note;
        RecycleDecision = recycleDecision;
        RecycleEligible = recycleDecision is not DealRecycleDecision.DoNotRecycle;
        RevisitAt = RecycleEligible.Value ? revisitAt : null;
        LostAt = now;
        ActualCloseDate = DateOnly.FromDateTime(now.UtcDateTime);
        ClearNextAction();
        Touch(now);
        return true;
    }

    internal bool Archive(string reason, DateTimeOffset now)
    {
        if (IsArchived)
            return false;
        ArchivedAt = now;
        ArchiveReason = reason;
        Touch(now);
        return true;
    }

    internal void InitializeNextAction(DateTimeOffset? nextActionAt, string? summary, string? taskId)
    {
        NextActionAt = nextActionAt;
        NextActionSummary = summary;
        if (nextActionAt is not null || summary is not null || taskId is not null)
        {
            NextActionType = taskId is null ? "MANUAL" : "TASK";
            NextActionId = taskId;
        }
    }

    private void AddForecastHistory(
        DateOnly nextExpectedCloseDate,
        string nextProbability,
        DealForecastCategory nextCategory,
        string actorId,
        DateTimeOffset now)
    {
        if (Profile.ExpectedCloseDate == nextExpectedCloseDate
            && Profile.OpportunityScore == nextProbability
            && ForecastCategory == nextCategory)
        {
            return;
        }

        ForecastHistory =
        [
            new DealForecastHistory(
                DealIds.New("deal_forecast"),
                now,
                actorId,
                Profile.ExpectedCloseDate,
                nextExpectedCloseDate,
                Profile.OpportunityScore,
                nextProbability,
                ForecastCategory,
                nextCategory),
            .. ForecastHistory
        ];
    }

    private void ClearNextAction()
    {
        NextActionAt = null;
        NextActionSummary = null;
        NextActionType = null;
        NextActionId = null;
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}

internal sealed record DealProfile(
    string Name,
    DealBuyer BuyerRef,
    DealMoneyValue Amount,
    string OpportunityScore,
    string OwnerId,
    DateOnly ExpectedCloseDate,
    string? ContactId,
    string? SourceLeadId,
    IReadOnlyList<string> InterestedProductIds,
    string? Notes);

internal sealed record DealBuyer(string Type, string Id);
internal sealed record DealMoneyValue(string Amount, string Currency);

internal sealed record DealForecastHistory(
    string Id,
    DateTimeOffset OccurredAt,
    string Actor,
    DateOnly PreviousExpectedCloseDate,
    DateOnly NextExpectedCloseDate,
    string PreviousProbability,
    string NextProbability,
    DealForecastCategory PreviousCategory,
    DealForecastCategory NextCategory);

internal sealed class DealIdempotencyRecord
{
    private DealIdempotencyRecord() { }

    internal DealIdempotencyRecord(
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

internal sealed class DealAuditRecord
{
    private DealAuditRecord() { }

    internal DealAuditRecord(
        string operation,
        string workspaceId,
        string actorId,
        string? aggregateId,
        string requestId,
        string correlationId,
        string outcome,
        long? priorVersion,
        long? newVersion,
        DateTimeOffset occurredAt)
    {
        AuditId = DealIds.New("audit");
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
}

internal sealed class DealOutboxMessage
{
    private DealOutboxMessage() { }

    internal DealOutboxMessage(
        string eventType,
        string aggregateId,
        string workspaceId,
        string correlationId,
        string payloadJson,
        DateTimeOffset occurredAt)
    {
        EventId = DealIds.New("event");
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

internal enum DealStageCategory { Open, Won, Lost }
internal enum DealForecastCategory { Commit, BestCase, Pipeline }
internal enum DealRecycleDecision { Recycle, Conditional, DoNotRecycle }
internal enum DealTransitionResult { Succeeded, InvalidTransition, LifecycleConflict }

internal static class DealIds
{
    internal static string New(string prefix) => $"{prefix}_{Guid.NewGuid():N}";
}
