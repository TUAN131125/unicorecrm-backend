using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Crm.Customers.Application.Common;
using UnicoreCRM.Crm.Customers.Contracts;
using UnicoreCRM.Crm.Customers.Infrastructure.Persistence;
using UnicoreCRM.Crm.Customers.Domain;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Crm.Customers.Application.Health;

internal sealed class CustomerAttentionReader(
    CustomersDbContext db,
    CustomerAuthorization authorization,
    TimeProvider clock) : ICustomerAttentionReader
{
    private static readonly IReadOnlyList<string> Fields = ["id", "customerCode", "health"];
    private static readonly RecordAccessRepresentation Representation =
        RecordAccessRepresentation.Create("customer.attention", "id", "customerCode", "health");

    public async Task<CustomerAttentionReadResult> ReadAuthorizedAsync(
        IReadOnlyCollection<string> customerIds,
        CustomerAttentionRequestContext requestContext,
        CancellationToken cancellationToken)
    {
        if (customerIds.Count == 0) return new(true, new Dictionary<string, CustomerAttentionProjection>());
        if (customerIds.Count > 100) throw new ArgumentOutOfRangeException(nameof(customerIds));
        var ids = customerIds.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 128)) throw new ArgumentException("Customer identity is invalid.", nameof(customerIds));

        var access = await authorization.AuthorizeAsync(
            new CustomerRequestMetadata(requestContext.RequestId, requestContext.CorrelationId),
            CustomerCapabilities.View,
            cancellationToken,
            Representation,
            Fields);
        if (!access.IsSuccess) return new(false, new Dictionary<string, CustomerAttentionProjection>());
        if (!access.Value!.Authorization.CanRead("id") || !access.Value.Authorization.CanRead("health"))
            return new(true, new Dictionary<string, CustomerAttentionProjection>());

        var trusted = access.Value.Trusted;
        var policy = access.Value.Authorization;
        if (policy.ScopeFilter is RecordAccessScopeFilter.Denied or RecordAccessScopeFilter.NotEvaluated
            || policy.ScopeFilter == RecordAccessScopeFilter.OwnedByMember && policy.ScopeOwnerMemberId is null)
            return new(true, new Dictionary<string, CustomerAttentionProjection>());
        var query = db.Customers.AsNoTracking().Where(x => x.WorkspaceId == trusted.WorkspaceId
            && x.OwnerId == trusted.MemberId && ids.Contains(x.CustomerId));
        query = policy.ScopeFilter switch
        {
            RecordAccessScopeFilter.Workspace => query,
            RecordAccessScopeFilter.OwnedByMember when policy.ScopeOwnerMemberId is not null =>
                query.Where(x => x.OwnerId == policy.ScopeOwnerMemberId),
            _ => query.Where(_ => false)
        };
        var rows = await query.Select(x => new { x.CustomerId, x.CustomerCode }).ToArrayAsync(cancellationToken);
        db.ReadAuditRecords.Add(new CustomerReadAuditRecord("readCustomerAttentionBatch", trusted.WorkspaceId,
            trusted.MemberId, null, requestContext.RequestId, requestContext.CorrelationId, null, clock.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
        return new(true, rows.ToDictionary(
            x => x.CustomerId,
            x => new CustomerAttentionProjection(
                x.CustomerId,
                policy.CanRead("customerCode") ? x.CustomerCode : x.CustomerId),
            StringComparer.Ordinal));
    }
}
