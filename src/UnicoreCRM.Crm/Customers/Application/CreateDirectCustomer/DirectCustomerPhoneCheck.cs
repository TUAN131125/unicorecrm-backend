using UnicoreCRM.BuildingBlocks;
using UnicoreCRM.Crm.Customers.Application.Common;
using UnicoreCRM.Crm.Customers.Contracts;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Crm.Customers.Application.CreateDirectCustomer;

internal sealed class DirectCustomerPhoneCheck(CustomerAuthorization authorization, ICustomersPersistence persistence,
    IEnumerable<IDirectCustomerSourceParticipant> sources)
{
    internal async Task<CustomerOperationResult<CustomerPhoneCheckResponse>> HandleAsync(CheckCustomerPhoneRequest request, CustomerRequestMetadata metadata, CancellationToken ct)
    {
        var grant = await authorization.AuthorizeAsync(metadata, CustomerCapabilities.CreateDirect, ct);
        if (!grant.IsSuccess) return CustomerOperationResult<CustomerPhoneCheckResponse>.Failure(grant.Error!);
        if (string.IsNullOrWhiteSpace(request.Phone)) return CustomerOperationResult<CustomerPhoneCheckResponse>.Success(new([]));
        var normalized = CustomerPhoneComparison.Normalize(request.Phone);
        if (normalized is null) return CustomerOperationResult<CustomerPhoneCheckResponse>.Failure(CustomerErrors.Validation(new Dictionary<string, string[]> { ["phone"] = ["Enter a valid full phone number."] }));
        await using var transaction = await persistence.BeginSerializableAsync(ct);
        try { return CustomerOperationResult<CustomerPhoneCheckResponse>.Success(new(await ReadAsync(transaction.OwnerTransaction, normalized, metadata, ct))); }
        catch (BusinessCommandException failure) { return CustomerOperationResult<CustomerPhoneCheckResponse>.Failure(new(failure.Code, failure.Status, "Phone review unavailable")); }
    }

    internal async Task<IReadOnlyList<CustomerPhoneCandidate>> ReadAsync(OwnerTransaction transaction, string normalized,
        CustomerRequestMetadata metadata, CancellationToken ct)
    {
        var matches = new List<CustomerPhoneCandidate>();
        foreach (var source in sources) matches.AddRange(await source.FindVisiblePhoneMatchesAsync(transaction, normalized, metadata.RequestId, metadata.CorrelationId, ct));
        var grant = await authorization.AuthorizeAsync(metadata, CustomerCapabilities.View, ct);
        if (!grant.IsSuccess || !grant.Value!.Authorization.CanRead("relationshipRef") || !grant.Value.Authorization.CanRead("status")) return matches;
        var access = grant.Value;
        if (access.Authorization.ScopeFilter == RecordAccessScopeFilter.Denied) return matches;
        var owner = access.Authorization.ScopeFilter == RecordAccessScopeFilter.OwnedByMember ? access.Authorization.ScopeOwnerMemberId : null;
        if (access.Authorization.ScopeFilter == RecordAccessScopeFilter.OwnedByMember && owner is null) return matches;
        var linked = await persistence.ReadBySubjectsAsync(access.Trusted.WorkspaceId, owner, matches.Select(m => m.RelationshipRef).ToArray(), ct);
        for (var index = 0; index < matches.Count; index++)
        {
            var match = matches[index]; var customer = linked.SingleOrDefault(c => c.RelationshipType == match.RelationshipRef.Type && c.RelationshipId == match.RelationshipRef.Id);
            if (customer is not null && await authorization.EnforceRecordAsync(access, customer, "checkDirectCustomerPhone", metadata, ct) is null)
                matches[index] = match with { CustomerId = customer.CustomerId, CustomerStatus = customer.Status };
        }
        return matches;
    }
}
