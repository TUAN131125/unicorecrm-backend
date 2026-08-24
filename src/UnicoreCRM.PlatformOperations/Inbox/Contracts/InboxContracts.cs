namespace UnicoreCRM.PlatformOperations.Inbox.Contracts;

public sealed record InboundDelivery(
    string IntegrationId,
    string DeliveryId,
    string PayloadHash,
    string ProviderCode,
    string WorkspaceId,
    string DelegatedMemberId,
    string CorrelationId,
    DateTimeOffset ReceivedAt);

public enum InboxAdmissionKind
{
    Accepted,
    Resume,
    Replay,
    Conflict
}

public sealed record InboxAdmission(
    InboxAdmissionKind Kind,
    string InboxMessageId,
    string? LeadId,
    string? LastResultCode);

public interface IInboundDeliveryInbox
{
    Task<InboxAdmission> AdmitAsync(
        InboundDelivery delivery,
        CancellationToken cancellationToken);

    Task CompleteAsync(
        string inboxMessageId,
        string leadId,
        string resultCode,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken);

    Task FailAsync(
        string inboxMessageId,
        string resultCode,
        DateTimeOffset failedAt,
        CancellationToken cancellationToken);
}
