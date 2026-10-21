using UnicoreCRM.Crm.Customers.Application.Common;
using UnicoreCRM.Crm.Customers.Contracts;
using UnicoreCRM.Crm.Customers.Domain;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Customers.Application.ResolveRelationshipTargets;

internal sealed class Participant(CustomerAuthorization authorization, ICustomersPersistence persistence, TimeProvider timeProvider)
    : ICustomerRelationshipTargetParticipant
{
    public async Task<CustomerRelationshipTargetResolution> ResolveSubjectAsync(
        TrustedWorkspaceContext trusted, string subjectType, string subjectId,
        string requestId, string correlationId, CancellationToken cancellationToken)
    {
        // Canonical account subjects are distinct from Contact stakeholder memberships.
        // Resolve by the persisted unique subject, then apply the same Customer security.
        if (subjectType is not ("CONTACT" or "ORGANIZATION_ACCOUNT"))
            return new(false, new Dictionary<string, string?>(), new HashSet<string>(StringComparer.Ordinal));
        var record = await persistence.LoadBySubjectAsync(trusted.WorkspaceId, subjectType, subjectId, cancellationToken);
        return await ResolveVisibleAsync(trusted, record is null ? [] : [record.CustomerId],
            requestId, correlationId, cancellationToken);
    }

    public async Task<CustomerRelationshipTargetResolution> ResolveVisibleAsync(
        TrustedWorkspaceContext trusted, IReadOnlyCollection<string> customerIds,
        string requestId, string correlationId, CancellationToken cancellationToken)
    {
        var metadata = new CustomerRequestMetadata(requestId, correlationId);
        var access = await authorization.AuthorizeAsync(metadata, cancellationToken);
        if (!access.IsSuccess || access.Value!.Trusted.WorkspaceId != trusted.WorkspaceId)
            return new(false, new Dictionary<string, string?>(), new HashSet<string>(StringComparer.Ordinal));

        var records = await persistence.ReadCustomersAsync(trusted.WorkspaceId, customerIds, cancellationToken);
        var visible = new Dictionary<string, string?>(StringComparer.Ordinal);
        var eligible = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            var denied = await authorization.EnforceRecordAsync(access.Value, record,
                "resolveContactCustomerRelationshipTarget", metadata, cancellationToken);
            if (denied is null)
            {
                visible[record.CustomerId] = record.CustomerCode;
                if (record.Status != "ARCHIVED") eligible.Add(record.CustomerId);
                persistence.AddReadAudit(new CustomerReadAuditRecord(
                    "resolveContactCustomerRelationshipTarget", trusted.WorkspaceId,
                    trusted.MemberId, record.CustomerId, requestId, correlationId,
                    record.Version, timeProvider.GetUtcNow()));
            }
        }
        if (visible.Count > 0) await persistence.SaveChangesAsync(cancellationToken);
        return new(true, visible, eligible);
    }
}
