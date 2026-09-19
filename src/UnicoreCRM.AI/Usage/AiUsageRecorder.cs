using Microsoft.Extensions.Logging;
using UnicoreCRM.PlatformOperations.AiExecution.Contracts;

namespace UnicoreCRM.AI.Usage;

internal sealed record AiUsageEvent(
    string ExecutionId,
    string WorkspaceId,
    string MemberId,
    string Provider,
    string Model,
    string Operation,
    IReadOnlyList<string> ToolNames,
    IReadOnlyList<string> ContextFields,
    string Status,
    TimeSpan Duration,
    DateTimeOffset StartedAt,
    IReadOnlyList<string> EvidenceIdentifiers,
    int? InputTokens = null,
    int? OutputTokens = null,
    string? ProviderRequestId = null);

internal interface IAiUsageRecorder
{
    Task RecordAsync(AiUsageEvent usageEvent, CancellationToken cancellationToken);
}

internal sealed class DurableAiUsageRecorder(ILogger<DurableAiUsageRecorder> logger, IAiExecutionLedger ledger, TimeProvider clock) : IAiUsageRecorder
{
    public async Task RecordAsync(AiUsageEvent usageEvent, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "AI operation {Operation} execution {ExecutionId} in Workspace {WorkspaceId} for Member {MemberId} used provider {Provider}/{Model}, tools {ToolNames}, context fields {ContextFields}, status {Status}, duration {DurationMs}ms",
            usageEvent.Operation,
            usageEvent.ExecutionId,
            usageEvent.WorkspaceId,
            usageEvent.MemberId,
            usageEvent.Provider,
            usageEvent.Model,
            string.Join(',', usageEvent.ToolNames),
            string.Join(',', usageEvent.ContextFields),
            usageEvent.Status,
            usageEvent.Duration.TotalMilliseconds);
        await ledger.RecordAsync(new AiExecutionEvidence(usageEvent.ExecutionId, usageEvent.WorkspaceId, usageEvent.MemberId, usageEvent.Operation, usageEvent.Provider, usageEvent.Model, usageEvent.StartedAt, clock.GetUtcNow(), usageEvent.Status, (long)usageEvent.Duration.TotalMilliseconds, usageEvent.ToolNames, usageEvent.EvidenceIdentifiers, usageEvent.InputTokens, usageEvent.OutputTokens, usageEvent.ProviderRequestId, usageEvent.Status == "SUCCEEDED" ? null : usageEvent.Status), cancellationToken);
    }
}
