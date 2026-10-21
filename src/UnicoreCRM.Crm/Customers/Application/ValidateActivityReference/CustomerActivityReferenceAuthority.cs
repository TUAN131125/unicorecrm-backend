using System.Text.RegularExpressions;
using UnicoreCRM.Crm.Customers.Application.Common;
using UnicoreCRM.Operations.Tasks.Contracts;

namespace UnicoreCRM.Crm.Customers.Application.ValidateActivityReference;

internal sealed class CustomerActivityReferenceAuthority(CustomerAuthorization authorization,
    ICustomersPersistence persistence) : ICustomerActivityReferenceAuthority
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
            "validateActivityCustomerReference", metadata, cancellationToken) is not null)
            return false;
        // Activity-specific Owner decision; validation-point check, not a cross-owner lock.
        return customer.Status is "NEW" or "ACTIVE" or "INACTIVE";
    }
}
