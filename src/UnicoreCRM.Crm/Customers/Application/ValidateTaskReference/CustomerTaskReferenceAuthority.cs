using System.Text.RegularExpressions;
using UnicoreCRM.Crm.Customers.Application.Common;
using UnicoreCRM.Operations.Tasks.Contracts;

namespace UnicoreCRM.Crm.Customers.Application.ValidateTaskReference;

internal sealed class CustomerTaskReferenceAuthority(CustomerAuthorization authorization,
    ICustomersPersistence persistence) : ICustomerTaskReferenceAuthority
{
    public async Task<bool> CanReferenceAsync(string workspaceId, string customerId,
        string requestId, string correlationId, CancellationToken cancellationToken)
    {
        var metadata = new CustomerRequestMetadata(requestId, correlationId);
        var access = await authorization.AuthorizeAsync(metadata, cancellationToken);
        if (!access.IsSuccess || access.Value!.Trusted.WorkspaceId != workspaceId
            || !Regex.IsMatch(customerId, "^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant))
            return false;
        var customer = await persistence.ReadCustomerAsync(workspaceId, customerId, cancellationToken);
        if (customer is null || await authorization.EnforceRecordAsync(access.Value, customer,
            "validateTaskCustomerReference", metadata, cancellationToken) is not null)
            return false;
        // Owner-approved Task association eligibility; checked at validation time,
        // without a cross-owner lifecycle lock or a commit-time authority guarantee.
        return customer.Status is "NEW" or "ACTIVE" or "INACTIVE";
    }
}
