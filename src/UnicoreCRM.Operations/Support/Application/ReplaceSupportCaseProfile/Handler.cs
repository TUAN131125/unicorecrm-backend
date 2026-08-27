using UnicoreCRM.Operations.Support.Application.Common;
using UnicoreCRM.Operations.Support.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Operations.Support.Application.ReplaceSupportCaseProfile;

internal sealed record Command(string CaseId, ReplaceSupportCaseProfileRequest Request, SupportCommandMetadata Metadata);

internal sealed class Handler(
    SupportAuthorization authorization,
    SupportMutationExecution execution,
    IWorkspaceMemberReferenceValidator memberValidator)
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
        if (!ReplaceSupportCaseProfileValidation.TryProfile(command.Request, out var profile, out var fields))
            return SupportOperationResult<SupportCaseMutationResponse>.Failure(SupportErrors.Validation(fields));

        var fingerprint = SupportCommandSupport.Fingerprint(new { command.CaseId, Profile = profile, command.Metadata.ExpectedVersion });
        return await execution.ExecuteAsync(
            access.Value!,
            "replaceSupportCaseProfile",
            "SUPPORT_CASE_PROFILE_REPLACED",
            command.CaseId,
            command.Metadata,
            fingerprint,
            (supportCase, now) =>
            {
                // The profile contract carries ownerId, but assignment authority is support.assign,
                // not support.update. Without this check a caller holding only support.update could
                // assign, reassign or clear the owner through a profile replacement and quietly
                // acquire the assignment privilege. Replacing the owner with the value it already
                // holds is not an assignment and is left alone.
                if (!string.Equals(supportCase.OwnerId, profile!.OwnerId, StringComparison.Ordinal)
                    && !access.Value!.Authorization.Holds(SupportCapabilities.Assign.Capability))
                {
                    return SupportErrors.OwnerAssignmentDenied();
                }

                var fieldError = SupportFieldSecurity.GuardProfileWrite(access.Value!.Authorization, supportCase, profile);
                if (fieldError is not null)
                    return fieldError;

                supportCase.ReplaceProfile(profile, now);
                return null;
            },
            profile!.OwnerId is null
                ? null
                : async (trusted, token) => await memberValidator.IsActiveMemberAsync(trusted.WorkspaceId, profile.OwnerId, token)
                    ? null
                    : SupportErrors.Validation(new Dictionary<string, string[]>
                    {
                        ["ownerId"] = ["ownerId must reference an active member of the trusted workspace."]
                    }),
            (recordAccess, record) => authorization.EnforceRecordAsync(
                recordAccess, record, "replaceSupportCaseProfile", metadata, cancellationToken),
            cancellationToken);
    }
}
