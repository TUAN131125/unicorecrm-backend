using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Crm.Leads.Application.Common;
using UnicoreCRM.Crm.Leads.Contracts;
using UnicoreCRM.Crm.Leads.Domain;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Crm.Leads.Infrastructure.Persistence;

internal sealed class LeadLinkReader(LeadsDbContext db, LeadAuthorization authorization) : ILeadLinkReader
{
    private static readonly RecordAccessRepresentation Representation = RecordAccessRepresentation.Create(
        "lead.manualReference", LeadFieldSecurity.FieldKeys.Where(x => x != "id").ToArray());

    private Task<LeadOperationResult<LeadAccess>> Access(string request, string correlation, CancellationToken ct) =>
        authorization.AuthorizeAsync(LeadCapabilities.Read, new(request, correlation), ct, Representation);

    private static LeadLinkTarget Project(Lead row, RecordAccessAuthorization policy) => new(row.LeadId,
        policy.CanRead("displayName") ? row.Profile.DisplayName : null, row.ArchivedAt is null,
        policy.CanRead("relationshipRef") && policy.CanRead("leadWorkState") && policy.CanRead("qualificationOutcome")
            && row.WorkState == LeadWorkState.Closed
            && row.QualificationOutcome is LeadQualificationOutcome.Nurture or LeadQualificationOutcome.Opportunity
            && row.RelationshipType == "CONTACT" ? row.RelationshipId : null);

    public async Task<LeadLinkTarget?> ReadAsync(string id, string requestId, string correlationId, CancellationToken ct)
    {
        var result = await Access(requestId, correlationId, ct);
        if (!result.IsSuccess || !LeadValidation.IsEntityId(id)) return null;
        var access = result.Value!;
        var row = await db.Leads.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == access.Trusted.WorkspaceId && x.LeadId == id, ct);
        if (row is null || await authorization.EnforceRecordAsync(access, row, "readLeadManualReference", new(requestId, correlationId), ct) is not null) return null;
        return Project(row, access.Authorization);
    }

    public async Task<IReadOnlyList<LeadLinkTarget>> SearchAsync(string search, string requestId, string correlationId, CancellationToken ct)
    {
        var result = await Access(requestId, correlationId, ct);
        if (!result.IsSuccess) return [];
        var access = result.Value!;
        // Search only the owner-maintained ID/name projection, never hidden phone/email.
        if (!access.Authorization.CanRead("displayName")) return [];
        var query = db.Leads.AsNoTracking().Where(x => x.WorkspaceId == access.Trusted.WorkspaceId && x.ArchivedAt == null);
        if (access.Authorization.ScopeFilter == RecordAccessScopeFilter.OwnedByMember) query = query.Where(x => x.ScopeOwnerId == access.Trusted.MemberId);
        else if (access.Authorization.ScopeFilter != RecordAccessScopeFilter.Workspace) return [];
        var normalized = search.ToUpperInvariant();
        var rows = await query.Where(x => x.SearchText.Contains(normalized)).OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.LeadId).Take(20).ToListAsync(ct);
        var targets = new List<LeadLinkTarget>();
        foreach (var row in rows)
            if (await authorization.EnforceRecordAsync(access, row, "searchLeadManualReferences", new(requestId, correlationId), ct) is null)
                targets.Add(Project(row, access.Authorization));
        return targets;
    }
}
