using System.Text.RegularExpressions;
using UnicoreCRM.Crm.Customers.Application.Common;
using UnicoreCRM.Crm.Customers.Contracts;
using UnicoreCRM.Crm.Customers.Domain;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Crm.Customers.Application.ReadCustomerSummary;

internal sealed class CustomerSummaryReader(CustomerAuthorization authorization, ICustomersPersistence persistence, TimeProvider clock) : ICustomerSummaryReader
{
    private static readonly RecordAccessRepresentation Representation = RecordAccessRepresentation.Create("customer.summary", "customerCode", "status", "health", "tier", "segment", "nextCareAt");
    public async Task<CustomerSummaryReadResult> ReadAsync(string id, string requestId, string correlationId, CancellationToken ct)
    {
        var metadata = new CustomerRequestMetadata(requestId, correlationId);
        var access = await authorization.AuthorizeAsync(metadata, CustomerCapabilities.View, ct, Representation);
        if (!access.IsSuccess) return new(access.Error!.Code == "WORKSPACE_MISMATCH" ? CustomerSummaryReadStatus.WorkspaceMismatch : CustomerSummaryReadStatus.AccessDenied);
        if (!Regex.IsMatch(id, "^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)) return new(CustomerSummaryReadStatus.InvalidReference);
        var record = await persistence.ReadCustomerAsync(access.Value!.Trusted.WorkspaceId, id, ct);
        if (record is null || await authorization.EnforceRecordAsync(access.Value, record, "readCustomerSummary", metadata, ct) is not null) return new(CustomerSummaryReadStatus.NotFound);
        var document = CustomerFieldSecurity.Project(CustomerProjection.Document(record), access.Value.Authorization);
        var policy = access.Value.Authorization;
        var projection = new CustomerSummaryProjection(record.CustomerId, policy.CanRead("customerCode") ? document.CustomerCode : null, policy.CanRead("status") ? document.Status : null, policy.CanRead("health") ? document.Health : null, policy.CanRead("tier") ? document.Tier : null, policy.CanRead("segment") ? document.Segment : null, policy.CanRead("nextCareAt") ? document.NextCareAt : null, record.Version);
        persistence.AddReadAudit(new CustomerReadAuditRecord("readCustomerSummary", access.Value.Trusted.WorkspaceId, access.Value.Trusted.MemberId, record.CustomerId, requestId, correlationId, record.Version, clock.GetUtcNow()));
        await persistence.SaveChangesAsync(ct);
        return new(CustomerSummaryReadStatus.Succeeded, projection);
    }
}
