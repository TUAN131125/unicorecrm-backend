namespace UnicoreCRM.Platform.AccessControl.Domain;

internal sealed class WorkspaceServiceCapabilityGrant
{
    private WorkspaceServiceCapabilityGrant() { }
    internal WorkspaceServiceCapabilityGrant(string workspaceId, string servicePrincipalId, string capability, DateTimeOffset grantedAt) =>
        (WorkspaceId, ServicePrincipalId, Capability, GrantedAt) = (workspaceId, servicePrincipalId, capability, grantedAt);
    internal string WorkspaceId { get; private set; } = null!;
    internal string ServicePrincipalId { get; private set; } = null!;
    internal string Capability { get; private set; } = null!;
    internal DateTimeOffset GrantedAt { get; private set; }
}

internal sealed class ServiceAuthorizationDecisionRecord
{
    private ServiceAuthorizationDecisionRecord() { }
    internal ServiceAuthorizationDecisionRecord(string workspaceId, string servicePrincipalId, string capability,
        bool allowed, string correlationId, DateTimeOffset evaluatedAt)
    {
        DecisionId = AccessControlIds.New("service_decision"); WorkspaceId = workspaceId;
        ServicePrincipalId = servicePrincipalId; RequiredCapability = capability; Allowed = allowed;
        CorrelationId = correlationId; EvaluatedAt = evaluatedAt;
    }
    internal string DecisionId { get; private set; } = null!;
    internal string WorkspaceId { get; private set; } = null!;
    internal string ServicePrincipalId { get; private set; } = null!;
    internal string RequiredCapability { get; private set; } = null!;
    internal bool Allowed { get; private set; }
    internal string CorrelationId { get; private set; } = null!;
    internal DateTimeOffset EvaluatedAt { get; private set; }
}
