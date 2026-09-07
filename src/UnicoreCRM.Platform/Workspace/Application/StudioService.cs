using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Application.Common;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Platform.Workspace.Application;

internal sealed class StudioService(
    IStudioPersistence persistence,
    ICurrentWorkspace currentWorkspace,
    IAccessAuthorizer authorizer,
    TimeProvider timeProvider)
{
    private static readonly AccessRequirement Read = AccessRequirement.ForCanonicalCapability("studio.read");
    private static readonly AccessRequirement Configure = AccessRequirement.ForCanonicalCapability("studio.configure");

    internal async Task<WorkspaceOperationResult<StudioCoreConfigurationDocument>> GetConfigurationAsync(
        WorkspaceRequest request,
        CancellationToken cancellationToken)
    {
        var access = await AuthorizeAsync(Read, request.CorrelationId, cancellationToken);
        if (access.Error is not null) return WorkspaceOperationResult<StudioCoreConfigurationDocument>.Failure(access.Error);
        var value = await persistence.FindConfigurationAsync(access.Context!.WorkspaceId, cancellationToken);
        if (value is null) return WorkspaceOperationResult<StudioCoreConfigurationDocument>.Failure(WorkspaceErrors.ResourceNotFound("Studio configuration"));
        await persistence.RecordReadAsync("getWorkspaceConfiguration", access.Context!, request, timeProvider.GetUtcNow(), cancellationToken);
        return WorkspaceOperationResult<StudioCoreConfigurationDocument>.Success(StudioDefaults.Project(value));
    }

    internal async Task<WorkspaceOperationResult<StudioQuickSetupStateDocument>> GetQuickSetupAsync(
        WorkspaceRequest request,
        CancellationToken cancellationToken)
    {
        var access = await AuthorizeAsync(Read, request.CorrelationId, cancellationToken);
        if (access.Error is not null) return WorkspaceOperationResult<StudioQuickSetupStateDocument>.Failure(access.Error);
        var value = await persistence.FindQuickSetupAsync(access.Context!.WorkspaceId, cancellationToken);
        if (value is null) return WorkspaceOperationResult<StudioQuickSetupStateDocument>.Failure(WorkspaceErrors.ResourceNotFound("Studio Quick Setup"));
        await persistence.RecordReadAsync("getStudioQuickSetup", access.Context!, request, timeProvider.GetUtcNow(), cancellationToken);
        return WorkspaceOperationResult<StudioQuickSetupStateDocument>.Success(StudioDefaults.Project(value));
    }

    internal async Task<WorkspaceOperationResult<StudioConfigurationAuditListDocument>> ListAuditAsync(
        WorkspaceRequest request,
        CancellationToken cancellationToken)
    {
        var access = await AuthorizeAsync(Read, request.CorrelationId, cancellationToken);
        if (access.Error is not null) return WorkspaceOperationResult<StudioConfigurationAuditListDocument>.Failure(access.Error);
        var items = await persistence.ListAuditAsync(access.Context!.WorkspaceId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        await persistence.RecordReadAsync("listWorkspaceConfigurationAudit", access.Context, request, now, cancellationToken);
        return WorkspaceOperationResult<StudioConfigurationAuditListDocument>.Success(new(
            access.Context.WorkspaceId,
            items.Select(item => new StudioConfigurationAuditEntryDocument(
                item.AuditId, item.WorkspaceId, item.Revision, item.Action, item.ActorMemberId,
                item.OccurredAt, item.CorrelationId, item.Summary)).ToArray(),
            now));
    }

    internal Task<WorkspaceOperationResult<StudioConfigurationMutationResponse>> UpdateBusinessInformationAsync(
        UpdateWorkspaceBusinessInformationRequest request,
        StudioCommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        var fields = StudioValidation.Business(request);
        if (fields.Count != 0) return Task.FromResult(WorkspaceOperationResult<StudioConfigurationMutationResponse>.Failure(WorkspaceErrors.Validation(fields)));
        return MutateConfigurationAsync(new(
            StudioConfigurationChangeKind.BusinessInformation,
            "updateWorkspaceBusinessInformation", "BUSINESS_INFORMATION_UPDATED", "WORKSPACE_BUSINESS_INFORMATION_UPDATED",
            metadata.ExpectedVersion, metadata.IdempotencyKey,
            StudioJson.Fingerprint("updateWorkspaceBusinessInformation", request), metadata.CorrelationId, "",
            StudioJson.Serialize(request.BusinessInformation), StudioJson.Serialize(request.Addresses)), cancellationToken);
    }

    internal Task<WorkspaceOperationResult<StudioConfigurationMutationResponse>> UpdateLocaleRegionAsync(
        UpdateWorkspaceLocaleRegionRequest request,
        StudioCommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        var fields = StudioValidation.Locale(request);
        if (fields.Count != 0) return Task.FromResult(WorkspaceOperationResult<StudioConfigurationMutationResponse>.Failure(WorkspaceErrors.Validation(fields)));
        return MutateConfigurationAsync(new(
            StudioConfigurationChangeKind.LocaleRegion,
            "updateWorkspaceLocaleRegion", "LOCALE_REGION_UPDATED", "WORKSPACE_LOCALE_REGION_UPDATED",
            metadata.ExpectedVersion, metadata.IdempotencyKey,
            StudioJson.Fingerprint("updateWorkspaceLocaleRegion", request), metadata.CorrelationId, "",
            LocaleRegionJson: StudioJson.Serialize(request.LocaleRegion)), cancellationToken);
    }

    internal Task<WorkspaceOperationResult<StudioConfigurationMutationResponse>> UpdateBlueprintAsync(
        UpdateWorkspaceBlueprintRequest request,
        StudioCommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        var fields = StudioValidation.Blueprint(request);
        if (fields.Count != 0) return Task.FromResult(WorkspaceOperationResult<StudioConfigurationMutationResponse>.Failure(WorkspaceErrors.Validation(fields)));
        return MutateConfigurationAsync(new(
            StudioConfigurationChangeKind.Blueprint,
            "updateWorkspaceBlueprint", "BLUEPRINT_UPDATED", "WORKSPACE_BLUEPRINT_UPDATED",
            metadata.ExpectedVersion, metadata.IdempotencyKey,
            StudioJson.Fingerprint("updateWorkspaceBlueprint", request), metadata.CorrelationId, "",
            BlueprintJson: StudioJson.Serialize(request.Blueprint), FeaturesJson: StudioJson.Serialize(request.Features)), cancellationToken);
    }

    internal Task<WorkspaceOperationResult<StudioConfigurationMutationResponse>> UpdateFeaturesAsync(
        UpdateWorkspaceFeaturesRequest request,
        StudioCommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        var fields = StudioValidation.Features(request);
        if (fields.Count != 0) return Task.FromResult(WorkspaceOperationResult<StudioConfigurationMutationResponse>.Failure(WorkspaceErrors.Validation(fields)));
        return MutateConfigurationAsync(new(
            StudioConfigurationChangeKind.Features,
            "updateWorkspaceFeatures", "FEATURE_USAGE_UPDATED", "WORKSPACE_FEATURE_USAGE_UPDATED",
            metadata.ExpectedVersion, metadata.IdempotencyKey,
            StudioJson.Fingerprint("updateWorkspaceFeatures", request), metadata.CorrelationId, "",
            FeaturesJson: StudioJson.Serialize(request.Features)), cancellationToken);
    }

    internal async Task<WorkspaceOperationResult<StudioConfigurationMutationResponse>> PublishAsync(
        PublishWorkspaceConfigurationRequest request,
        StudioCommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        if (request.Note?.Length > 1000)
            return WorkspaceOperationResult<StudioConfigurationMutationResponse>.Failure(
                WorkspaceErrors.Validation(new Dictionary<string, string[]> { ["note"] = ["note must contain at most 1000 characters."] }));
        var access = await AuthorizeAsync(Configure, metadata.CorrelationId, cancellationToken);
        if (access.Error is not null) return WorkspaceOperationResult<StudioConfigurationMutationResponse>.Failure(access.Error);
        return WorkspaceOperationResult<StudioConfigurationMutationResponse>.Failure(WorkspaceErrors.LifecycleConflict(
            "Studio configuration changes are currently effective immediately; an authoritative publication transition is not available."));
    }

    internal Task<WorkspaceOperationResult<StudioQuickSetupMutationResponse>> OpenQuickSetupAsync(StudioCommandMetadata metadata, CancellationToken cancellationToken) =>
        MutateQuickSetupAsync(StudioQuickSetupChangeKind.Open, "openStudioQuickSetup", "QUICK_SETUP_OPENED", "STUDIO_QUICK_SETUP_OPENED", null, metadata, cancellationToken);

    internal Task<WorkspaceOperationResult<StudioQuickSetupMutationResponse>> DismissQuickSetupAsync(StudioCommandMetadata metadata, CancellationToken cancellationToken) =>
        MutateQuickSetupAsync(StudioQuickSetupChangeKind.Dismiss, "dismissStudioQuickSetupAutoOpen", "QUICK_SETUP_DISMISSED", "STUDIO_QUICK_SETUP_AUTO_OPEN_DISMISSED", null, metadata, cancellationToken);

    internal Task<WorkspaceOperationResult<StudioQuickSetupMutationResponse>> CompleteQuickSetupStepAsync(string stepId, StudioCommandMetadata metadata, CancellationToken cancellationToken) =>
        MutateQuickSetupAsync(StudioQuickSetupChangeKind.CompleteStep, "completeStudioQuickSetupStep", "QUICK_SETUP_STEP_COMPLETED", "STUDIO_QUICK_SETUP_STEP_COMPLETED", stepId, metadata, cancellationToken);

    internal Task<WorkspaceOperationResult<StudioQuickSetupMutationResponse>> SkipQuickSetupStepAsync(string stepId, StudioCommandMetadata metadata, CancellationToken cancellationToken) =>
        MutateQuickSetupAsync(StudioQuickSetupChangeKind.SkipStep, "skipStudioQuickSetupStep", "QUICK_SETUP_STEP_SKIPPED", "STUDIO_QUICK_SETUP_STEP_SKIPPED", stepId, metadata, cancellationToken);

    private async Task<WorkspaceOperationResult<StudioConfigurationMutationResponse>> MutateConfigurationAsync(
        StudioConfigurationChange change,
        CancellationToken cancellationToken)
    {
        var access = await AuthorizeAsync(Configure, change.CorrelationId, cancellationToken);
        if (access.Error is not null) return WorkspaceOperationResult<StudioConfigurationMutationResponse>.Failure(access.Error);
        change = change with { ActorMemberId = access.Context!.MemberId };
        var commit = await persistence.CommitConfigurationAsync(access.Context.WorkspaceId, change, timeProvider.GetUtcNow(), cancellationToken);
        return MapConfigurationCommit(commit, change.ExpectedVersion);
    }

    private async Task<WorkspaceOperationResult<StudioQuickSetupMutationResponse>> MutateQuickSetupAsync(
        StudioQuickSetupChangeKind kind,
        string operationId,
        string action,
        string eventType,
        string? stepId,
        StudioCommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        if (stepId is not null && !StudioValidation.IsStep(stepId))
            return WorkspaceOperationResult<StudioQuickSetupMutationResponse>.Failure(
                WorkspaceErrors.Validation(new Dictionary<string, string[]> { ["stepId"] = ["stepId is not a supported Quick Setup step."] }));
        var access = await AuthorizeAsync(Configure, metadata.CorrelationId, cancellationToken);
        if (access.Error is not null) return WorkspaceOperationResult<StudioQuickSetupMutationResponse>.Failure(access.Error);
        var commit = await persistence.CommitQuickSetupAsync(access.Context!.WorkspaceId, new(
            kind, operationId, action, eventType, metadata.ExpectedVersion, metadata.IdempotencyKey,
            StudioJson.Fingerprint(operationId, new { StepId = stepId }), metadata.CorrelationId,
            access.Context.MemberId, stepId), timeProvider.GetUtcNow(), cancellationToken);
        return MapQuickSetupCommit(commit, metadata.ExpectedVersion);
    }

    private async Task<(TrustedWorkspaceContext? Context, WorkspaceOperationError? Error)> AuthorizeAsync(
        AccessRequirement requirement,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (!currentWorkspace.IsResolved) return (null, WorkspaceErrors.WorkspaceMismatch());
        var decision = await authorizer.AuthorizeAsync(requirement, correlationId, cancellationToken);
        return decision.IsAllowed ? (currentWorkspace.Require(), null) : (null, WorkspaceErrors.AccessDenied());
    }

    private static WorkspaceOperationResult<StudioConfigurationMutationResponse> MapConfigurationCommit(StudioConfigurationCommit commit, long expectedVersion) => commit.Status switch
    {
        StudioCommitStatus.Committed or StudioCommitStatus.Replayed => WorkspaceOperationResult<StudioConfigurationMutationResponse>.Success(commit.Response!),
        StudioCommitStatus.VersionConflict => WorkspaceOperationResult<StudioConfigurationMutationResponse>.Failure(WorkspaceErrors.VersionConflict(expectedVersion)),
        StudioCommitStatus.IdempotencyKeyReused => WorkspaceOperationResult<StudioConfigurationMutationResponse>.Failure(WorkspaceErrors.IdempotencyKeyReused()),
        _ => WorkspaceOperationResult<StudioConfigurationMutationResponse>.Failure(WorkspaceErrors.ResourceNotFound("Studio configuration"))
    };

    private static WorkspaceOperationResult<StudioQuickSetupMutationResponse> MapQuickSetupCommit(StudioQuickSetupCommit commit, long expectedVersion) => commit.Status switch
    {
        StudioCommitStatus.Committed or StudioCommitStatus.Replayed => WorkspaceOperationResult<StudioQuickSetupMutationResponse>.Success(commit.Response!),
        StudioCommitStatus.VersionConflict => WorkspaceOperationResult<StudioQuickSetupMutationResponse>.Failure(WorkspaceErrors.VersionConflict(expectedVersion)),
        StudioCommitStatus.IdempotencyKeyReused => WorkspaceOperationResult<StudioQuickSetupMutationResponse>.Failure(WorkspaceErrors.IdempotencyKeyReused()),
        _ => WorkspaceOperationResult<StudioQuickSetupMutationResponse>.Failure(WorkspaceErrors.ResourceNotFound("Studio Quick Setup"))
    };
}

internal sealed record StudioCommandMetadata(string RequestId, string CorrelationId, string IdempotencyKey, long ExpectedVersion);
