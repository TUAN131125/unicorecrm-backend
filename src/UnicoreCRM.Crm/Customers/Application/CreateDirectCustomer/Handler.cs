using UnicoreCRM.BuildingBlocks;
using UnicoreCRM.Crm.Customers.Application.Common;
using UnicoreCRM.Crm.Customers.Contracts;
using UnicoreCRM.Crm.Customers.Domain;

namespace UnicoreCRM.Crm.Customers.Application.CreateDirectCustomer;

internal sealed record Command(CreateDirectCustomerRequest Request, CustomerCommandMetadata Metadata);
internal sealed class Handler(CustomerAuthorization authorization, ICustomersPersistence persistence,
    IEnumerable<IDirectCustomerSourceParticipant> sources, DirectCustomerPhoneCheck phones, TimeProvider clock)
{
    internal async Task<CustomerOperationResult<CustomerMutationResponse>> HandleAsync(Command command, CancellationToken ct)
    {
        var metadata = new CustomerRequestMetadata(command.Metadata.RequestId, command.Metadata.CorrelationId);
        var grant = await authorization.AuthorizeAsync(metadata, CustomerCapabilities.CreateDirect, ct);
        if (!grant.IsSuccess) return CustomerOperationResult<CustomerMutationResponse>.Failure(grant.Error!);
        var request = command.Request;
        var errors = CustomerMutationSupport.ValidateProfile(request.Segment, request.Tags, request.Tier, request.ServiceLevel);
        if (request.Type is not ("B2C" or "B2B")) errors["type"] = ["Choose B2C or B2B."];
        if (string.IsNullOrWhiteSpace(request.Name)) errors["name"] = ["A name is required."];
        var normalized = CustomerPhoneComparison.Normalize(request.Phone);
        if (!string.IsNullOrWhiteSpace(request.Phone) && normalized is null) errors["phone"] = ["Enter a valid full phone number."];
        if (request.Type == "B2C" && (!string.IsNullOrWhiteSpace(request.LegalName) || !string.IsNullOrWhiteSpace(request.TaxCode)))
            errors["type"] = ["Legal name and tax code belong to a business Customer."];
        if (request.ReviewedSources?.Count > 200 || request.ReviewedSources?.Any(s => s is null || s.Type is not ("CONTACT" or "ORGANIZATION_ACCOUNT") || string.IsNullOrWhiteSpace(s.Id)) == true)
            errors["reviewedSources"] = ["Reviewed sources must be valid relationship references."];
        if (errors.Count > 0) return CustomerOperationResult<CustomerMutationResponse>.Failure(CustomerErrors.Validation(errors));
        var access = grant.Value!; var trusted = access.Trusted;
        var writeDenied = CustomerFieldSecurity.GuardWrite(access.Authorization,
            new[] { "relationshipRef", "ownerId" }.Concat(new[] { request.Segment is null ? null : "segment", request.Tags is null ? null : "tags",
                request.Tier is null ? null : "tier", request.ServiceLevel is null ? null : "serviceLevel" }.OfType<string>()).ToArray());
        if (writeDenied is not null) return CustomerOperationResult<CustomerMutationResponse>.Failure(writeDenied);
        var fingerprint = CustomerMutationSupport.Fingerprint(request);
        var scope = CustomerMutationSupport.ScopeKey(trusted, "createDirectCustomer", "WORKSPACE", command.Metadata.IdempotencyKey);
        try
        {
            await using var transaction = await persistence.BeginSerializableAsync(ct);
            await transaction.LockDirectCreationAsync(trusted.WorkspaceId, ct);
            var prior = await persistence.FindIdempotencyAsync(scope, ct);
            if (prior is not null)
            {
                if (prior.Fingerprint != fingerprint) return CustomerOperationResult<CustomerMutationResponse>.Failure(CustomerErrors.IdempotencyReused());
                var response = CustomerMutationSupport.Replay(prior, access.Authorization);
                var current = await persistence.ReadCustomerAsync(trusted.WorkspaceId, response.AggregateId, ct);
                if (current is null) return CustomerOperationResult<CustomerMutationResponse>.Failure(CustomerErrors.NotFound());
                var denied = await authorization.EnforceRecordAsync(access, current, "createDirectCustomerReplay", metadata, ct);
                return denied is null ? CustomerOperationResult<CustomerMutationResponse>.Success(response)
                    : CustomerOperationResult<CustomerMutationResponse>.Failure(denied);
            }
            var matches = normalized is null ? [] : await phones.ReadAsync(transaction.OwnerTransaction, normalized, metadata, ct);
            var reviewed = new HashSet<string>((request.ReviewedSources ?? []).Select(s => s.Type + ":" + s.Id), StringComparer.Ordinal);
            if (matches.Any(m => !request.DifferentCustomerConfirmed || !reviewed.Contains(m.RelationshipRef.Type + ":" + m.RelationshipRef.Id)))
                return CustomerOperationResult<CustomerMutationResponse>.Failure(new("CUSTOMER_PHONE_REVIEW_REQUIRED", 409,
                    "Phone review required", FieldErrors: new Dictionary<string, string[]> { ["phone"] = ["This phone number is already in use. Review accessible records before creating another Customer."] }));
            var buyerType = request.Type == "B2C" ? "CONTACT" : "ORGANIZATION_ACCOUNT";
            var source = sources.Single(s => s.BuyerType == buyerType);
            var childKey = CustomerMutationSupport.Fingerprint(new { Parent = scope, Participant = buyerType });
            var sourceId = await source.CreateAsync(transaction.OwnerTransaction, request, childKey, metadata.RequestId, metadata.CorrelationId, ct);
            var now = clock.GetUtcNow();
            var profile = CustomerMutationSupport.Profile(new CreateCustomerRequest(new(buyerType, sourceId))
            { Segment = request.Segment, Tags = request.Tags, Tier = request.Tier, ServiceLevel = request.ServiceLevel }) with
            { SourceSystem = "DIRECT_MANUAL", ConversionCorrelationId = metadata.CorrelationId };
            var customer = new Customer(trusted.WorkspaceId, trusted.MemberId, buyerType, sourceId, profile, now);
            persistence.AddCustomer(customer);
            var result = CustomerMutationSupport.Commit(persistence, customer, access, command.Metadata,
                "createDirectCustomer", ["CUSTOMER_CREATED"], "WORKSPACE", fingerprint, now);
            await persistence.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return CustomerOperationResult<CustomerMutationResponse>.Success(result);
        }
        catch (CustomerSourceValidationException failure) { return CustomerOperationResult<CustomerMutationResponse>.Failure(CustomerErrors.Validation(failure.Fields)); }
        catch (BusinessCommandException failure) { return CustomerOperationResult<CustomerMutationResponse>.Failure(new(failure.Code, failure.Status, "Customer source operation blocked")); }
        catch (CustomersPersistenceConcurrencyException) { return CustomerOperationResult<CustomerMutationResponse>.Failure(new("CUSTOMER_CREATE_RETRY_REQUIRED", 503, "Retry the same Customer creation request")); }
        catch (CustomerBusinessKeyConflictException) { return CustomerOperationResult<CustomerMutationResponse>.Failure(new("CUSTOMER_CREATE_RETRY_REQUIRED", 503, "Retry the same Customer creation request")); }
    }
}
