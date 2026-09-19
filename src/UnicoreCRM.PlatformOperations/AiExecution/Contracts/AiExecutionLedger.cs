namespace UnicoreCRM.PlatformOperations.AiExecution.Contracts;

public sealed record AiExecutionEvidence(
    string ExecutionId, string WorkspaceId, string MemberId, string Operation, string Provider, string Model,
    DateTimeOffset StartedAt, DateTimeOffset CompletedAt, string Status, long DurationMilliseconds,
    IReadOnlyList<string> ContextTypes, IReadOnlyList<string> EvidenceIdentifiers,
    int? InputTokens = null, int? OutputTokens = null, string? ProviderRequestId = null, string? ErrorCode = null);

public interface IAiExecutionLedger
{
    Task RecordAsync(AiExecutionEvidence evidence, CancellationToken cancellationToken);
    Task RecordAttemptAsync(AiProviderAttemptEvidence evidence, CancellationToken cancellationToken);
    Task<AiWorkspaceUsageSummary> ReadWorkspaceSummaryAsync(string workspaceId, DateTimeOffset since, CancellationToken cancellationToken);
}

public sealed record AiWorkspaceUsageSummary(long Executions, long SuccessfulExecutions, long FailedExecutions,
    long ProviderAttempts, long InputTokens, long OutputTokens, DateTimeOffset? LastExecutionAt, string? LastStatus);

public sealed record AiProviderAttemptEvidence(
    string AttemptId, string ExecutionId, string WorkspaceId, int AttemptNumber, string AttemptKind,
    string Provider, string Model, DateTimeOffset StartedAt, DateTimeOffset CompletedAt, long DurationMilliseconds,
    string Status, string? ProviderRequestId = null, int? InputTokens = null, int? OutputTokens = null,
    string? ErrorCategory = null, string? SafeDiagnostic = null);
