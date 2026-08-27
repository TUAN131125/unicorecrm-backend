using UnicoreCRM.Operations.Support.Application.Common;
using UnicoreCRM.Operations.Support.Contracts;
using UnicoreCRM.Operations.Support.Domain;

namespace UnicoreCRM.Operations.Support.Application.GetSupportCase;

internal sealed record Query(string CaseId, string RequestId, string CorrelationId);

internal sealed class Handler(
    SupportAuthorization authorization,
    ISupportPersistence persistence,
    TimeProvider timeProvider)
{
    internal async Task<SupportOperationResult<SupportCaseReadModel>> HandleAsync(
        Query query,
        CancellationToken cancellationToken)
    {
        var metadata = new SupportRequestMetadata(query.RequestId, query.CorrelationId);
        var access = await authorization.AuthorizeAsync(SupportCapabilities.Read, metadata, cancellationToken);
        if (!access.IsSuccess)
            return SupportOperationResult<SupportCaseReadModel>.Failure(access.Error!);
        if (!SupportValidation.IsEntityId(query.CaseId))
            return SupportOperationResult<SupportCaseReadModel>.Failure(SupportErrors.Validation(
                new Dictionary<string, string[]> { ["caseId"] = ["caseId is not a valid entity identifier."] }));

        var trusted = access.Value!.Trusted;
        // Reads are scoped to the trusted Workspace, so a foreign-workspace case is indistinguishable
        // from a missing one.
        var supportCase = await persistence.ReadCaseAsync(trusted.WorkspaceId, query.CaseId, cancellationToken);
        if (supportCase is null)
            return SupportOperationResult<SupportCaseReadModel>.Failure(SupportErrors.NotFound());

        // Record scope is enforced here, not left to the consumer. A case inside the trusted
        // Workspace but outside the caller's record scope is reported as not found, so it is
        // indistinguishable from an unknown case and from a foreign-Workspace one.
        var denied = await authorization.EnforceRecordAsync(
            access.Value!, supportCase, "getSupportCase", metadata, cancellationToken);
        if (denied is not null)
            return SupportOperationResult<SupportCaseReadModel>.Failure(denied);

        var now = timeProvider.GetUtcNow();
        persistence.AddAudit(new SupportAuditRecord(
            "getSupportCase",
            trusted.WorkspaceId,
            trusted.MemberId,
            supportCase.CaseId,
            query.RequestId,
            query.CorrelationId,
            "READ",
            supportCase.Version,
            supportCase.Version,
            now));
        await persistence.SaveChangesAsync(cancellationToken);
        return SupportOperationResult<SupportCaseReadModel>.Success(
            SupportFieldSecurity.Project(SupportProjection.Case(supportCase), access.Value!.Authorization));
    }
}
