using UnicoreCRM.Crm.Leads.Application.Common;
using UnicoreCRM.Crm.Leads.Contracts;
using UnicoreCRM.Crm.Leads.Domain;

namespace UnicoreCRM.Crm.Leads.Application.ReadLeadSummary;

/// <summary>
/// The minimized Lead projection AI reads through. It carried its own copy of the record-scope and
/// field-visibility rules, which made it a second authorization authority over the same stored
/// policy; it now goes through the canonical AccessControl boundary like every other Leads use case.
/// </summary>
internal sealed class LeadSummaryReader(
    LeadAuthorization authorization,
    ILeadsPersistence persistence,
    TimeProvider timeProvider) : ILeadSummaryReader
{
    public async Task<LeadSummaryReadResult> ReadAsync(
        string leadId,
        string requestId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var metadata = new LeadRequestMetadata(requestId, correlationId);
        var access = await authorization.AuthorizeAsync(LeadCapabilities.Read, metadata, cancellationToken);
        if (!access.IsSuccess)
        {
            return new(access.Error!.Code == "WORKSPACE_MISMATCH"
                ? LeadSummaryReadStatus.WorkspaceMismatch
                : LeadSummaryReadStatus.AccessDenied);
        }

        if (!LeadValidation.IsEntityId(leadId))
            return new(LeadSummaryReadStatus.InvalidReference);

        var trusted = access.Value!.Trusted;
        var lead = await persistence.ReadLeadAsync(trusted.WorkspaceId, leadId, cancellationToken);
        if (lead is null)
            return new(LeadSummaryReadStatus.NotFound);
        if (await authorization.EnforceRecordAsync(access.Value!, lead, "readLeadSummary", metadata, cancellationToken) is not null)
            return new(LeadSummaryReadStatus.NotFound);

        var policy = access.Value!.Authorization;
        var document = LeadFieldSecurity.Project(LeadProjection.Document(lead), policy);
        var summary = new LeadSummaryProjection(
            lead.LeadId,
            policy.CanRead("displayName") ? document.DisplayName : null,
            policy.CanRead("leadWorkState") ? document.LeadWorkState : null,
            policy.CanRead("score") ? document.Score : null,
            policy.CanRead("priority") ? document.Priority : null,
            policy.CanRead("nextFollowUpAt") ? document.NextFollowUpAt : null);

        persistence.AddAudit(new LeadAuditRecord(
            "readLeadSummary",
            trusted.WorkspaceId,
            trusted.MemberId,
            lead.LeadId,
            requestId,
            correlationId,
            "READ",
            lead.Version,
            lead.Version,
            timeProvider.GetUtcNow()));
        await persistence.SaveChangesAsync(cancellationToken);
        return new(LeadSummaryReadStatus.Succeeded, summary);
    }
}
