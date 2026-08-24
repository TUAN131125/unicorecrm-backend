using UnicoreCRM.Crm.Leads.Application.Common;
using UnicoreCRM.Crm.Leads.Contracts;
using UnicoreCRM.Crm.Leads.Domain;

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
        var access = await authorization.AuthorizeAsync(LeadCapabilities.Read, query.CorrelationId, cancellationToken);
        if (!access.IsSuccess)
            return LeadOperationResult<IReadOnlyList<LeadDocument>>.Failure(access.Error!);
        var trusted = access.Value!;
        var leads = await persistence.ListLeadsAsync(trusted.WorkspaceId, cancellationToken);
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
            leads.Select(LeadProjection.Document).ToArray());
    }
}
