using UnicoreCRM.Operations.Support.Application.Common;
using UnicoreCRM.Operations.Support.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Operations.Support.Application.AssignSupportCase;

internal sealed record Command(string CaseId, AssignSupportCaseRequest Request, SupportCommandMetadata Metadata);

/// <summary>
/// Records the Support-owned owner assignment. The owner must be an active member of the
/// trusted Workspace, verified through the admitted narrow Workspace member contract. No
/// admitted authority makes assignment change the case lifecycle, so the status is untouched.
/// </summary>
internal sealed class Handler(
    SupportAuthorization authorization,
    SupportMutationExecution execution,
    IWorkspaceMemberReferenceValidator memberValidator)
{
    internal async Task<SupportOperationResult<SupportCaseMutationResponse>> HandleAsync(
        Command command,
        CancellationToken cancellationToken)
    {
        var access = await authorization.AuthorizeAsync(SupportCapabilities.Assign, command.Metadata.CorrelationId, cancellationToken);
        if (!access.IsSuccess)
            return SupportOperationResult<SupportCaseMutationResponse>.Failure(access.Error!);
        if (!SupportValidation.IsEntityId(command.CaseId))
            return SupportOperationResult<SupportCaseMutationResponse>.Failure(SupportErrors.NotFound());

        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var ownerId = SupportValidation.Entity(command.Request.OwnerId, "ownerId", true, fields);
        if (fields.Count != 0)
            return SupportOperationResult<SupportCaseMutationResponse>.Failure(SupportErrors.Validation(fields));

        var fingerprint = SupportCommandSupport.Fingerprint(new { command.CaseId, ownerId, command.Metadata.ExpectedVersion });
        return await execution.ExecuteAsync(
            access.Value!,
            "assignSupportCase",
            "SUPPORT_CASE_ASSIGNED",
            command.CaseId,
            command.Metadata,
            fingerprint,
            (supportCase, now) =>
            {
                supportCase.Assign(ownerId!, now);
                return null;
            },
            async (trusted, token) => await memberValidator.IsActiveMemberAsync(trusted.WorkspaceId, ownerId!, token)
                ? null
                : SupportErrors.Validation(new Dictionary<string, string[]>
                {
                    ["ownerId"] = ["ownerId must reference an active member of the trusted workspace."]
                }),
            cancellationToken);
    }
}
