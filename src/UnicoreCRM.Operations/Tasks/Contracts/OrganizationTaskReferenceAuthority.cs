namespace UnicoreCRM.Operations.Tasks.Contracts;

/// <summary>Organizations owns current target read/scope and lifecycle eligibility.
/// No Organization content is returned. Archived targets are unavailable, including replay.</summary>
public interface IOrganizationTaskReferenceAuthority
{
    Task<bool> CanReferenceAsync(string workspaceId, string organizationId,
        string requestId, string correlationId, CancellationToken cancellationToken);
}
