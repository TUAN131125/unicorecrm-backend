using UnicoreCRM.Crm.Leads.Application.Common;
using UnicoreCRM.Crm.Leads.Contracts;
using UnicoreCRM.Crm.Leads.Domain;

using UnicoreCRM.Platform.AccessControl.Contracts;

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
    /// <summary>
    /// The representation this reader returns. Every property of <c>LeadSummaryProjection</c> except the identifier is
    /// declared nullable by that contract, so each of these fields genuinely has an admitted absent
    /// representation here even where the module's full read model makes it required. The set is a
    /// fixed static declaration owned by this operation, never assembled per request, and it can
    /// only turn a refusal into a withheld value - never a withheld value into a returned one.
    /// </summary>
    private static readonly RecordAccessRepresentation Representation =
        RecordAccessRepresentation.Create("lead.summary", "displayName", "leadWorkState", "score", "priority", "nextFollowUpAt");

    public async Task<LeadSummaryReadResult> ReadAsync(
        string leadId,
        string requestId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var metadata = new LeadRequestMetadata(requestId, correlationId);
        // Every field of the minimized summary contract is optional, so this operation can return
        // any of them absent. The full Lead read model makes some of them required, but that
        // declaration governs the full representation, not this one.
        var access = await authorization.AuthorizeAsync(
            LeadCapabilities.Read, metadata, cancellationToken, Representation);
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
