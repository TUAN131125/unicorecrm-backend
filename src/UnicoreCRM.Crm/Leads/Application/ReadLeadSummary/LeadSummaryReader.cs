using UnicoreCRM.Crm.Leads.Application.Common;
using UnicoreCRM.Crm.Leads.Contracts;
using UnicoreCRM.Crm.Leads.Domain;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Leads.Application.ReadLeadSummary;

internal sealed class LeadSummaryReader(
    ICurrentWorkspace currentWorkspace,
    IAccessAuthorizer accessAuthorizer,
    ILeadsPersistence persistence,
    TimeProvider timeProvider) : ILeadSummaryReader
{
    public async Task<LeadSummaryReadResult> ReadAsync(
        string leadId,
        string requestId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (!currentWorkspace.IsResolved)
            return new(LeadSummaryReadStatus.WorkspaceMismatch);

        var access = await accessAuthorizer.AuthorizeAsync(LeadCapabilities.Read, correlationId, cancellationToken);
        if (!access.IsAllowed)
        {
            return new(access.Code == "WORKSPACE_MISMATCH"
                ? LeadSummaryReadStatus.WorkspaceMismatch
                : LeadSummaryReadStatus.AccessDenied);
        }

        if (!LeadValidation.IsEntityId(leadId))
            return new(LeadSummaryReadStatus.InvalidReference);

        var trusted = currentWorkspace.Require();
        var lead = await persistence.ReadLeadAsync(trusted.WorkspaceId, leadId, cancellationToken);
        if (lead is null || !CanReadRecord(access.Context!, trusted.MemberId, lead.Profile.OwnerId))
            return new(LeadSummaryReadStatus.NotFound);

        var document = LeadProjection.Document(lead);
        var summary = new LeadSummaryProjection(
            lead.LeadId,
            Visible(access.Context!, "displayName") ? document.DisplayName : null,
            Visible(access.Context!, "leadWorkState") ? document.LeadWorkState : null,
            Visible(access.Context!, "score") ? document.Score : null,
            Visible(access.Context!, "priority") ? document.Priority : null,
            Visible(access.Context!, "nextFollowUpAt") ? document.NextFollowUpAt : null);

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

    private static bool CanReadRecord(AuthorizationContextDocument context, string memberId, string ownerId)
    {
        var scope = context.DataScopes.FirstOrDefault(item =>
            string.Equals(item.ResourceKey, "leads", StringComparison.OrdinalIgnoreCase));
        return scope?.Scope.ToUpperInvariant() switch
        {
            null or "WORKSPACE" => true,
            "OWN" => string.Equals(memberId, ownerId, StringComparison.Ordinal),
            _ => false
        };
    }

    private static bool Visible(AuthorizationContextDocument context, string fieldKey)
    {
        var field = context.FieldSecurity.FirstOrDefault(item =>
            string.Equals(item.ResourceKey, "leads", StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.FieldKey, fieldKey, StringComparison.OrdinalIgnoreCase));
        return field is null || field.Access is "READ_ONLY" or "READ_WRITE";
    }
}
