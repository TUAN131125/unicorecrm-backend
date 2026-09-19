using System.Text.RegularExpressions;
using UnicoreCRM.Crm.Organizations.Application.Common;
using UnicoreCRM.Crm.Organizations.Contracts;
using UnicoreCRM.Crm.Organizations.Domain;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Crm.Organizations.Application.ReadOrganizationSummary;

internal sealed class OrganizationSummaryReader(OrganizationAuthorization authorization, IOrganizationsPersistence persistence, TimeProvider clock) : IOrganizationSummaryReader
{
    private static readonly RecordAccessRepresentation Representation = RecordAccessRepresentation.Create("organization.summary", "displayName", "status", "industry", "employeeCount", "relationshipLevel");
    public async Task<OrganizationSummaryReadResult> ReadAsync(string id, string requestId, string correlationId, CancellationToken ct)
    {
        var metadata = new OrganizationRequestMetadata(requestId, correlationId);
        var access = await authorization.AuthorizeAsync(metadata, OrganizationCapabilities.Read, ct, Representation);
        if (!access.IsSuccess) return new(access.Error!.Code == "WORKSPACE_MISMATCH" ? OrganizationSummaryReadStatus.WorkspaceMismatch : OrganizationSummaryReadStatus.AccessDenied);
        if (!Regex.IsMatch(id, "^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)) return new(OrganizationSummaryReadStatus.InvalidReference);
        var record = await persistence.ReadOrganizationAsync(access.Value!.Trusted.WorkspaceId, id, ct);
        if (record is null || await authorization.EnforceRecordAsync(access.Value, record, "readOrganizationSummary", metadata, ct) is not null) return new(OrganizationSummaryReadStatus.NotFound);
        var document = OrganizationFieldSecurity.Project(OrganizationProjection.Document(record), access.Value.Authorization);
        var policy = access.Value.Authorization;
        var projection = new OrganizationSummaryProjection(record.OrganizationId, policy.CanRead("displayName") ? document.DisplayName : null, policy.CanRead("status") ? document.Status : null, policy.CanRead("industry") ? document.Industry : null, policy.CanRead("employeeCount") ? document.EmployeeCount : null, policy.CanRead("relationshipLevel") ? document.RelationshipLevel : null, record.Version);
        persistence.AddReadAudit(new OrganizationReadAuditRecord("readOrganizationSummary", access.Value.Trusted.WorkspaceId, access.Value.Trusted.MemberId, record.OrganizationId, requestId, correlationId, record.Version, clock.GetUtcNow()));
        await persistence.SaveChangesAsync(ct);
        return new(OrganizationSummaryReadStatus.Succeeded, projection);
    }
}
