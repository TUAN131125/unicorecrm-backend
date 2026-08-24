namespace UnicoreCRM.Workflows.Durable.Contracts;

/// <summary>
/// The single authenticated Initial Workspace Provisioning intent. Every value is optional:
/// omitting a value - the explicit Skip path - selects the documented server-owned default.
/// The caller cannot supply an account, member, membership status, Workspace key, aggregate
/// identifier, role, capability, enabled module or product space.
/// </summary>
public sealed record ProvisionInitialWorkspaceRequest
{
    public string? Name { get; init; }
    public string? LogoText { get; init; }
    public string? Locale { get; init; }
    public string? TimeZone { get; init; }
    public string? BaseCurrency { get; init; }
}

/// <param name="Outcome"><c>PROVISIONED</c> when this call created the Workspace, <c>REPLAYED</c> when it converged on the existing one.</param>
public sealed record ProvisionInitialWorkspaceResponse(
    string CommandId,
    string CorrelationId,
    string Outcome,
    string WorkspaceId,
    string MembershipId,
    UnicoreCRM.Platform.Workspace.Contracts.WorkspaceMembershipSummary Workspace,
    DateTimeOffset ProvisionedAt);

public sealed record DurableWorkflowProblemDetails(
    string Type,
    string Title,
    int Status,
    string Code,
    bool Retryable,
    string CorrelationId,
    string? Detail = null,
    string? Instance = null,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null,
    string? IdempotencyKey = null);
