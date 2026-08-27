using UnicoreCRM.Operations.Support.Domain;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Operations.Support.Application.Common;

/// <summary>
/// The authorization result every Support use case works from: the trusted Workspace plus the
/// AccessControl decision that governs this resource for this caller.
/// </summary>
internal sealed record SupportAccess(TrustedWorkspaceContext Trusted, RecordAccessAuthorization Authorization);

/// <summary>
/// The Support application boundary of the trusted authority chain: authenticated user ->
/// requested Workspace -> verified membership -> trusted CurrentWorkspace -> capability
/// authorization -> record scope -> field security -> Support use case. A caller-supplied Workspace
/// identifier is never trusted here; only the resolved <see cref="TrustedWorkspaceContext"/> is.
///
/// <para>Everything beyond the capability check is decided by AccessControl through
/// <see cref="IRecordAccessEvaluator"/>. Support holds no scope rule and no field rule of its own:
/// duplicating either would create a second authorization authority that could drift from the one
/// the `evaluateEffectiveRecordAccess` contract reports to consumers.</para>
/// </summary>
internal sealed class SupportAuthorization(IRecordAccessEvaluator evaluator)
{
    internal const string ResourceKey = "support";

    internal async Task<SupportOperationResult<SupportAccess>> AuthorizeAsync(
        AccessRequirement requirement,
        SupportRequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        var authorization = await evaluator.AuthorizeResourceAsync(
            ResourceKey,
            requirement.Capability,
            SupportFieldSecurity.FieldKeys,
            new RecordAccessRequestContext(metadata.RequestId, metadata.CorrelationId),
            cancellationToken);

        if (authorization.TrustedWorkspace is not { } trusted)
        {
            return SupportOperationResult<SupportAccess>.Failure(
                authorization.Code == "WORKSPACE_MISMATCH" ? SupportErrors.WorkspaceMismatch() : SupportErrors.AccessDenied());
        }

        if (!authorization.IsAllowed)
            return SupportOperationResult<SupportAccess>.Failure(SupportErrors.AccessDenied());

        // A policy Support cannot honour refuses the whole operation before any record is touched.
        var unenforceable = SupportFieldSecurity.UnenforceablePolicy(authorization);
        if (unenforceable is not null)
            return SupportOperationResult<SupportAccess>.Failure(unenforceable);

        return SupportOperationResult<SupportAccess>.Success(new SupportAccess(trusted, authorization));
    }

    /// <summary>
    /// Enforces record scope against Support's own authoritative facts. The facts come from the
    /// loaded aggregate rather than the request, and a record outside scope is reported as not found
    /// so it is indistinguishable from a record that does not exist or belongs to another Workspace.
    /// </summary>
    internal async Task<SupportOperationError?> EnforceRecordAsync(
        SupportAccess access,
        SupportCase supportCase,
        string enforcementPoint,
        SupportRequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        var decision = await evaluator.AuthorizeRecordAsync(
            access.Authorization,
            supportCase.CaseId,
            RecordAccessFacts.Found(supportCase.OwnerId),
            enforcementPoint,
            new RecordAccessRequestContext(metadata.RequestId, metadata.CorrelationId),
            cancellationToken);
        return decision.IsAllowed ? null : SupportErrors.NotFound();
    }
}
