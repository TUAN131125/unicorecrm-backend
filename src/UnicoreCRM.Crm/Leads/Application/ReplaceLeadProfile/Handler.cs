using UnicoreCRM.Crm.Leads.Application.Common;
using UnicoreCRM.Crm.Leads.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Leads.Application.ReplaceLeadProfile;

internal sealed record Command(string LeadId, ReplaceLeadProfileRequest Request, LeadCommandMetadata Metadata);

internal sealed class Handler(
    LeadAuthorization authorization,
    LeadMutationExecution execution,
    IWorkspaceMemberReferenceValidator memberValidator)
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
        if (!LeadValidation.TryProfile(command.Request, out var profile, out var fields))
            return LeadOperationResult<LeadMutationResponse>.Failure(LeadErrors.Validation(fields));
        var fingerprint = LeadCommandSupport.Fingerprint(new { command.LeadId, profile, command.Metadata.ExpectedVersion });
        return await execution.ExecuteAsync(
            access.Value!,
            "replaceLeadProfile",
            "LEAD_PROFILE_REPLACED",
            command.LeadId,
            command.Metadata,
            fingerprint,
            (lead, now) =>
            {
                lead.ReplaceProfile(profile!, now);
                return null;
            },
            async (trusted, token) => await memberValidator.IsActiveMemberAsync(trusted.WorkspaceId, profile!.OwnerId, token)
                ? null
                : LeadErrors.Validation(new Dictionary<string, string[]>
                {
                    ["ownerId"] = ["ownerId must reference an active member of the trusted workspace."]
                }),
            (recordAccess, record) => authorization.EnforceRecordAsync(
                recordAccess, record, "replaceLeadProfile", metadata, cancellationToken),
            cancellationToken);
    }
}
