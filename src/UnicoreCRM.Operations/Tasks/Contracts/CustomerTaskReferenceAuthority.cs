namespace UnicoreCRM.Operations.Tasks.Contracts;

/// <summary>Customers owns current target read/scope and lifecycle eligibility.
/// Returns no Customer content. Archived targets deny new association and replay.</summary>
public interface ICustomerTaskReferenceAuthority
{
    Task<bool> CanReferenceAsync(string workspaceId, string customerId,
        string requestId, string correlationId, CancellationToken cancellationToken);
}
