using UnicoreCRM.Crm.Leads.Application.Common;
using UnicoreCRM.Crm.Leads.Contracts;

namespace UnicoreCRM.Crm.Leads.Application.DisqualifyLead;

internal sealed record Command(string LeadId, DisqualifyLeadRequest Request, LeadCommandMetadata Metadata);

internal sealed class Handler(LeadAuthorization authorization, LeadMutationExecution execution)
{
    internal async Task<LeadOperationResult<LeadMutationResponse>> HandleAsync(
        Command command,
        CancellationToken cancellationToken)
    {
        var metadata = new LeadRequestMetadata(command.Metadata.RequestId, command.Metadata.CorrelationId);
        var access = await authorization.AuthorizeAsync(LeadCapabilities.Qualify, metadata, cancellationToken);
        if (!access.IsSuccess)
            return LeadOperationResult<LeadMutationResponse>.Failure(access.Error!);
        if (!LeadValidation.IsEntityId(command.LeadId))
            return LeadOperationResult<LeadMutationResponse>.Failure(LeadErrors.Validation(
                new Dictionary<string, string[]> { ["leadId"] = ["leadId is not a valid entity identifier."] }));
        if (!DisqualifyLeadValidation.TryDisqualify(command.Request, out var reason, out var evidence, out var fields))
            return LeadOperationResult<LeadMutationResponse>.Failure(LeadErrors.DisqualificationEvidence(fields));
        var trusted = access.Value!.Trusted;
        var fingerprint = LeadCommandSupport.Fingerprint(new { command.LeadId, reason, evidence, command.Metadata.ExpectedVersion });
        return await execution.ExecuteAsync(
            access.Value!,
            "disqualifyLead",
            "LEAD_DISQUALIFIED",
            command.LeadId,
            command.Metadata,
            fingerprint,
            (lead, now) => lead.Disqualify(reason!, evidence!, trusted.MemberId, now)
                ? null
                : LeadErrors.InvalidTransition(lead.LeadId),
            null,
            (recordAccess, record) => authorization.EnforceRecordAsync(
                recordAccess, record, "disqualifyLead", metadata, cancellationToken, "leadWorkState", "qualificationOutcome", "disqualifiedAt", "disqualifiedBy", "disqualificationReason", "disqualificationNote"),
            cancellationToken);
    }
}
