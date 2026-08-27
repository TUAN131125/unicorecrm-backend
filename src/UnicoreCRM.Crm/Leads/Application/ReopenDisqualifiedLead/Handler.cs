using UnicoreCRM.Crm.Leads.Application.Common;
using UnicoreCRM.Crm.Leads.Contracts;

namespace UnicoreCRM.Crm.Leads.Application.ReopenDisqualifiedLead;

internal sealed record Command(string LeadId, ReopenDisqualifiedLeadRequest Request, LeadCommandMetadata Metadata);

internal sealed class Handler(LeadAuthorization authorization, LeadMutationExecution execution)
{
    internal async Task<LeadOperationResult<LeadMutationResponse>> HandleAsync(
        Command command,
        CancellationToken cancellationToken)
    {
        var metadata = new LeadRequestMetadata(command.Metadata.RequestId, command.Metadata.CorrelationId);
        var access = await authorization.AuthorizeAsync(LeadCapabilities.Update, metadata, cancellationToken);
        if (!access.IsSuccess)
            return LeadOperationResult<LeadMutationResponse>.Failure(access.Error!);
        if (!LeadValidation.IsEntityId(command.LeadId))
            return LeadOperationResult<LeadMutationResponse>.Failure(LeadErrors.Validation(
                new Dictionary<string, string[]> { ["leadId"] = ["leadId is not a valid entity identifier."] }));
        var fingerprint = LeadCommandSupport.Fingerprint(new { command.LeadId, command.Metadata.ExpectedVersion });
        return await execution.ExecuteAsync(
            access.Value!,
            "reopenDisqualifiedLead",
            "LEAD_REOPENED",
            command.LeadId,
            command.Metadata,
            fingerprint,
            (lead, now) => lead.Reopen(now) ? null : LeadErrors.ReopenNotAllowed(lead.LeadId),
            null,
            (recordAccess, record) => authorization.EnforceRecordAsync(
                recordAccess, record, "reopenDisqualifiedLead", metadata, cancellationToken),
            recordAccess => LeadAuthorization.EnforceFieldWrite(recordAccess, "leadWorkState", "qualificationOutcome"),
            cancellationToken);
    }
}
