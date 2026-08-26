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
        var access = await authorization.AuthorizeAsync(SupportCapabilities.Update, command.Metadata.CorrelationId, cancellationToken);
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
                supportCase.ReplaceProfile(profile!, now);
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
            cancellationToken);
    }
}
