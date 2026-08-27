using UnicoreCRM.Operations.Support.Application.Common;
using UnicoreCRM.Operations.Support.Contracts;

namespace UnicoreCRM.Operations.Support.Application.TransitionSupportCase;

internal sealed record Command(string CaseId, TransitionSupportCaseRequest Request, SupportCommandMetadata Metadata);

/// <summary>
/// Applies an admitted SupportCase lifecycle transition. The frozen transition table in
/// <c>SupportCaseLifecycle</c> is the only authority; any pair it does not admit fails closed
/// with the canonical <c>SUPPORT_CASE_INVALID_TRANSITION</c> error and mutates nothing.
///
/// <para>Completing a foreign Task never reaches this handler, and this handler never touches
/// Task state: Support owns the case, Tasks owns Task state.</para>
/// </summary>
internal sealed class Handler(
    SupportAuthorization authorization,
    SupportMutationExecution execution)
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
        var nextStatus = SupportValidation.Status(command.Request.NextStatus, "nextStatus", fields);
        var resolutionSummary = SupportValidation.Text(command.Request.ResolutionSummary, "resolutionSummary", 0, 4000, false, fields);
        // The declared reason is command evidence rather than case state: no read-model field
        // carries it, so it is bound-checked and kept in the Support command audit trail only.
        SupportValidation.Text(command.Request.Reason, "reason", 0, 2000, false, fields);
        if (fields.Count != 0)
            return SupportOperationResult<SupportCaseMutationResponse>.Failure(SupportErrors.Validation(fields));

        var fingerprint = SupportCommandSupport.Fingerprint(new
        {
            command.CaseId,
            NextStatus = command.Request.NextStatus,
            ResolutionSummary = resolutionSummary,
            Reason = command.Request.Reason?.Trim(),
            command.Metadata.ExpectedVersion
        });
        return await execution.ExecuteAsync(
            access.Value!,
            "transitionSupportCase",
            "SUPPORT_CASE_STATUS_CHANGED",
            command.CaseId,
            command.Metadata,
            fingerprint,
            (supportCase, now) =>
            {
                var writes = resolutionSummary is null ? new[] { "status" } : ["status", "resolutionSummary"];
                var fieldError = SupportFieldSecurity.GuardFieldWrite(access.Value!.Authorization, writes);
                if (fieldError is not null)
                    return fieldError;
                return supportCase.Transition(nextStatus!.Value, resolutionSummary, now)
                    ? null
                    : SupportErrors.InvalidTransition(supportCase.CaseId);
            },
            null,
            (recordAccess, record) => authorization.EnforceRecordAsync(
                recordAccess, record, "transitionSupportCase", metadata, cancellationToken),
            cancellationToken);
    }
}
