namespace UnicoreCRM.Crm.Customers.Contracts;

/// <summary>Owner-approved bounded system authority: confirmed accepted Order only,
/// exact Workspace/buyer identity, active source owner, identifiers/provenance only.
/// No user impersonation, profile overwrite or permission grant. Not exposed by HTTP.</summary>
public interface IAcceptedQuoteCustomerParticipant
{
    Task<AcceptedQuoteCustomerResult> ResolveAsync(string workspaceId,string orderId,CancellationToken cancellationToken);
}
public sealed record AcceptedQuoteCustomerResult(bool Succeeded,string? CustomerId,string? Resolution,string? ErrorCode);

// Narrow owner checks for this system workflow. They expose no masked/hidden PII.
public interface IAcceptedQuoteContactBuyer
{ Task<bool> ExistsActiveAsync(string workspaceId,string contactId,CancellationToken cancellationToken); }
public interface IAcceptedQuoteOrganizationBuyer
{ Task<bool> ExistsActiveAsync(string workspaceId,string organizationId,CancellationToken cancellationToken); }
