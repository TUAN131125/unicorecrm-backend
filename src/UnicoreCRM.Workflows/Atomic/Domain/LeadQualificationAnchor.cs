namespace UnicoreCRM.Workflows.Atomic.Domain;

internal static class WorkflowIds
{
    internal static string New(string prefix) => $"{prefix}_{Guid.NewGuid():N}";
}

/// <summary>
/// How far this qualification has durably progressed. The stages are forward-only and each one is
/// entered only after the participant commit it names has actually committed, so a coordinator that
/// stops at any point leaves a stage that is true rather than optimistic.
/// </summary>
internal enum LeadQualificationStage
{
    Started,
    ContactResolved,
    TaskCreated,
    Completed,
    DealCreated
}

/// <summary>
/// The Workflows-owned durable anchor for one typed Lead qualification.
///
/// It is the coordinator's convergence record, and it is deliberately **not** the Contacts
/// participant's replay state: Contacts owns its own conversion record for its own aggregate, and
/// neither substitutes for the other. This anchor exists so that a coordinator interrupted after any
/// successful participant commit resumes forward rather than repeating a foreign owner's mutation or
/// abandoning it.
///
/// There is no compensation. Committed participant state is never deleted; recovery only ever moves
/// the anchor forward.
/// </summary>
internal sealed class LeadQualificationAnchor
{
    private LeadQualificationAnchor() { }

    internal LeadQualificationAnchor(
        string scopeKey,
        string workspaceId,
        string workflow,
        string leadId,
        string idempotencyKey,
        string fingerprint,
        long expectedLeadVersion,
        string participantMemberId,
        string taskAssigneeId,
        string correlationId,
        DateTimeOffset now)
    {
        ScopeKey = scopeKey;
        WorkspaceId = workspaceId;
        Workflow = workflow;
        LeadId = leadId;
        IdempotencyKey = idempotencyKey;
        Fingerprint = fingerprint;
        ExpectedLeadVersion = expectedLeadVersion;
        IntentVersion = 1;
        ParticipantMemberId = participantMemberId;
        TaskAssigneeId = taskAssigneeId;
        CorrelationId = correlationId;
        Stage = LeadQualificationStage.Started;
        CreatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// The frozen workflow identity: a hash over the trusted WorkspaceId, the workflow operation,
    /// the leadId and the caller's Idempotency-Key.
    /// </summary>
    internal string ScopeKey { get; private set; } = null!;

    internal string WorkspaceId { get; private set; } = null!;
    internal string Workflow { get; private set; } = null!;
    internal string LeadId { get; private set; } = null!;
    internal string IdempotencyKey { get; private set; } = null!;

    /// <summary>
    /// The effective intent. Replaying this key with a different intent is an idempotency conflict,
    /// never a second execution.
    /// </summary>
    internal string Fingerprint { get; private set; } = null!;

    internal long ExpectedLeadVersion { get; private set; }
    internal int IntentVersion { get; private set; }
    // Execution provenance, never part of caller intent. Retained for stable participant replay.
    internal string? ParticipantMemberId { get; private set; }
    internal string? TaskAssigneeId { get; private set; }
    internal string? CorrelationId { get; private set; }
    internal LeadQualificationStage Stage { get; private set; }
    internal string? ContactId { get; private set; }
    internal long? ContactVersion { get; private set; }
    internal bool? ContactWasCreated { get; private set; }
    internal string? ContactDisplayName { get; private set; }
    internal string? TaskId { get; private set; }
    internal long? TaskVersion { get; private set; }
    internal string? DealId { get; private set; }
    internal long? DealVersion { get; private set; }
    internal long? LeadVersion { get; private set; }
    internal string? ResponseJson { get; private set; }
    internal DateTimeOffset CreatedAt { get; private set; }
    internal DateTimeOffset UpdatedAt { get; private set; }
    internal byte[] RowVersion { get; private set; } = [];

    internal void RecordContact(string contactId, long version, bool wasCreated, string displayName, DateTimeOffset now)
    {
        ContactId = contactId;
        ContactVersion = version;
        ContactWasCreated = wasCreated;
        ContactDisplayName = displayName;
        Advance(LeadQualificationStage.ContactResolved, now);
    }

    internal void RecordTask(string taskId, long version, DateTimeOffset now)
    {
        TaskId = taskId;
        TaskVersion = version;
        Advance(LeadQualificationStage.TaskCreated, now);
    }

    internal void RecordDeal(string dealId, long version, DateTimeOffset now)
    {
        DealId = dealId;
        DealVersion = version;
        Advance(LeadQualificationStage.DealCreated, now);
    }

    /// <summary>
    /// Stores the authoritative workflow response alongside completion, so a replay returns exactly
    /// what the original execution returned rather than a response recomposed from partial state.
    /// </summary>
    internal void Complete(long leadVersion, string responseJson, DateTimeOffset now)
    {
        LeadVersion = leadVersion;
        ResponseJson = responseJson;
        Advance(LeadQualificationStage.Completed, now);
    }

    /// <summary>Forward-only. A resumed attempt can never move the anchor backwards.</summary>
    private void Advance(LeadQualificationStage stage, DateTimeOffset now)
    {
        if (Stage == LeadQualificationStage.Completed || stage == Stage)
            return;
        if (stage != LeadQualificationStage.Completed && stage < Stage)
            return;
        Stage = stage;
        UpdatedAt = now;
    }
}
