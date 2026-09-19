namespace UnicoreCRM.PlatformOperations.AiExecution.Contracts;

public static class AiConfigurationValues
{
    public const string Unconfigured = "UNCONFIGURED";
    public const string Draft = "DRAFT";
    public const string Active = "ACTIVE";
    public const string Disabled = "DISABLED";
    public const string WorkspaceCredential = "WORKSPACE";
    public const string DeploymentCredential = "DEPLOYMENT";
}

public sealed record WorkspaceAiConfigurationState(
    string WorkspaceId,
    string Status,
    string PrimaryProvider,
    string PrimaryModel,
    string PrimaryCredentialSource,
    bool PrimaryCredentialConfigured,
    bool FallbackEnabled,
    string? FallbackProvider,
    string? FallbackModel,
    string? FallbackCredentialSource,
    bool FallbackCredentialConfigured,
    bool RetryRateLimited,
    long Version,
    bool IsValidated,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ActivatedAt,
    WorkspaceAiActiveConfigurationState? ActiveConfiguration);

public sealed record WorkspaceAiActiveConfigurationState(
    string PrimaryProvider,
    string PrimaryModel,
    string PrimaryCredentialSource,
    bool PrimaryCredentialConfigured,
    bool FallbackEnabled,
    string? FallbackProvider,
    string? FallbackModel,
    string? FallbackCredentialSource,
    bool FallbackCredentialConfigured,
    bool RetryRateLimited,
    DateTimeOffset ActivatedAt);

public sealed record WorkspaceAiConfigurationDraft(
    string PrimaryProvider,
    string PrimaryModel,
    string PrimaryCredentialSource,
    bool FallbackEnabled,
    string? FallbackProvider,
    string? FallbackModel,
    string? FallbackCredentialSource,
    bool RetryRateLimited);

public sealed record ActiveWorkspaceAiPolicy(WorkspaceAiConfigurationDraft Policy, string? PrimaryProtectedCredential, string? FallbackProtectedCredential);

public enum AiConfigurationCommitStatus { Committed, Replayed, VersionConflict, IdempotencyKeyReused }

public sealed record AiConfigurationCommit(AiConfigurationCommitStatus Status, WorkspaceAiConfigurationState? State = null);

public interface IWorkspaceAiConfigurationStore
{
    Task<WorkspaceAiConfigurationState?> FindAsync(string workspaceId, CancellationToken cancellationToken);
    Task<AiConfigurationCommit> SaveDraftAsync(string workspaceId, string memberId, WorkspaceAiConfigurationDraft draft,
        long expectedVersion, string idempotencyKey, string fingerprint, string correlationId, DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<AiConfigurationCommit> SetCredentialAsync(string workspaceId, string memberId, bool fallback, string protectedCredential,
        long expectedVersion, string idempotencyKey, string fingerprint, string correlationId, DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<AiConfigurationCommit> SetValidationAsync(string workspaceId, string memberId, bool succeeded,
        long expectedVersion, string idempotencyKey, string fingerprint, string correlationId, DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<AiConfigurationCommit> ActivateAsync(string workspaceId, string memberId, long expectedVersion,
        string idempotencyKey, string fingerprint, string correlationId, DateTimeOffset now, CancellationToken cancellationToken);
    Task<AiConfigurationCommit> DisableAsync(string workspaceId, string memberId, long expectedVersion,
        string idempotencyKey, string fingerprint, string correlationId, DateTimeOffset now, CancellationToken cancellationToken);
    Task<string?> FindProtectedCredentialAsync(string workspaceId, bool fallback, CancellationToken cancellationToken);
    Task<ActiveWorkspaceAiPolicy?> FindActivePolicyAsync(string workspaceId, CancellationToken cancellationToken);
    Task RecordTestOutcomeAsync(string workspaceId, string memberId, bool succeeded, string provider, string model,
        string correlationId, DateTimeOffset now, CancellationToken cancellationToken);
}
