namespace UnicoreCRM.Operations.Support.Domain;

/// <summary>
/// The SupportCase aggregate. Support owns the case identity, human-readable case number,
/// profile, lifecycle, assignment, resolution timestamps and resource version. It owns no
/// Task state, no CRM state and no commercial evidence.
/// </summary>
internal sealed class SupportCase
{
    private SupportCase() { }

    internal SupportCase(
        string workspaceId,
        int caseYear,
        int caseSequence,
        string caseNumber,
        SupportCaseProfile profile,
        DateTimeOffset now)
    {
        CaseId = SupportIds.New("case");
        WorkspaceId = workspaceId;
        CaseYear = caseYear;
        CaseSequence = caseSequence;
        CaseNumber = caseNumber;
        Status = SupportCaseLifecycle.Initial;
        ApplyProfile(profile);
        CreatedAt = now;
        UpdatedAt = now;
    }

    public string CaseId { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public string CaseNumber { get; private set; } = null!;

    /// <summary>Support-owned case-number allocation state. Never projected onto the wire.</summary>
    public int CaseYear { get; private set; }

    /// <summary>Support-owned case-number allocation state. Never projected onto the wire.</summary>
    public int CaseSequence { get; private set; }

    public string Title { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public SupportCaseStatus Status { get; private set; }
    public SupportCasePriority Priority { get; private set; }
    public SupportCaseCategory Category { get; private set; }
    public SupportCaseSource Source { get; private set; }
    public SupportCaseChannel? Channel { get; private set; }
    public string RelationshipType { get; private set; } = null!;
    public string RelationshipId { get; private set; } = null!;
    public string? ContactId { get; private set; }
    public string? RelatedOrderId { get; private set; }
    public string? RelatedProductId { get; private set; }
    public string? RelatedOwnedProductId { get; private set; }
    public string? OwnerId { get; private set; }
    public IReadOnlyList<string> Tags { get; private set; } = [];
    public DateTimeOffset? NextFollowUpAt { get; private set; }
    public DateTimeOffset? FirstResponseDueAt { get; private set; }
    public DateTimeOffset? ResolutionDueAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public DateTimeOffset? ReopenedAt { get; private set; }
    public string? ResolutionSummary { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public long Version { get; private set; }

    /// <summary>
    /// Full profile replacement. The admitted PUT contract carries the whole profile, so an
    /// omitted optional field clears the stored value. Status, assignment history, resolution
    /// timestamps and the case number are not part of the profile and are untouched.
    /// </summary>
    internal void ReplaceProfile(SupportCaseProfile profile, DateTimeOffset now)
    {
        ApplyProfile(profile);
        Touch(now);
    }

    /// <summary>
    /// Assignment records the Support-owned owner reference. No admitted authority makes
    /// assignment change the lifecycle, so the status is deliberately left alone.
    /// </summary>
    internal void Assign(string ownerId, DateTimeOffset now)
    {
        OwnerId = ownerId;
        Touch(now);
    }

    /// <summary>
    /// Applies an admitted lifecycle transition. Returns false for any pair the frozen
    /// transition table does not admit. Resolve and close stamp their timestamps; reopen
    /// stamps <c>reopenedAt</c> and clears the resolved/closed stamps, exactly as the
    /// canonical baseline states.
    /// </summary>
    internal bool Transition(SupportCaseStatus nextStatus, string? resolutionSummary, DateTimeOffset now)
    {
        if (!SupportCaseLifecycle.CanTransition(Status, nextStatus))
            return false;

        if (resolutionSummary is not null)
            ResolutionSummary = resolutionSummary;

        if (Status != nextStatus)
        {
            Status = nextStatus;
            switch (nextStatus)
            {
                case SupportCaseStatus.Resolved:
                    ResolvedAt ??= now;
                    break;
                case SupportCaseStatus.Closed:
                    ClosedAt ??= now;
                    break;
                case SupportCaseStatus.Reopened:
                    ReopenedAt = now;
                    ResolvedAt = null;
                    ClosedAt = null;
                    break;
            }
        }

        Touch(now);
        return true;
    }

    /// <summary>
    /// Appending conversation evidence advances the case resource version so the admitted
    /// If-Match contract stays meaningful for the next command.
    /// </summary>
    internal void RecordComment(DateTimeOffset now) => Touch(now);

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }

    private void ApplyProfile(SupportCaseProfile profile)
    {
        Title = profile.Title;
        Description = profile.Description;
        Priority = profile.Priority;
        Category = profile.Category;
        Source = profile.Source;
        Channel = profile.Channel;
        RelationshipType = profile.RelationshipRef.Type;
        RelationshipId = profile.RelationshipRef.Id;
        ContactId = profile.ContactId;
        RelatedOrderId = profile.RelatedOrderId;
        RelatedProductId = profile.RelatedProductId;
        RelatedOwnedProductId = profile.RelatedOwnedProductId;
        OwnerId = profile.OwnerId;
        NextFollowUpAt = profile.NextFollowUpAt;
        FirstResponseDueAt = profile.FirstResponseDueAt;
        ResolutionDueAt = profile.ResolutionDueAt;
        Tags = profile.Tags;
    }
}
