using UnicoreCRM.Crm.Leads.Application.Common;
using UnicoreCRM.Crm.Leads.Contracts;
using UnicoreCRM.Crm.Leads.Domain;

namespace UnicoreCRM.Crm.Leads.Application.AdvanceLeadWorkState;

internal sealed record Command(string LeadId, AdvanceLeadWorkStateRequest Request, LeadCommandMetadata Metadata);

internal sealed class Handler(LeadAuthorization authorization, LeadMutationExecution execution)
{
    internal async Task<LeadOperationResult<LeadMutationResponse>> HandleAsync(
        Command command,
        CancellationToken cancellationToken)
    {
        var access = await authorization.AuthorizeAsync(LeadCapabilities.Update, command.Metadata.CorrelationId, cancellationToken);
        if (!access.IsSuccess)
            return LeadOperationResult<LeadMutationResponse>.Failure(access.Error!);
        if (!LeadValidation.IsEntityId(command.LeadId))
            return LeadOperationResult<LeadMutationResponse>.Failure(LeadErrors.Validation(
                new Dictionary<string, string[]> { ["leadId"] = ["leadId is not a valid entity identifier."] }));
        if (!AdvanceLeadWorkStateValidation.TryAdvance(command.Request, out var target, out var verification, out var fields))
            return LeadOperationResult<LeadMutationResponse>.Failure(LeadErrors.Validation(fields));
        var fingerprint = LeadCommandSupport.Fingerprint(new { command.LeadId, target, verification, command.Metadata.ExpectedVersion });
        return await execution.ExecuteAsync(
            access.Value!,
            "advanceLeadWorkState",
            "LEAD_WORK_STATE_ADVANCED",
            command.LeadId,
            command.Metadata,
            fingerprint,
            (lead, now) => lead.Advance(target, verification, now) switch
            {
                LeadTransitionResult.Succeeded => null,
                LeadTransitionResult.ProfileIncomplete => LeadErrors.ProgressiveProfile(
                    AdvanceLeadWorkStateValidation.ProgressiveProfileErrors(lead.Profile.WithVerification(verification))),
                _ => LeadErrors.InvalidTransition(lead.LeadId)
            },
            null,
            cancellationToken);
    }
}
