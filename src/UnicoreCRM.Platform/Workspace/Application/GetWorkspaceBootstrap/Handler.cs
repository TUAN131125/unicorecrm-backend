using System.Text.Json;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Application.Common;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Platform.Workspace.Domain;

namespace UnicoreCRM.Platform.Workspace.Application.GetWorkspaceBootstrap;

internal sealed class Handler(
    IWorkspacePersistence persistence,
    ITrustedWorkspaceSetter trustedWorkspaceSetter,
    IAccessAuthorizer accessAuthorizer,
    TimeProvider timeProvider)
{
    internal async Task<WorkspaceOperationResult<WorkspaceBootstrapDocument>> HandleAsync(
        Query query,
        CancellationToken cancellationToken)
    {
        var bootstrap = await persistence.FindActiveBootstrapAsync(
            query.AccountId,
            query.MemberId,
            query.WorkspaceId,
            cancellationToken);
        if (bootstrap is null)
            return WorkspaceOperationResult<WorkspaceBootstrapDocument>.Failure(WorkspaceErrors.AccessDenied());

        trustedWorkspaceSetter.Set(new TrustedWorkspaceContext(
            bootstrap.Workspace.WorkspaceId,
            query.AccountId,
            query.MemberId,
            bootstrap.Workspace.MembershipId));
        var accessDecision = await accessAuthorizer.AuthorizeAsync(
            AccessCapabilities.WorkspaceContextResolve,
            query.CorrelationId,
            cancellationToken);
        if (!accessDecision.IsAllowed || accessDecision.Context is not { } authorizationContext)
            return WorkspaceOperationResult<WorkspaceBootstrapDocument>.Failure(WorkspaceErrors.AccessDenied());

        var now = timeProvider.GetUtcNow();
        var document = new WorkspaceBootstrapDocument(
            WorkspaceProjection.Membership(bootstrap.Workspace),
            bootstrap.ContextVersion,
            authorizationContext.Capabilities,
            new WorkspaceRuntimeConfiguration(
                bootstrap.ConfigurationVersion,
                bootstrap.Locale,
                bootstrap.TimeZone,
                bootstrap.BaseCurrency,
                Deserialize(bootstrap.EnabledModuleKeysJson),
                Deserialize(bootstrap.AvailableProductSpacesJson)),
            now);
        persistence.AddAccessRecord(new WorkspaceAccessRecord("getWorkspaceBootstrap", query.AccountId, query.WorkspaceId, query.CorrelationId, now));
        await persistence.SaveChangesAsync(cancellationToken);
        return WorkspaceOperationResult<WorkspaceBootstrapDocument>.Success(document);
    }

    private static IReadOnlyList<string> Deserialize(string json) =>
        JsonSerializer.Deserialize<string[]>(json) ?? [];
}
