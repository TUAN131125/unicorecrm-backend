namespace UnicoreCRM.Operations.Tasks.Contracts;

public enum DealTaskReferenceStatus { Available, AccessDenied, NotFound, Ineligible }

/// <summary>
/// Tasks consumes this reference-validation port; Deals implements its own access and lifecycle
/// decision. No labels or foreign persistence are exposed. WorkspaceId comes from Tasks' trusted
/// authorization and must also match the implementer's trusted context.
/// </summary>
public interface IDealTaskReferenceAuthority
{
    Task<DealTaskReferenceStatus> ValidateAsync(string workspaceId, string dealId, bool allowArchived,
        string requestId, string correlationId, CancellationToken cancellationToken);
}
