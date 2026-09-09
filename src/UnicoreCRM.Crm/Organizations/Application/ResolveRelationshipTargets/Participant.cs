using UnicoreCRM.Crm.Organizations.Application.Common;
using UnicoreCRM.Crm.Organizations.Contracts;
using UnicoreCRM.Crm.Organizations.Domain;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Organizations.Application.ResolveRelationshipTargets;

internal sealed class Participant(OrganizationAuthorization authorization, IOrganizationsPersistence persistence, TimeProvider timeProvider)
    : IOrganizationRelationshipTargetParticipant
{
    public async Task<OrganizationRelationshipTargetResolution> ResolveVisibleAsync(
        TrustedWorkspaceContext trusted, IReadOnlyCollection<string> organizationIds,
        string requestId, string correlationId, CancellationToken cancellationToken)
    {
        var metadata = new OrganizationRequestMetadata(requestId, correlationId);
        var access = await authorization.AuthorizeAsync(metadata, cancellationToken);
        if (!access.IsSuccess || access.Value!.Trusted.WorkspaceId != trusted.WorkspaceId)
            return new(false, new Dictionary<string, string?>());

        var records = await persistence.ReadOrganizationsAsync(trusted.WorkspaceId, organizationIds, cancellationToken);
        var visible = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            var denied = await authorization.EnforceRecordAsync(access.Value, record,
                "resolveContactOrganizationRelationshipTarget", metadata, cancellationToken);
            if (denied is null)
            {
                visible[record.OrganizationId] = record.DisplayName;
                persistence.AddReadAudit(new OrganizationReadAuditRecord(
                    "resolveContactOrganizationRelationshipTarget", trusted.WorkspaceId,
                    trusted.MemberId, record.OrganizationId, requestId, correlationId,
                    record.Version, timeProvider.GetUtcNow()));
            }
        }
        if (visible.Count > 0) await persistence.SaveChangesAsync(cancellationToken);
        return new(true, visible);
    }
}
