using UnicoreCRM.Operations.Support.Application.Common;
using UnicoreCRM.Operations.Support.Contracts;
using UnicoreCRM.Operations.Support.Domain;

namespace UnicoreCRM.Operations.Support.Application.AddSupportCaseInternalNote;

internal sealed record Command(string CaseId, AddSupportCaseInternalNoteRequest Request, SupportCommandMetadata Metadata);

/// <summary>
/// Appends internal Support collaboration evidence. The note is stored as internal and stays
/// semantically separate from a reply: Support emits no customer-facing notification and
/// exposes no customer-facing channel, so an internal note cannot leak outward. The append is
/// immutable: no admitted operation edits or deletes an internal note.
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
            "addSupportCaseInternalNote",
            "SUPPORT_CASE_INTERNAL_NOTE_ADDED",
            command.CaseId,
            command.Metadata,
            fingerprint,
            (supportCase, now) =>
            {
                persistence.AddComment(new SupportCaseComment(
                    trusted.WorkspaceId,
                    supportCase.CaseId,
                    SupportCaseCommentType.InternalNote,
                    body!,
                    trusted.MemberId,
                    now));
                supportCase.RecordComment(now);
                return null;
            },
            null,
            (recordAccess, record) => authorization.EnforceRecordAsync(
                recordAccess, record, "addSupportCaseInternalNote", metadata, cancellationToken),
            cancellationToken);
    }
}
