using UnicoreCRM.Crm.Customers.Application.Common;
using UnicoreCRM.Crm.Customers.Contracts;

namespace UnicoreCRM.Crm.Customers.Application.ArchiveCustomer;

internal sealed record Command(string CustomerId, CustomerCommandMetadata Metadata);
internal sealed class Handler(CustomerAuthorization authorization, ICustomersPersistence persistence, TimeProvider timeProvider)
{
    internal async Task<CustomerOperationResult<CustomerMutationResponse>> HandleAsync(Command command, CancellationToken cancellationToken)
    {
        var metadata = new CustomerRequestMetadata(command.Metadata.RequestId, command.Metadata.CorrelationId);
        var access = await authorization.AuthorizeAsync(metadata, CustomerCapabilities.Archive, cancellationToken);
        if (!access.IsSuccess) return CustomerOperationResult<CustomerMutationResponse>.Failure(access.Error!);
        var writeError = CustomerFieldSecurity.GuardWrite(access.Value!.Authorization, "status");
        if (writeError is not null) return CustomerOperationResult<CustomerMutationResponse>.Failure(writeError);
        var trusted = access.Value!.Trusted;
        var fingerprint = CustomerMutationSupport.Fingerprint(new { command.CustomerId, command.Metadata.ExpectedVersion });
        var scope = CustomerMutationSupport.ScopeKey(trusted, "archiveCustomer", command.CustomerId, command.Metadata.IdempotencyKey);
        var visibleCustomer = await persistence.LoadCustomerAsync(trusted.WorkspaceId, command.CustomerId, cancellationToken);
        if (visibleCustomer is null) return CustomerOperationResult<CustomerMutationResponse>.Failure(CustomerErrors.NotFound());
        var visibleDenied = await authorization.EnforceRecordAsync(access.Value, visibleCustomer, "archiveCustomer", metadata, cancellationToken);
        if (visibleDenied is not null) return CustomerOperationResult<CustomerMutationResponse>.Failure(visibleDenied);
        var prior = await persistence.FindIdempotencyAsync(scope, cancellationToken);
        if (prior is not null) return prior.Fingerprint == fingerprint
            ? CustomerOperationResult<CustomerMutationResponse>.Success(CustomerMutationSupport.Replay(prior, access.Value.Authorization))
            : CustomerOperationResult<CustomerMutationResponse>.Failure(CustomerErrors.IdempotencyReused());
        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);
        prior = await persistence.FindIdempotencyAsync(scope, cancellationToken);
        if (prior is not null) return prior.Fingerprint == fingerprint
            ? CustomerOperationResult<CustomerMutationResponse>.Success(CustomerMutationSupport.Replay(prior, access.Value.Authorization))
            : CustomerOperationResult<CustomerMutationResponse>.Failure(CustomerErrors.IdempotencyReused());
        var customer = await persistence.LoadCustomerAsync(trusted.WorkspaceId, command.CustomerId, cancellationToken);
        if (customer is null) return CustomerOperationResult<CustomerMutationResponse>.Failure(CustomerErrors.NotFound());
        var denied = await authorization.EnforceRecordAsync(access.Value, customer, "archiveCustomer", metadata, cancellationToken);
        if (denied is not null) return CustomerOperationResult<CustomerMutationResponse>.Failure(denied);
        if (customer.Status == "ARCHIVED") return CustomerOperationResult<CustomerMutationResponse>.Failure(CustomerErrors.LifecycleConflict());
        if (customer.Version != command.Metadata.ExpectedVersion)
            return CustomerOperationResult<CustomerMutationResponse>.Failure(CustomerErrors.VersionConflict(customer.CustomerId, command.Metadata.ExpectedVersion!.Value, customer.Version));
        var now = timeProvider.GetUtcNow(); customer.Archive(now);
        var response = CustomerMutationSupport.Commit(persistence, customer, access.Value, command.Metadata,
            "archiveCustomer", ["CUSTOMER_ARCHIVED"], command.CustomerId, fingerprint, now);
        try { await persistence.SaveChangesAsync(cancellationToken); }
        catch (CustomersPersistenceConcurrencyException) { return CustomerOperationResult<CustomerMutationResponse>.Failure(CustomerErrors.VersionConflict(customer.CustomerId, command.Metadata.ExpectedVersion!.Value, customer.Version)); }
        await transaction.CommitAsync(cancellationToken);
        return CustomerOperationResult<CustomerMutationResponse>.Success(response);
    }
}
