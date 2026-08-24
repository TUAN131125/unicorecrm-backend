namespace UnicoreCRM.Platform.AccessControl.Domain;

internal sealed class AuthorizationDecisionRecord
{
    private AuthorizationDecisionRecord() { }

    internal AuthorizationDecisionRecord(
        string workspaceId,
        string membershipId,
        string requiredCapability,
        bool allowed,
        string correlationId,
        DateTimeOffset evaluatedAt)
    {
        DecisionId = AccessControlIds.New("decision");
        WorkspaceId = workspaceId;
        MembershipId = membershipId;
        RequiredCapability = requiredCapability;
        Allowed = allowed;
        CorrelationId = correlationId;
        EvaluatedAt = evaluatedAt;
    }

    public string DecisionId { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public string MembershipId { get; private set; } = null!;
    public string RequiredCapability { get; private set; } = null!;
    public bool Allowed { get; private set; }
    public string CorrelationId { get; private set; } = null!;
    public DateTimeOffset EvaluatedAt { get; private set; }
}
