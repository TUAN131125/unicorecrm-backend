using UnicoreCRM.Operations.Support.Application.Common;
using UnicoreCRM.Operations.Support.Contracts;
using UnicoreCRM.Operations.Support.Domain;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Operations.Support.Application.CreateSupportCase;

internal sealed record Command(CreateSupportCaseRequest Request, SupportCommandMetadata Metadata);

internal sealed class Handler(
    SupportAuthorization authorization,
    ISupportPersistence persistence,
    IWorkspaceMemberReferenceValidator memberValidator,
    TimeProvider timeProvider)
{
    internal async Task<SupportOperationResult<SupportCaseMutationResponse>> HandleAsync(
        Command command,
        CancellationToken cancellationToken)
    {
        var metadata = new SupportRequestMetadata(command.Metadata.RequestId, command.Metadata.CorrelationId);
        var access = await authorization.AuthorizeAsync(SupportCapabilities.Create, metadata, cancellationToken);
        if (!access.IsSuccess)
            return SupportOperationResult<SupportCaseMutationResponse>.Failure(access.Error!);
        if (!CreateSupportCaseValidation.TryProfile(command.Request, out var profile, out var fields))
            return SupportOperationResult<SupportCaseMutationResponse>.Failure(SupportErrors.Validation(fields));

        var trusted = access.Value!.Trusted;
        var fingerprint = SupportCommandSupport.Fingerprint(profile);
        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);
        var scopeKey = SupportCommandSupport.ScopeKey(trusted, "createSupportCase", "WORKSPACE", command.Metadata.IdempotencyKey);
        var existing = await persistence.FindIdempotencyAsync(scopeKey, cancellationToken);
        if (existing is not null)
        {
            // Answered from stored evidence alone, so an owner deactivated after the original
            // commit cannot retroactively invalidate the replay or create a second SupportCase.
            var replayError = SupportCommandSupport.ReplayError(existing, fingerprint);
            return replayError is null
                ? SupportOperationResult<SupportCaseMutationResponse>.Success(Project(SupportCommandSupport.Replay(existing), access.Value!))
                : SupportOperationResult<SupportCaseMutationResponse>.Failure(replayError);
        }

        // Creation is a resource-level question, so no record scope applies, but field security
        // still does: a field the caller may not write must not be written on the way in either.
        // Assignment authority is separate from creation authority, so naming an owner at creation
        // requires support.assign exactly as a later assignment does.
        //
        // Both checks authorize a write, so both belong to the new-execution path. A committed
        // creation writes nothing on replay and must stay replayable after a field turns READ_ONLY
        // or HIDDEN, or after the caller loses support.assign.
        var createWriteError = SupportFieldSecurity.GuardCreateWrite(access.Value!.Authorization, profile!);
        if (createWriteError is not null)
            return SupportOperationResult<SupportCaseMutationResponse>.Failure(createWriteError);
        if (profile!.OwnerId is not null && !access.Value!.Authorization.Holds(SupportCapabilities.Assign.Capability))
            return SupportOperationResult<SupportCaseMutationResponse>.Failure(SupportErrors.OwnerAssignmentDenied());

        // Only a genuinely new command evaluates current mutable owner/member state. An owner is a
        // Workspace member, which the admitted narrow Workspace contract can verify. The buyer
        // relationship and the related order/product references are foreign-owner scalars with no
        // admitted reference contract, so they are recorded unverified.
        if (profile.OwnerId is not null
            && !await memberValidator.IsActiveMemberAsync(trusted.WorkspaceId, profile.OwnerId, cancellationToken))
        {
            return SupportOperationResult<SupportCaseMutationResponse>.Failure(SupportErrors.Validation(
                new Dictionary<string, string[]> { ["ownerId"] = ["ownerId must reference an active member of the trusted workspace."] }));
        }

        var now = timeProvider.GetUtcNow();
        var caseYear = now.UtcDateTime.Year;
        var sequence = await persistence.MaxCaseSequenceAsync(trusted.WorkspaceId, caseYear, cancellationToken) + 1;
        var supportCase = new SupportCase(
            trusted.WorkspaceId,
            caseYear,
            sequence,
            SupportCaseNumber.Format(caseYear, sequence),
            profile,
            now);
        persistence.AddCase(supportCase);
        var response = SupportCommandSupport.RecordCommit(
            persistence,
            supportCase,
            trusted,
            command.Metadata,
            "createSupportCase",
            "SUPPORT_CASE_CREATED",
            scopeKey,
            "WORKSPACE",
            fingerprint,
            null,
            now);
        await persistence.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return SupportOperationResult<SupportCaseMutationResponse>.Success(Project(response, access.Value!));
    }

    private static SupportCaseMutationResponse Project(SupportCaseMutationResponse response, SupportAccess access) =>
        response with
        {
            Result = new SupportCaseMutationResult(
                SupportFieldSecurity.Project(response.Result.SupportCase, access.Authorization))
        };
}
