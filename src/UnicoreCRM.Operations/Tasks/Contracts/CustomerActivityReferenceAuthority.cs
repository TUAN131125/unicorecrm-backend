namespace UnicoreCRM.Operations.Tasks.Contracts;

/// <summary>Customers owns current target read/scope and lifecycle eligibility for Activity logging.
/// Returns no Customer content. Archived targets deny new logs and replay.</summary>
public interface ICustomerActivityReferenceAuthority
{
    Task<bool> CanReferenceAsync(string workspaceId, string customerId,
        string requestId, string correlationId, CancellationToken cancellationToken);
}
