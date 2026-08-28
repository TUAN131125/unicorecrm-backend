using UnicoreCRM.Crm.Customers.Application.Common;
using UnicoreCRM.Crm.Customers.Contracts;
using UnicoreCRM.Crm.Customers.Domain;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Crm.Customers.Application.ListCustomers;

internal sealed record Query(CustomerRequestMetadata Metadata);

internal sealed class Handler(
    CustomerAuthorization authorization,
    ICustomersPersistence persistence,
    TimeProvider timeProvider)
{
    internal async Task<CustomerOperationResult<IReadOnlyList<CustomerDocument>>> HandleAsync(
        Query query,
        CancellationToken cancellationToken)
    {
        var access = await authorization.AuthorizeAsync(query.Metadata, cancellationToken);
        if (!access.IsSuccess)
            return CustomerOperationResult<IReadOnlyList<CustomerDocument>>.Failure(access.Error!);

        IReadOnlyList<Customer> customers = access.Value!.Authorization.ScopeFilter switch
        {
            RecordAccessScopeFilter.Workspace => await persistence.ReadCustomersAsync(
                access.Value.Trusted.WorkspaceId,
                cancellationToken),
            // Customer OWN has no canonical owner fact; TEAM and CUSTOM are unresolved. All
            // non-WORKSPACE scopes fail closed before any Customer rows are loaded.
            _ => []
        };

        persistence.AddReadAudit(new CustomerReadAuditRecord(
            "listCustomers",
            access.Value.Trusted.WorkspaceId,
            access.Value.Trusted.MemberId,
            null,
            query.Metadata.RequestId,
            query.Metadata.CorrelationId,
            null,
            timeProvider.GetUtcNow()));
        await persistence.SaveChangesAsync(cancellationToken);
        return CustomerOperationResult<IReadOnlyList<CustomerDocument>>.Success(
            customers
                .Select(customer => CustomerFieldSecurity.Project(
                    CustomerProjection.Document(customer),
                    access.Value.Authorization))
                .ToArray());
    }
}
