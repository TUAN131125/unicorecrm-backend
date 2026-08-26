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
        var access = await authorization.AuthorizeAsync(SupportCapabilities.Create, command.Metadata.CorrelationId, cancellationToken);
        if (!access.IsSuccess)
            return SupportOperationResult<SupportCaseMutationResponse>.Failure(access.Error!);
        if (!CreateSupportCaseValidation.TryProfile(command.Request, out var profile, out var fields))
            return SupportOperationResult<SupportCaseMutationResponse>.Failure(SupportErrors.Validation(fields));

        var trusted = access.Value!;
        // An owner is a Workspace member, which the admitted narrow Workspace contract can verify.
        // The buyer relationship and the related order/product references are foreign-owner scalars
        // with no admitted reference contract, so they are recorded unverified.
        if (profile!.OwnerId is not null
            && !await memberValidator.IsActiveMemberAsync(trusted.WorkspaceId, profile.OwnerId, cancellationToken))
        {
            return SupportOperationResult<SupportCaseMutationResponse>.Failure(SupportErrors.Validation(
                new Dictionary<string, string[]> { ["ownerId"] = ["ownerId must reference an active member of the trusted workspace."] }));
        }

        var fingerprint = SupportCommandSupport.Fingerprint(profile);
        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);
        var scopeKey = SupportCommandSupport.ScopeKey(trusted, "createSupportCase", "WORKSPACE", command.Metadata.IdempotencyKey);
        var existing = await persistence.FindIdempotencyAsync(scopeKey, cancellationToken);
        if (existing is not null)
        {
            var replayError = SupportCommandSupport.ReplayError(existing, fingerprint);
            return replayError is null
                ? SupportOperationResult<SupportCaseMutationResponse>.Success(SupportCommandSupport.Replay(existing))
                : SupportOperationResult<SupportCaseMutationResponse>.Failure(replayError);
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
        return SupportOperationResult<SupportCaseMutationResponse>.Success(response);
    }
}
