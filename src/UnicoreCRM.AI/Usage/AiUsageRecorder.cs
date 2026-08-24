using Microsoft.Extensions.Logging;

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
    TimeSpan Duration);

internal interface IAiUsageRecorder
{
    void Record(AiUsageEvent usageEvent);
}

internal sealed class LoggingAiUsageRecorder(ILogger<LoggingAiUsageRecorder> logger) : IAiUsageRecorder
{
    public void Record(AiUsageEvent usageEvent)
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
    }
}
