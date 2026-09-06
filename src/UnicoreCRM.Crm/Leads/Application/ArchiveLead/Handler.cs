using UnicoreCRM.Crm.Leads.Application.Common;
using UnicoreCRM.Crm.Leads.Contracts;

namespace UnicoreCRM.Crm.Leads.Application.ArchiveLead;

internal sealed record Command(string LeadId, ArchiveLeadRequest Request, LeadCommandMetadata Metadata);

internal sealed class Handler(LeadAuthorization authorization, LeadMutationExecution execution)
{
    internal async Task<LeadOperationResult<LeadMutationResponse>> HandleAsync(
        Command command,
        CancellationToken cancellationToken)
    {
        var metadata = new LeadRequestMetadata(command.Metadata.RequestId, command.Metadata.CorrelationId);
        var access = await authorization.AuthorizeAsync(LeadCapabilities.Delete, metadata, cancellationToken);
        if (!access.IsSuccess)
            return LeadOperationResult<LeadMutationResponse>.Failure(access.Error!);
        if (!LeadValidation.IsEntityId(command.LeadId))
            return LeadOperationResult<LeadMutationResponse>.Failure(LeadErrors.Validation(
                new Dictionary<string, string[]> { ["leadId"] = ["leadId is not a valid entity identifier."] }));

        var reason = command.Request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason))
        {
            return LeadOperationResult<LeadMutationResponse>.Failure(LeadErrors.ArchiveReasonRequired(
                new Dictionary<string, string[]>
                {
                    ["reason"] = ["reason is required."]
                }));
        }
        if (reason.Length > 2000)
        {
            return LeadOperationResult<LeadMutationResponse>.Failure(LeadErrors.Validation(
                new Dictionary<string, string[]> { ["reason"] = ["reason cannot contain more than 2000 characters."] }));
        }

        var fingerprint = LeadCommandSupport.Fingerprint(
            new { command.LeadId, reason, command.Metadata.ExpectedVersion });
        return await execution.ExecuteAsync(
            access.Value!,
            "archiveLead",
            "LEAD_ARCHIVED",
            command.LeadId,
            command.Metadata,
            fingerprint,
            (lead, now) => lead.Archive(reason, now) ? null : LeadErrors.AlreadyArchived(lead.LeadId),
            null,
            (recordAccess, record) => authorization.EnforceRecordAsync(
                recordAccess, record, "archiveLead", metadata, cancellationToken),
            recordAccess => LeadAuthorization.EnforceFieldWrite(recordAccess, "archivedAt", "archiveReason"),
            cancellationToken);
    }
}
