using UnicoreCRM.Integrations.Webhooks.Inbound.Contracts;

namespace UnicoreCRM.Integrations.Webhooks.Inbound.Application;

internal sealed record VerifiedWebhookRequest(
    string IntegrationId,
    string DeliveryId,
    string Timestamp,
    string Signature,
    string CorrelationId,
    byte[] RawPayload);

internal sealed record InboundWebhookExecutionResult(
    int Status,
    InboundLeadWebhookReceipt? Receipt,
    InboundWebhookProblemDetails? Problem)
{
    internal static InboundWebhookExecutionResult Success(InboundLeadWebhookReceipt receipt) => new(200, receipt, null);

    internal static InboundWebhookExecutionResult Failure(
        int status,
        string code,
        string title,
        bool retryable,
        string correlationId,
        IReadOnlyDictionary<string, string[]>? fieldErrors = null) =>
        new(status, null, new InboundWebhookProblemDetails(
            $"urn:unicore:error:{code.ToLowerInvariant()}",
            title,
            status,
            code,
            retryable,
            correlationId,
            fieldErrors));
}
