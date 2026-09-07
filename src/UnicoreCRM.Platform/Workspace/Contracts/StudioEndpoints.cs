using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using UnicoreCRM.Platform.Workspace.Application;
using UnicoreCRM.Platform.Workspace.Application.Common;

namespace UnicoreCRM.Platform.Workspace.Contracts;

public static class StudioEndpoints
{
    public static IEndpointRouteBuilder MapStudioEndpoints(this IEndpointRouteBuilder endpoints)
    {
        MapGet(endpoints, "/workspace-configuration", GetConfigurationAsync, "getWorkspaceConfiguration");
        MapPatch(endpoints, "/workspace-configuration/business-information", UpdateBusinessInformationAsync, "updateWorkspaceBusinessInformation");
        MapPatch(endpoints, "/workspace-configuration/locale-region", UpdateLocaleRegionAsync, "updateWorkspaceLocaleRegion");
        MapPatch(endpoints, "/workspace-configuration/blueprint", UpdateBlueprintAsync, "updateWorkspaceBlueprint");
        MapPatch(endpoints, "/workspace-configuration/features", UpdateFeaturesAsync, "updateWorkspaceFeatures");
        MapPost(endpoints, "/workspace-configuration/publish", PublishAsync, "publishWorkspaceConfiguration");
        MapGet(endpoints, "/workspace-configuration/audit", ListAuditAsync, "listWorkspaceConfigurationAudit");
        MapGet(endpoints, "/studio/quick-setup", GetQuickSetupAsync, "getStudioQuickSetup");
        MapPost(endpoints, "/studio/quick-setup/open", OpenQuickSetupAsync, "openStudioQuickSetup");
        MapPost(endpoints, "/studio/quick-setup/dismiss-auto-open", DismissQuickSetupAsync, "dismissStudioQuickSetupAutoOpen");
        MapPost(endpoints, "/studio/quick-setup/steps/{stepId}/complete", CompleteQuickSetupStepAsync, "completeStudioQuickSetupStep");
        MapPost(endpoints, "/studio/quick-setup/steps/{stepId}/skip", SkipQuickSetupStepAsync, "skipStudioQuickSetupStep");
        return endpoints;
    }

    private static void MapGet(IEndpointRouteBuilder endpoints, string path, Delegate handler, string name) =>
        endpoints.MapGet(path, handler).RequireAuthorization().RequireTrustedWorkspace().WithName(name);
    private static void MapPatch(IEndpointRouteBuilder endpoints, string path, Delegate handler, string name) =>
        endpoints.MapPatch(path, handler).RequireAuthorization().RequireTrustedWorkspace().WithName(name);
    private static void MapPost(IEndpointRouteBuilder endpoints, string path, Delegate handler, string name) =>
        endpoints.MapPost(path, handler).RequireAuthorization().RequireTrustedWorkspace().WithName(name);

    private static async Task<IResult> GetConfigurationAsync(HttpContext context, StudioService service, CancellationToken cancellationToken)
    {
        if (!TryReadMetadata(context, out var request, out var error)) return error!;
        var result = await service.GetConfigurationAsync(request!, cancellationToken);
        return ConfigurationResult(context, result, request!.CorrelationId);
    }

    private static async Task<IResult> GetQuickSetupAsync(HttpContext context, StudioService service, CancellationToken cancellationToken)
    {
        if (!TryReadMetadata(context, out var request, out var error)) return error!;
        var result = await service.GetQuickSetupAsync(request!, cancellationToken);
        if (result.IsSuccess) SetEtag(context, result.Value!.Revision);
        return Result(result, request!.CorrelationId);
    }

    private static async Task<IResult> ListAuditAsync(HttpContext context, StudioService service, CancellationToken cancellationToken)
    {
        if (!TryReadMetadata(context, out var request, out var error)) return error!;
        return Result(await service.ListAuditAsync(request!, cancellationToken), request!.CorrelationId);
    }

    private static async Task<IResult> UpdateBusinessInformationAsync(UpdateWorkspaceBusinessInformationRequest body, HttpContext context, StudioService service, CancellationToken cancellationToken)
    {
        if (!TryCommandMetadata(context, out var metadata, out var error)) return error!;
        return ConfigurationResult(context, await service.UpdateBusinessInformationAsync(body, metadata!, cancellationToken), metadata!.CorrelationId);
    }

    private static async Task<IResult> UpdateLocaleRegionAsync(UpdateWorkspaceLocaleRegionRequest body, HttpContext context, StudioService service, CancellationToken cancellationToken)
    {
        if (!TryCommandMetadata(context, out var metadata, out var error)) return error!;
        return ConfigurationResult(context, await service.UpdateLocaleRegionAsync(body, metadata!, cancellationToken), metadata!.CorrelationId);
    }

    private static async Task<IResult> UpdateBlueprintAsync(UpdateWorkspaceBlueprintRequest body, HttpContext context, StudioService service, CancellationToken cancellationToken)
    {
        if (!TryCommandMetadata(context, out var metadata, out var error)) return error!;
        return ConfigurationResult(context, await service.UpdateBlueprintAsync(body, metadata!, cancellationToken), metadata!.CorrelationId);
    }

    private static async Task<IResult> UpdateFeaturesAsync(UpdateWorkspaceFeaturesRequest body, HttpContext context, StudioService service, CancellationToken cancellationToken)
    {
        if (!TryCommandMetadata(context, out var metadata, out var error)) return error!;
        return ConfigurationResult(context, await service.UpdateFeaturesAsync(body, metadata!, cancellationToken), metadata!.CorrelationId);
    }

    private static async Task<IResult> PublishAsync(PublishWorkspaceConfigurationRequest body, HttpContext context, StudioService service, CancellationToken cancellationToken)
    {
        if (!TryCommandMetadata(context, out var metadata, out var error)) return error!;
        return ConfigurationResult(context, await service.PublishAsync(body, metadata!, cancellationToken), metadata!.CorrelationId);
    }

    private static async Task<IResult> OpenQuickSetupAsync(EmptyCommandRequest body, HttpContext context, StudioService service, CancellationToken cancellationToken)
    {
        if (!TryCommandMetadata(context, out var metadata, out var error)) return error!;
        return QuickSetupResult(context, await service.OpenQuickSetupAsync(metadata!, cancellationToken), metadata!.CorrelationId);
    }

    private static async Task<IResult> DismissQuickSetupAsync(EmptyCommandRequest body, HttpContext context, StudioService service, CancellationToken cancellationToken)
    {
        if (!TryCommandMetadata(context, out var metadata, out var error)) return error!;
        return QuickSetupResult(context, await service.DismissQuickSetupAsync(metadata!, cancellationToken), metadata!.CorrelationId);
    }

    private static async Task<IResult> CompleteQuickSetupStepAsync(string stepId, EmptyCommandRequest body, HttpContext context, StudioService service, CancellationToken cancellationToken)
    {
        if (!TryCommandMetadata(context, out var metadata, out var error)) return error!;
        return QuickSetupResult(context, await service.CompleteQuickSetupStepAsync(stepId, metadata!, cancellationToken), metadata!.CorrelationId);
    }

    private static async Task<IResult> SkipQuickSetupStepAsync(string stepId, EmptyCommandRequest body, HttpContext context, StudioService service, CancellationToken cancellationToken)
    {
        if (!TryCommandMetadata(context, out var metadata, out var error)) return error!;
        return QuickSetupResult(context, await service.SkipQuickSetupStepAsync(stepId, metadata!, cancellationToken), metadata!.CorrelationId);
    }

    private static IResult ConfigurationResult(HttpContext context, WorkspaceOperationResult<StudioCoreConfigurationDocument> result, string correlationId)
    {
        if (result.IsSuccess) SetEtag(context, result.Value!.Revision);
        return Result(result, correlationId);
    }

    private static IResult ConfigurationResult(HttpContext context, WorkspaceOperationResult<StudioConfigurationMutationResponse> result, string correlationId)
    {
        if (result.IsSuccess) SetEtag(context, result.Value!.Version);
        return Result(result, correlationId);
    }

    private static IResult QuickSetupResult(HttpContext context, WorkspaceOperationResult<StudioQuickSetupMutationResponse> result, string correlationId)
    {
        if (result.IsSuccess) SetEtag(context, result.Value!.Version);
        return Result(result, correlationId);
    }

    private static IResult Result<T>(WorkspaceOperationResult<T> result, string correlationId) =>
        result.IsSuccess ? Results.Json(result.Value) : WorkspaceHttp.Error(result.Error!, correlationId);

    private static bool TryReadMetadata(HttpContext context, out WorkspaceRequest? request, out IResult? error)
    {
        return WorkspaceHttp.TryRequest(context, out request, out error);
    }

    private static bool TryCommandMetadata(HttpContext context, out StudioCommandMetadata? metadata, out IResult? error)
    {
        metadata = null;
        if (!WorkspaceHttp.TryRequest(context, out var request, out error)) return false;
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString();
        if (idempotencyKey.Length is < 8 or > 128)
            fields["Idempotency-Key"] = ["Idempotency-Key must contain between 8 and 128 characters."];
        var ifMatch = context.Request.Headers["If-Match"].ToString();
        var expectedVersion = -1L;
        if (ifMatch.Length < 3 || ifMatch[0] != '"' || ifMatch[^1] != '"'
            || !long.TryParse(ifMatch[1..^1], NumberStyles.None, CultureInfo.InvariantCulture, out expectedVersion)
            || expectedVersion < 0)
            fields["If-Match"] = ["If-Match must contain a quoted non-negative resource version."];
        if (fields.Count != 0)
        {
            error = WorkspaceHttp.Error(WorkspaceErrors.Validation(fields), request!.CorrelationId);
            return false;
        }
        metadata = new(request!.RequestId, request.CorrelationId, idempotencyKey, expectedVersion);
        return true;
    }

    private static void SetEtag(HttpContext context, long version) =>
        context.Response.Headers.ETag = $"\"{version.ToString(CultureInfo.InvariantCulture)}\"";
}
