using UnicoreCRM.Crm.Contacts.Contracts;
using UnicoreCRM.Crm.Customers.Application.Common;
using UnicoreCRM.Crm.Customers.Contracts;
using UnicoreCRM.Crm.Customers.Domain;
using UnicoreCRM.Crm.Organizations.Contracts;

namespace UnicoreCRM.Crm.Customers.Application.CreateCustomer;

internal sealed record Command(CreateCustomerRequest Request, CustomerCommandMetadata Metadata);
internal sealed class Handler(CustomerAuthorization authorization, ICustomersPersistence persistence,
    IContactCustomerSubjectParticipant contacts, IOrganizationCustomerSubjectParticipant organizations, TimeProvider timeProvider)
{
    internal async Task<CustomerOperationResult<CustomerMutationResponse>> HandleAsync(Command command, CancellationToken cancellationToken)
    {
        var metadata = new CustomerRequestMetadata(command.Metadata.RequestId, command.Metadata.CorrelationId);
        var access = await authorization.AuthorizeAsync(metadata, CustomerCapabilities.Create, cancellationToken);
        if (!access.IsSuccess) return CustomerOperationResult<CustomerMutationResponse>.Failure(access.Error!);
        var validation = CustomerMutationSupport.Validate(command.Request);
        if (validation is not null) return CustomerOperationResult<CustomerMutationResponse>.Failure(validation);
        var writeError = CustomerFieldSecurity.GuardWrite(access.Value!.Authorization,
            CustomerMutationSupport.WrittenFields(command.Request));
        if (writeError is not null) return CustomerOperationResult<CustomerMutationResponse>.Failure(writeError);
        var trusted = access.Value!.Trusted;
        var subject = command.Request.RelationshipRef!;
        var target = $"{subject.Type}:{subject.Id}";
        var fingerprint = CustomerMutationSupport.Fingerprint(command.Request);
        var scope = CustomerMutationSupport.ScopeKey(trusted, "createCustomer", target, command.Metadata.IdempotencyKey);
        var prior = await persistence.FindIdempotencyAsync(scope, cancellationToken);
        if (prior is not null)
        {
            if (prior.Fingerprint != fingerprint)
                return CustomerOperationResult<CustomerMutationResponse>.Failure(CustomerErrors.IdempotencyReused());
            var replay = CustomerMutationSupport.Replay(prior, access.Value.Authorization);
            var replayCustomer = await persistence.LoadCustomerAsync(trusted.WorkspaceId, replay.AggregateId, cancellationToken);
            if (replayCustomer is null) return CustomerOperationResult<CustomerMutationResponse>.Failure(CustomerErrors.NotFound());
            var replayDenied = await authorization.EnforceRecordAsync(access.Value, replayCustomer, "createCustomer", metadata, cancellationToken);
            return replayDenied is null
                ? CustomerOperationResult<CustomerMutationResponse>.Success(replay)
                : CustomerOperationResult<CustomerMutationResponse>.Failure(replayDenied);
        }

        var eligible = subject.Type == "CONTACT"
            ? (await contacts.ResolveVisibleAsync(trusted, subject.Id, command.Metadata.RequestId, command.Metadata.CorrelationId, cancellationToken))?.IsEligible == true
            : (await organizations.ResolveVisibleAsync(trusted, subject.Id, command.Metadata.RequestId, command.Metadata.CorrelationId, cancellationToken))?.IsEligible == true;
        if (!eligible) return CustomerOperationResult<CustomerMutationResponse>.Failure(CustomerErrors.NotFound());

        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);
        prior = await persistence.FindIdempotencyAsync(scope, cancellationToken);
        if (prior is not null)
        {
            if (prior.Fingerprint != fingerprint)
                return CustomerOperationResult<CustomerMutationResponse>.Failure(CustomerErrors.IdempotencyReused());
            var replay = CustomerMutationSupport.Replay(prior, access.Value.Authorization);
            var replayCustomer = await persistence.LoadCustomerAsync(trusted.WorkspaceId, replay.AggregateId, cancellationToken);
            if (replayCustomer is null) return CustomerOperationResult<CustomerMutationResponse>.Failure(CustomerErrors.NotFound());
            var replayDenied = await authorization.EnforceRecordAsync(access.Value, replayCustomer, "createCustomer", metadata, cancellationToken);
            return replayDenied is null
                ? CustomerOperationResult<CustomerMutationResponse>.Success(replay)
                : CustomerOperationResult<CustomerMutationResponse>.Failure(replayDenied);
        }
        if (await persistence.SubjectExistsAsync(trusted.WorkspaceId, subject.Type, subject.Id, cancellationToken))
            return CustomerOperationResult<CustomerMutationResponse>.Failure(CustomerErrors.DuplicateSubject());
        var now = timeProvider.GetUtcNow();
        var customer = new Customer(trusted.WorkspaceId, trusted.MemberId, subject.Type, subject.Id,
            CustomerMutationSupport.Profile(command.Request), now);
        persistence.AddCustomer(customer);
        var response = CustomerMutationSupport.Commit(persistence, customer, access.Value, command.Metadata,
            "createCustomer", ["CUSTOMER_CREATED"], target, fingerprint, now);
        try { await persistence.SaveChangesAsync(cancellationToken); }
        catch (CustomerBusinessKeyConflictException) { return CustomerOperationResult<CustomerMutationResponse>.Failure(CustomerErrors.DuplicateSubject()); }
        catch (CustomersPersistenceConcurrencyException) { return CustomerOperationResult<CustomerMutationResponse>.Failure(CustomerErrors.IdempotencyReused()); }
        await transaction.CommitAsync(cancellationToken);
        return CustomerOperationResult<CustomerMutationResponse>.Success(response);
    }
}
