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
        ScopeOwnerId = profile.OwnerId;
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

    /// <summary>
    /// A queryable projection of <c>Profile.OwnerId</c>. The profile is persisted as a single JSON
    /// column, so the owner inside it cannot be used in a SQL predicate; the AccessControl record
    /// scope has to be pushed into the query rather than filtered in memory, and that needs a real
    /// column. It is derived state kept in step with the profile, never an independent fact.
    /// </summary>
    public string ScopeOwnerId { get; private set; } = null!;
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
        ScopeOwnerId = profile.OwnerId;
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
        ScopeOwnerId = ownerId;
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

internal enum DealStageCategory { Open, Won, Lost }
internal enum DealForecastCategory { Commit, BestCase, Pipeline }
internal enum DealRecycleDecision { Recycle, Conditional, DoNotRecycle }
internal enum DealTransitionResult { Succeeded, InvalidTransition, LifecycleConflict }
