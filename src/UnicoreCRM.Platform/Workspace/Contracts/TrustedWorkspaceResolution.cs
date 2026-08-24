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
}

public sealed class WorkspaceRequiredMetadata
{
    private WorkspaceRequiredMetadata() { }
    public static WorkspaceRequiredMetadata Instance { get; } = new();
}
