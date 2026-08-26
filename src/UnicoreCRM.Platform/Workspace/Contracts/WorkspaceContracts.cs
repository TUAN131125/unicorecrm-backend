namespace UnicoreCRM.Platform.Workspace.Contracts;

public sealed record WorkspaceMembershipSummary(
    string MembershipId,
    string WorkspaceId,
    string WorkspaceKey,
    string Name,
    string Status,
    string LogoText);

public sealed record WorkspaceMembershipListResponse(
    IReadOnlyList<WorkspaceMembershipSummary> Items,
    DateTimeOffset GeneratedAt);

public sealed record WorkspaceRuntimeConfiguration(
    long ConfigurationVersion,
    string Locale,
    string TimeZone,
    string BaseCurrency,
    IReadOnlyList<string> EnabledModuleKeys,
    IReadOnlyList<string> AvailableProductSpaces);

public sealed record WorkspaceBootstrapDocument(
    WorkspaceMembershipSummary Workspace,
    long ContextVersion,
    IReadOnlyList<string> Capabilities,
    WorkspaceRuntimeConfiguration Configuration,
    DateTimeOffset ResolvedAt);

public sealed record WorkspaceCurrencyConfiguration(
    string BaseCurrency,
    long ConfigurationVersion);

public interface IWorkspaceCurrencyConfigurationReader
{
    Task<WorkspaceCurrencyConfiguration?> FindAsync(
        string workspaceId,
        CancellationToken cancellationToken);
}

public sealed record WorkspaceProblemDetails(
    string Type,
    string Title,
    int Status,
    string Code,
    bool Retryable,
    string CorrelationId,
    string? Detail = null,
    string? Instance = null,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null);
