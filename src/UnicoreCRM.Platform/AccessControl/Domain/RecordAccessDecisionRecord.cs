namespace UnicoreCRM.Platform.AccessControl.Domain;

/// <summary>
/// Immutable AccessControl-owned evidence of one record-access evaluation. It stores the trusted
/// Workspace, the evaluating membership and member, the resource and record the caller asked
/// about, the capability that gated the read, the evaluated scope and the derived owner-match
/// fact - enough to reproduce the decision without persisting any foreign business data. The
/// owner member identifier itself is deliberately not stored: it belongs to the business owner,
/// and the boolean match is what the decision actually turned on.
/// </summary>
internal sealed class RecordAccessDecisionRecord
{
    private RecordAccessDecisionRecord() { }

    internal RecordAccessDecisionRecord(
        string workspaceId,
        string membershipId,
        string memberId,
        string resourceKey,
        string? recordId,
        string requiredCapability,
        bool allowed,
        string evaluatedScope,
        string decisionCode,
        string requestId,
        string correlationId,
        bool? ownerMatch,
        DateTimeOffset evaluatedAt)
    {
        DecisionId = AccessControlIds.New("recdec");
        WorkspaceId = workspaceId;
        MembershipId = membershipId;
        MemberId = memberId;
        ResourceKey = resourceKey;
        RecordId = recordId;
        RequiredCapability = requiredCapability;
        Allowed = allowed;
        EvaluatedScope = evaluatedScope;
        DecisionCode = decisionCode;
        RequestId = requestId;
        CorrelationId = correlationId;
        OwnerMatch = ownerMatch;
        EvaluatedAt = evaluatedAt;
    }

    public string DecisionId { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public string MembershipId { get; private set; } = null!;
    public string MemberId { get; private set; } = null!;
    public string ResourceKey { get; private set; } = null!;
    public string? RecordId { get; private set; }
    public string RequiredCapability { get; private set; } = null!;
    public bool Allowed { get; private set; }
    public string EvaluatedScope { get; private set; } = null!;
    public string DecisionCode { get; private set; } = null!;
    public string RequestId { get; private set; } = null!;
    public string CorrelationId { get; private set; } = null!;
    public bool? OwnerMatch { get; private set; }
    public DateTimeOffset EvaluatedAt { get; private set; }
}
