namespace UnicoreCRM.Platform.Workspace.Domain;

internal sealed class WorkspaceAccessRecord
{
    private WorkspaceAccessRecord() { }

    internal WorkspaceAccessRecord(string operation, string accountId, string? workspaceId, string correlationId, DateTimeOffset occurredAt)
    {
        AccessRecordId = WorkspaceIds.New("wsa");
        Operation = operation;
        AccountId = accountId;
        WorkspaceId = workspaceId;
        CorrelationId = correlationId;
        OccurredAt = occurredAt;
    }

    internal static WorkspaceAccessRecord SuccessfulRead(
        string operation,
        string accountId,
        string? workspaceId,
        string requestId,
        string correlationId,
        DateTimeOffset occurredAt)
    {
        var record = new WorkspaceAccessRecord(operation, accountId, workspaceId, correlationId, occurredAt)
        {
            RequestId = requestId,
            Outcome = "READ"
        };
        return record;
    }

    public string AccessRecordId { get; private set; } = null!;
    public string Operation { get; private set; } = null!;
    public string AccountId { get; private set; } = null!;
    public string? WorkspaceId { get; private set; }
    public string? RequestId { get; private set; }
    public string CorrelationId { get; private set; } = null!;
    public string? Outcome { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
}
