using UnicoreCRM.Crm.Leads.Application.Common;
using UnicoreCRM.Crm.Leads.Contracts;
using UnicoreCRM.Crm.Leads.Domain;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Crm.Leads.Application.ListLeads;

internal sealed record Query(string RequestId, string CorrelationId);

internal sealed class Handler(
    LeadAuthorization authorization,
    ILeadsPersistence persistence,
    TimeProvider timeProvider)
{
    internal async Task<LeadOperationResult<IReadOnlyList<LeadDocument>>> HandleAsync(
        Query query,
        CancellationToken cancellationToken)
    {
        var metadata = new LeadRequestMetadata(query.RequestId, query.CorrelationId);
        var access = await authorization.AuthorizeAsync(LeadCapabilities.Read, metadata, cancellationToken);
        if (!access.IsSuccess)
            return LeadOperationResult<IReadOnlyList<LeadDocument>>.Failure(access.Error!);
        var trusted = access.Value!.Trusted;

        // AccessControl resolves the record scope once and Leads pushes it into the owner query. A
        // denied scope returns nothing rather than a filtered view of everything.
        var scope = access.Value!.Authorization.ScopeFilter;
        if (scope == RecordAccessScopeFilter.Denied)
            return LeadOperationResult<IReadOnlyList<LeadDocument>>.Success([]);

        var leads = await persistence.ListLeadsAsync(
            trusted.WorkspaceId,
            scope == RecordAccessScopeFilter.OwnedByMember ? access.Value!.Authorization.ScopeOwnerMemberId : null,
            cancellationToken);
        persistence.AddAudit(new LeadAuditRecord(
            "listLeads",
            trusted.WorkspaceId,
            trusted.MemberId,
            null,
            query.RequestId,
            query.CorrelationId,
            "READ",
            null,
            null,
            timeProvider.GetUtcNow()));
        await persistence.SaveChangesAsync(cancellationToken);
        return LeadOperationResult<IReadOnlyList<LeadDocument>>.Success(
            leads.Select(lead => LeadFieldSecurity.Project(LeadProjection.Document(lead), access.Value!.Authorization)).ToArray());
    }
}
