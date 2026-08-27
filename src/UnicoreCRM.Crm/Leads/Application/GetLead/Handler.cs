using UnicoreCRM.Crm.Leads.Application.Common;
using UnicoreCRM.Crm.Leads.Contracts;
using UnicoreCRM.Crm.Leads.Domain;

namespace UnicoreCRM.Crm.Leads.Application.GetLead;

internal sealed record Query(string LeadId, string RequestId, string CorrelationId);

internal sealed class Handler(
    LeadAuthorization authorization,
    ILeadsPersistence persistence,
    TimeProvider timeProvider)
{
    internal async Task<LeadOperationResult<LeadDocument>> HandleAsync(Query query, CancellationToken cancellationToken)
    {
        var metadata = new LeadRequestMetadata(query.RequestId, query.CorrelationId);
        var access = await authorization.AuthorizeAsync(LeadCapabilities.Read, metadata, cancellationToken);
        if (!access.IsSuccess)
            return LeadOperationResult<LeadDocument>.Failure(access.Error!);
        if (!LeadValidation.IsEntityId(query.LeadId))
            return LeadOperationResult<LeadDocument>.Failure(LeadErrors.Validation(
                new Dictionary<string, string[]> { ["leadId"] = ["leadId is not a valid entity identifier."] }));
        var trusted = access.Value!.Trusted;
        var lead = await persistence.ReadLeadAsync(trusted.WorkspaceId, query.LeadId, cancellationToken);
        if (lead is null)
            return LeadOperationResult<LeadDocument>.Failure(LeadErrors.NotFound());

        // Record scope is enforced here, not left to the consumer. A lead inside the trusted
        // Workspace but outside the caller's record scope is reported as not found.
        var denied = await authorization.EnforceRecordAsync(access.Value!, lead, "getLead", metadata, cancellationToken);
        if (denied is not null)
            return LeadOperationResult<LeadDocument>.Failure(denied);

        persistence.AddAudit(new LeadAuditRecord(
            "getLead",
            trusted.WorkspaceId,
            trusted.MemberId,
            lead.LeadId,
            query.RequestId,
            query.CorrelationId,
            "READ",
            lead.Version,
            lead.Version,
            timeProvider.GetUtcNow()));
        await persistence.SaveChangesAsync(cancellationToken);
        return LeadOperationResult<LeadDocument>.Success(
            LeadFieldSecurity.Project(LeadProjection.Document(lead), access.Value!.Authorization));
    }
}
