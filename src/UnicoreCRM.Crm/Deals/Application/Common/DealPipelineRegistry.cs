using System.Text.Json;
using System.Text.RegularExpressions;
using UnicoreCRM.Crm.Deals.Contracts;
using UnicoreCRM.Crm.Deals.Domain;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Deals.Application.Common;

internal sealed class DealPipelineRegistry(
    IDealsPersistence persistence, IDealPipelinePersistence pipelines, ICurrentWorkspace currentWorkspace,
    IAccessAuthorizer authorizer, TimeProvider clock) : IDealPipelineProvisioning
{
    public async Task EnsureDefaultAsync(string workspaceId, string actorMemberId, CancellationToken cancellationToken)
    {
        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);
        await pipelines.LockRegistryAsync(workspaceId, cancellationToken, exclusive: true);
        await EnsureWithinTransactionAsync(workspaceId, actorMemberId, cancellationToken);
        await persistence.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<DealPipeline>> EnsureWithinTransactionAsync(string workspaceId, string actor, CancellationToken token, bool tracking = false)
    {
        var items = await pipelines.LoadPipelinesAsync(workspaceId, token, tracking);
        if (items.Count != 0) return items;
        var value = DealPipeline.Default(workspaceId, actor, clock.GetUtcNow());
        pipelines.AddPipeline(value);
        return [value];
    }

    internal async Task<DealOperationResult<DealPipelineListResponse>> ReadAsync(DealRequestMetadata metadata, CancellationToken token)
    {
        var access = await AuthorizeAsync(false, metadata, token);
        if (access.Error is not null) return DealOperationResult<DealPipelineListResponse>.Failure(access.Error);
        await using var transaction = await persistence.BeginSerializableAsync(token);
        await pipelines.LockRegistryAsync(access.Trusted!.WorkspaceId, token, exclusive: true);
        var items = await EnsureWithinTransactionAsync(access.Trusted.WorkspaceId, access.Trusted.MemberId, token);
        persistence.AddAudit(new DealAuditRecord("listCrmPipelines", access.Trusted.WorkspaceId,
            access.Trusted.MemberId, null, metadata.RequestId, metadata.CorrelationId, "READ", null, null, clock.GetUtcNow()));
        await persistence.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return DealOperationResult<DealPipelineListResponse>.Success(new(items.OrderByDescending(item => item.IsDefault)
            .ThenBy(item => item.PipelineId, StringComparer.Ordinal).Select(Project).ToArray()));
    }

    // Called only within the Deal transaction, under its shared registry lock, after replay.
    internal async Task<(DealPipeline? Pipeline, DealPipelineStage? Stage, DealOperationError? Error)> ReadInitialStageForCreateAsync(
        TrustedWorkspaceContext trusted, CancellationToken token)
    {
        await EnsureDefaultAsync(trusted.WorkspaceId, trusted.MemberId, token);
        await using var transaction = await persistence.BeginSerializableAsync(token);
        await pipelines.LockRegistryAsync(trusted.WorkspaceId, token);
        return await SelectAsync(trusted.WorkspaceId, null, null, token);
    }

    // Called only within the Deal transaction, under its shared registry lock, after replay.
    internal async Task<(DealPipeline? Pipeline, DealPipelineStage? Stage, DealOperationError? Error)> SelectAsync(
        string workspaceId, string? pipelineId, string? stageCode, CancellationToken token, bool requireActive = true)
    {
        var items = await pipelines.LoadPipelinesAsync(workspaceId, token);
        var pipeline = pipelineId is null ? items.SingleOrDefault(item => item.IsDefault && item.IsActive)
            : items.SingleOrDefault(item => item.PipelineId == pipelineId);
        if (pipeline is null || (requireActive && !pipeline.IsActive))
            return (null, null, new("DEAL_PIPELINE_NOT_FOUND", 422, "Pipeline is not selectable"));
        var stage = stageCode is null ? pipeline.Stages.Where(item => item.IsActive && item.Category == DealStageCategory.Open)
            .OrderBy(item => item.Order).FirstOrDefault() : pipeline.Stages.SingleOrDefault(item => item.Code == stageCode);
        if (stage is null) return (pipeline, null, DealErrors.StageNotFound(stageCode ?? ""));
        if (stage.Category != DealStageCategory.Open && requireActive)
            return (pipeline, stage, DealErrors.TerminalStageRequiresOutcome());
        if (requireActive && !stage.IsActive)
            return (pipeline, stage, new("DEAL_STAGE_INACTIVE", 422, "Stage is inactive"));
        return (pipeline, stage, null);
    }

    internal async Task<DealOperationResult<DealPipelineMutationResponse>> SaveAsync(string? pipelineId,
        SaveDealPipelineRequest request, DealCommandMetadata metadata, CancellationToken token)
    {
        var access = await AuthorizeAsync(true, new(metadata.RequestId, metadata.CorrelationId), token);
        if (access.Error is not null) return DealOperationResult<DealPipelineMutationResponse>.Failure(access.Error);
        var trusted = access.Trusted!;
        var operation = pipelineId is null ? "createCrmPipeline" : "updateCrmPipeline";
        var fingerprint = DealCommandSupport.Fingerprint(new { pipelineId, request, metadata.ExpectedVersion });
        var scope = DealCommandSupport.ScopeKey(trusted, operation, pipelineId ?? "WORKSPACE", metadata.IdempotencyKey);
        await using var transaction = await persistence.BeginSerializableAsync(token);
        await pipelines.LockRegistryAsync(trusted.WorkspaceId, token, exclusive: true);
        var existing = await persistence.FindIdempotencyAsync(scope, token);
        if (existing is not null)
        {
            var replayError = DealCommandSupport.ReplayError(existing, fingerprint);
            return replayError is not null ? DealOperationResult<DealPipelineMutationResponse>.Failure(replayError)
                : DealOperationResult<DealPipelineMutationResponse>.Success(
                    (JsonSerializer.Deserialize<DealPipelineMutationResponse>(existing.ResponseJson, DealCommandSupport.SerializationOptions)
                    ?? throw new InvalidOperationException("Invalid pipeline replay evidence")) with { Outcome = "REPLAYED" });
        }
        var items = await EnsureWithinTransactionAsync(trusted.WorkspaceId, trusted.MemberId, token, tracking: true);
        var target = pipelineId is null ? null : items.SingleOrDefault(item => item.PipelineId == pipelineId);
        if (pipelineId is not null && target is null) return DealOperationResult<DealPipelineMutationResponse>.Failure(DealErrors.NotFound());
        if (target is not null && target.Version != metadata.ExpectedVersion)
            return DealOperationResult<DealPipelineMutationResponse>.Failure(DealErrors.VersionConflict(target.PipelineId, metadata.ExpectedVersion!.Value, target.Version));
        var fields = Validate(request, target);
        if (fields.Count != 0) return DealOperationResult<DealPipelineMutationResponse>.Failure(DealErrors.Validation(fields));
        var now = clock.GetUtcNow();
        var priorVersion = target?.Version;
        if (target is not null)
        {
            if (target.IsDefault && (!request.IsDefault || !request.IsActive))
                return DealOperationResult<DealPipelineMutationResponse>.Failure(new("PIPELINE_DEFAULT_REQUIRED", 409, "Select another default pipeline first"));
            if (!request.IsActive && await pipelines.CountStageUsageAsync(trusted.WorkspaceId, target.PipelineId, null, token) > 0)
                return DealOperationResult<DealPipelineMutationResponse>.Failure(new("PIPELINE_IN_USE", 409, "Pipeline is used by Deals"));
            foreach (var old in target.Stages.ToArray())
            {
                var next = request.Stages!.SingleOrDefault(item => item.Id == old.StageId);
                if (next is null || (old.IsActive && !next.IsActive))
                {
                    var count = await pipelines.CountStageUsageAsync(trusted.WorkspaceId, target.PipelineId, old.Code, token);
                    if (count > 0) return DealOperationResult<DealPipelineMutationResponse>.Failure(new("PIPELINE_STAGE_IN_USE", 409,
                        "Stage is used by Deals", FieldErrors: new Dictionary<string, string[]> { ["stages"] = [$"{old.Code}: {count}"] }));
                }
                if (next is null) target.Stages.Remove(old);
            }
            foreach (var stage in request.Stages!)
            {
                var old = target.Stages.SingleOrDefault(item => item.StageId == stage.Id);
                if (old is null) target.Stages.Add(NewStage(stage));
                else old.Configure(stage.LabelVi!.Trim(), stage.LabelEn!.Trim(), stage.Order, stage.DefaultProbability, stage.Color!, stage.IsActive);
            }
            target.Replace(request.NameVi!.Trim(), request.NameEn!.Trim(), request.Description?.Trim() ?? "", request.IsActive, trusted.MemberId, now);
        }
        else
        {
            if (items.Count >= 50) return DealOperationResult<DealPipelineMutationResponse>.Failure(new("PIPELINE_LIMIT_REACHED", 422, "At most 50 pipelines are supported"));
            target = new DealPipeline(trusted.WorkspaceId, DealIds.New("pipeline"), request.NameVi!.Trim(), request.NameEn!.Trim(),
                request.Description?.Trim() ?? "", false, request.IsActive, request.Stages!.Select(NewStage), trusted.MemberId, now);
            pipelines.AddPipeline(target);
        }
        if (request.IsDefault && !target.IsDefault)
        {
            var previous = items.Single(item => item.IsDefault);
            var before = previous.Version;
            previous.SetDefault(false, trusted.MemberId, now);
            // Flush the demotion first to preserve the filtered unique index throughout the transaction.
            AddAudit(previous, "pipelineDefaultDemoted", before);
            await persistence.SaveChangesAsync(token);
            target.SetDefault(true, trusted.MemberId, now);
        }
        var audit = AddAudit(target, operation, priorVersion);
        var message = new DealOutboxMessage("DEAL_PIPELINE_CONFIGURED", target.PipelineId, trusted.WorkspaceId,
            metadata.CorrelationId, JsonSerializer.Serialize(new { pipelineId = target.PipelineId, version = target.Version }), now);
        persistence.AddOutbox(message);
        var response = new DealPipelineMutationResponse("COMMITTED", Project(target), [audit.AuditId]);
        persistence.AddIdempotency(new(scope, trusted.WorkspaceId, operation, trusted.MemberId, pipelineId ?? "WORKSPACE",
            metadata.IdempotencyKey, fingerprint, JsonSerializer.Serialize(response, DealCommandSupport.SerializationOptions), now));
        await persistence.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return DealOperationResult<DealPipelineMutationResponse>.Success(response);

        DealAuditRecord AddAudit(DealPipeline value, string op, long? before)
        {
            var evidence = new DealAuditRecord(op, trusted.WorkspaceId, trusted.MemberId, value.PipelineId,
                metadata.RequestId, metadata.CorrelationId, "COMMITTED", before, value.Version, now);
            persistence.AddAudit(evidence); return evidence;
        }
    }

    internal static IReadOnlyDictionary<string, string[]> Validate(SaveDealPipelineRequest request, DealPipeline? existing)
    {
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        DealValidation.RequiredText(request.NameVi, "nameVi", 200, fields);
        DealValidation.RequiredText(request.NameEn, "nameEn", 200, fields);
        DealValidation.OptionalText(request.Description, "description", 2000, fields);
        if (request.IsDefault && !request.IsActive) fields["isDefault"] = ["Default pipeline must be active."];
        if (request.Stages is null || request.Stages.Count is < 3 or > 50)
        { fields["stages"] = ["Supply between 3 and 50 stages."]; return fields; }
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var orders = new HashSet<int>();
        foreach (var stage in request.Stages)
        {
            if (stage is null) { fields["stages"] = ["Stage is required."]; continue; }
            var key = "stages." + stage.Id;
            if (!DealValidation.IsEntityId(stage.Id) || !ids.Add(stage.Id!)) fields[key + ".id"] = ["Unique stable stage ID required."];
            if (stage.Code is null || !Regex.IsMatch(stage.Code, "^[A-Z][A-Z0-9_]{0,119}$", RegexOptions.CultureInvariant) || !codes.Add(stage.Code))
                fields[key + ".code"] = ["Unique uppercase stage code required."];
            DealValidation.RequiredText(stage.LabelVi, key + ".labelVi", 200, fields);
            DealValidation.RequiredText(stage.LabelEn, key + ".labelEn", 200, fields);
            if (stage.Order < 0 || !orders.Add(stage.Order)) fields[key + ".order"] = ["Unique nonnegative order required."];
            if (stage.DefaultProbability is < 0 or > 100) fields[key + ".defaultProbability"] = ["Probability must be 0–100."];
            if (stage.Color is not ("blue" or "yellow" or "orange" or "purple" or "green" or "red" or "slate" or "teal")) fields[key + ".color"] = ["Invalid color."];
            if (stage.Category is not ("OPEN" or "WON" or "LOST")) fields[key + ".category"] = ["Invalid category."];
            if ((stage.Code == "WON") != (stage.Category == "WON") || (stage.Code == "LOST") != (stage.Category == "LOST")) fields[key + ".category"] = ["Terminal codes and categories are immutable."];
            var old = existing?.Stages.SingleOrDefault(item => item.StageId == stage.Id);
            if (old is not null && (old.Code != stage.Code || DealProjectionCategory(old.Category) != stage.Category)) fields[key + ".code"] = ["Existing code and category cannot change."];
            if (old is null && existing?.Stages.Any(item => item.Code == stage.Code) == true) fields[key + ".id"] = ["Preserve existing stage identity."];
        }
        if (!request.Stages.Any(item => item is { IsActive: true, Category: "OPEN" })) fields["stages.open"] = ["At least one active OPEN stage required."];
        foreach (var code in new[] { "WON", "LOST" })
        {
            var terminal = request.Stages.Where(item => item is not null && item.Code == code && item.Category == code && item.IsActive).ToArray();
            if (terminal.Length != 1 || (existing is not null && !terminal.Any(next => existing.Stages.Any(old => old.StageId == next.Id && old.Code == code))))
                fields["stages." + code] = ["Preserve the active terminal outcome and its identity."];
        }
        return fields;
    }

    private static DealPipelineStage NewStage(DealPipelineStageInput value) => new(value.Id!, value.Code!, value.LabelVi!.Trim(),
        value.LabelEn!.Trim(), value.Order, Enum.Parse<DealStageCategory>(value.Category!, true), value.DefaultProbability,
        value.Color!, value.IsActive, value.Category != "OPEN");
    private async Task<(TrustedWorkspaceContext? Trusted, DealOperationError? Error)> AuthorizeAsync(bool write, DealRequestMetadata metadata, CancellationToken token)
    {
        if (!currentWorkspace.IsResolved) return (null, DealErrors.WorkspaceMismatch());
        var decision = await authorizer.AuthorizeAsync(AccessRequirement.ForCanonicalCapability(write ? "studio.configure" : "studio.read"), metadata.CorrelationId, token);
        if (!decision.IsAllowed && !write) decision = await authorizer.AuthorizeAsync(DealCapabilities.Read, metadata.CorrelationId, token);
        return decision.IsAllowed ? (currentWorkspace.Require(), null) : (null, DealErrors.AccessDenied());
    }
    private static string DealProjectionCategory(DealStageCategory value) => value.ToString().ToUpperInvariant();
    internal static DealPipelineDocument Project(DealPipeline value) => new(value.PipelineId, value.WorkspaceId, value.NameVi,
        value.NameEn, value.Description, value.IsDefault, value.IsActive, value.Version, DealProjection.Utc(value.CreatedAt),
        DealProjection.Utc(value.UpdatedAt), value.UpdatedBy, value.Stages.OrderBy(item => item.Order).Select(item =>
            new DealPipelineStageDocument(item.StageId, item.Code, item.LabelVi, item.LabelEn, item.Order,
                DealProjectionCategory(item.Category), item.DefaultProbability, item.Color, item.IsActive, item.IsSystem,
                DealProjection.ForecastCategory(item.ForecastFloor), item.RequiresPositiveAmount)).ToArray());
}
