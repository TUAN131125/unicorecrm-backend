using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace UnicoreCRM.Platform.Workspace.Contracts;

public static class TrustedWorkspaceResolution
{
    public static IApplicationBuilder UseTrustedWorkspaceResolution(this IApplicationBuilder application) =>
        application.UseMiddleware<TrustedWorkspaceMiddleware>();

    public static TBuilder RequireTrustedWorkspace<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        builder.Add(endpoint => endpoint.Metadata.Add(WorkspaceRequiredMetadata.Instance));
        return builder;
    }

    public static TBuilder RequireTrustedWorkspaceWithDeferredRequestMetadata<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.Add(endpoint => endpoint.Metadata.Add(WorkspaceRequiredMetadata.DeferredRequestMetadata));
        return builder;
    }
}

public sealed class WorkspaceRequiredMetadata
{
    private WorkspaceRequiredMetadata(bool validateRequestMetadata) => ValidateRequestMetadata = validateRequestMetadata;

    public bool ValidateRequestMetadata { get; }
    public static WorkspaceRequiredMetadata Instance { get; } = new(true);
    public static WorkspaceRequiredMetadata DeferredRequestMetadata { get; } = new(false);
}
