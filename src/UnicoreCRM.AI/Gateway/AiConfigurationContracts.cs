namespace UnicoreCRM.AI.Gateway;

public sealed record AiProviderModelView(string Id, string DisplayName, bool StructuredOutput);
public sealed record AiProviderCatalogEntry(string Id, string DisplayName, bool DeploymentCredentialAvailable, IReadOnlyList<AiProviderModelView> Models);
public sealed record AiProviderCatalogResponse(IReadOnlyList<AiProviderCatalogEntry> Providers);

public sealed record AiPendingConfigurationView(
    string Status, string PrimaryProvider, string PrimaryModel, string PrimaryCredentialSource, bool PrimaryCredentialConfigured,
    bool FallbackEnabled, string? FallbackProvider, string? FallbackModel, string? FallbackCredentialSource,
    bool FallbackCredentialConfigured, bool RetryRateLimited, bool IsValidated);

public sealed record AiConfigurationView(
    string Status, string PrimaryProvider, string PrimaryModel, string PrimaryCredentialSource, bool PrimaryCredentialConfigured,
    bool FallbackEnabled, string? FallbackProvider, string? FallbackModel, string? FallbackCredentialSource,
    bool FallbackCredentialConfigured, bool RetryRateLimited, bool IsValidated, long Version,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? ActivatedAt,
    AiPendingConfigurationView? PendingDraft);

public sealed record SaveAiConfigurationRequest(
    string? PrimaryProvider, string? PrimaryModel, string? PrimaryCredentialSource, bool FallbackEnabled,
    string? FallbackProvider, string? FallbackModel, string? FallbackCredentialSource, bool RetryRateLimited);
public sealed record SetAiCredentialRequest(string? Credential, bool Fallback = false);
public sealed record AiConfigurationMutationResponse(AiConfigurationView Configuration);
public sealed record AiConfigurationTestResponse(bool Succeeded, string Provider, string Model, string Status, AiConfigurationView Configuration);
public sealed record AiUsageSummaryResponse(long Executions, long SuccessfulExecutions, long FailedExecutions, long ProviderAttempts,
    long InputTokens, long OutputTokens, DateTimeOffset WindowStartedAt, DateTimeOffset? LastExecutionAt, string? LastStatus);
