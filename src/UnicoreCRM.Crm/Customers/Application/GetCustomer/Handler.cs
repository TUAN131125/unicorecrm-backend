using System.Text.RegularExpressions;
using UnicoreCRM.Crm.Customers.Application.Common;
using UnicoreCRM.Crm.Customers.Contracts;
using UnicoreCRM.Crm.Customers.Domain;

namespace UnicoreCRM.Crm.Customers.Application.GetCustomer;

internal sealed record Query(string CustomerId, CustomerRequestMetadata Metadata);

internal sealed partial class Handler(
    CustomerAuthorization authorization,
    ICustomersPersistence persistence,
    TimeProvider timeProvider)
{
    internal async Task<CustomerOperationResult<CustomerDocument>> HandleAsync(
        Query query,
        CancellationToken cancellationToken)
    {
        var access = await authorization.AuthorizeAsync(query.Metadata, cancellationToken);
        if (!access.IsSuccess)
            return CustomerOperationResult<CustomerDocument>.Failure(access.Error!);
        if (!EntityIdPattern().IsMatch(query.CustomerId))
            return CustomerOperationResult<CustomerDocument>.Failure(CustomerErrors.NotFound());

        var customer = await persistence.ReadCustomerAsync(
            access.Value!.Trusted.WorkspaceId,
            query.CustomerId,
            cancellationToken);
        if (customer is null)
            return CustomerOperationResult<CustomerDocument>.Failure(CustomerErrors.NotFound());

        var denied = await authorization.EnforceRecordAsync(
            access.Value,
            customer,
            "getCustomer",
            query.Metadata,
            cancellationToken);
        if (denied is not null)
            return CustomerOperationResult<CustomerDocument>.Failure(denied);

        persistence.AddReadAudit(new CustomerReadAuditRecord(
            "getCustomer",
            access.Value.Trusted.WorkspaceId,
            access.Value.Trusted.MemberId,
            customer.CustomerId,
            query.Metadata.RequestId,
            query.Metadata.CorrelationId,
            customer.Version,
            timeProvider.GetUtcNow()));
        await persistence.SaveChangesAsync(cancellationToken);
        return CustomerOperationResult<CustomerDocument>.Success(
            CustomerFieldSecurity.Project(
                CustomerProjection.Document(customer),
                access.Value.Authorization));
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityIdPattern();
}
