using UnicoreCRM.Platform.AccessControl.Application.AccessDirectory;
using UnicoreCRM.Platform.AccessControl.Application.Common;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.AccessControl.Domain;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Platform.AccessControl.Application.GetWorkspaceAccessDirectory;

internal sealed class Handler(
    IAccessContextAuthorizer authorizer,
    ICurrentWorkspace currentWorkspace,
    DirectoryComposer composer,
    IAccessDirectoryPersistence persistence,
    TimeProvider timeProvider)
{
    internal async Task<AccessOperationResult<WorkspaceAccessDirectoryDocument>> HandleAsync(
        Query query,
        CancellationToken cancellationToken)
    {
        var authorization = await authorizer.AuthorizeWithContextAsync(
            AccessCapabilities.AccessRead,
            query.CorrelationId,
            cancellationToken);
        if (!authorization.IsAllowed)
        {
            return AccessOperationResult<WorkspaceAccessDirectoryDocument>.Failure(
                authorization.Code == "WORKSPACE_MISMATCH" ? AccessErrors.WorkspaceMismatch() : AccessErrors.AccessDenied());
        }

        var metadataErrors = MetadataErrors(query);
        if (metadataErrors.Count != 0)
            return AccessOperationResult<WorkspaceAccessDirectoryDocument>.Failure(AccessErrors.Validation(metadataErrors));

        var trusted = currentWorkspace.Require();
        var directory = await composer.ComposeAsync(trusted.WorkspaceId, cancellationToken);
        if (!directory.IsSuccess)
            return AccessOperationResult<WorkspaceAccessDirectoryDocument>.Failure(directory.Error!);

        await persistence.AppendReadEvidenceAsync(
            new AccessDirectoryReadEvidence(
                trusted.WorkspaceId,
                trusted.AccountId,
                trusted.MembershipId,
                trusted.MemberId,
                query.RequestId,
                query.CorrelationId,
                timeProvider.GetUtcNow()),
            cancellationToken);
        return directory;
    }

    private static IReadOnlyDictionary<string, string[]> MetadataErrors(Query query)
    {
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (query.RequestId.Length is < 8 or > 128)
            fields["X-Request-Id"] = ["X-Request-Id must contain between 8 and 128 characters."];
        if (query.SuppliedCorrelationId.Length != 0 && query.SuppliedCorrelationId.Length is < 8 or > 128)
            fields["X-Correlation-Id"] = ["X-Correlation-Id must contain between 8 and 128 characters."];
        return fields;
    }
}
