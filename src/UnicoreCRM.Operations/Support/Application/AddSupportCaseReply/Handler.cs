using UnicoreCRM.Operations.Support.Application.Common;
using UnicoreCRM.Operations.Support.Contracts;
using UnicoreCRM.Operations.Support.Domain;

namespace UnicoreCRM.Operations.Support.Application.AddSupportCaseReply;

internal sealed record Command(string CaseId, AddSupportCaseReplyRequest Request, SupportCommandMetadata Metadata);

/// <summary>
/// Appends customer/agent-visible Support conversation evidence. The admitted request carries
/// only a body, and every admitted Support command runs under an authenticated Workspace
/// member holding <c>support.update</c>, so the stored reply is an agent reply and is not
/// internal. The append is immutable: no admitted operation edits or deletes a reply.
/// </summary>
internal sealed class Handler(
    SupportAuthorization authorization,
    SupportMutationExecution execution,
    ISupportPersistence persistence)
{
    internal async Task<SupportOperationResult<SupportCaseMutationResponse>> HandleAsync(
        Command command,
        CancellationToken cancellationToken)
    {
        var metadata = new SupportRequestMetadata(command.Metadata.RequestId, command.Metadata.CorrelationId);
        var access = await authorization.AuthorizeAsync(SupportCapabilities.Update, metadata, cancellationToken);
        if (!access.IsSuccess)
            return SupportOperationResult<SupportCaseMutationResponse>.Failure(access.Error!);
        if (!SupportValidation.IsEntityId(command.CaseId))
            return SupportOperationResult<SupportCaseMutationResponse>.Failure(SupportErrors.NotFound());

        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var body = SupportValidation.Text(command.Request.Body, "body", 1, 10000, true, fields);
        if (fields.Count != 0)
            return SupportOperationResult<SupportCaseMutationResponse>.Failure(SupportErrors.Validation(fields));

        var trusted = access.Value!.Trusted;
        var fingerprint = SupportCommandSupport.Fingerprint(new { command.CaseId, body, command.Metadata.ExpectedVersion });
        return await execution.ExecuteAsync(
            access.Value!,
            "addSupportCaseReply",
            "SUPPORT_CASE_REPLY_ADDED",
            command.CaseId,
            command.Metadata,
            fingerprint,
            (supportCase, now) =>
            {
                persistence.AddComment(new SupportCaseComment(
                    trusted.WorkspaceId,
                    supportCase.CaseId,
                    SupportCaseCommentType.AgentReply,
                    body!,
                    trusted.MemberId,
                    now));
                supportCase.RecordComment(now);
                return null;
            },
            null,
            (recordAccess, record) => authorization.EnforceRecordAsync(
                recordAccess, record, "addSupportCaseReply", metadata, cancellationToken),
            cancellationToken);
    }
}
