namespace UnicoreCRM.Integrations.Webhooks.Inbound.Contracts;

public sealed record InboundLeadWebhookReceipt(
    string IntegrationId,
    string DeliveryId,
    string LeadId,
    string Outcome,
    string CorrelationId);

public sealed record InboundWebhookProblemDetails(
    string Type,
    string Title,
    int Status,
    string Code,
    bool Retryable,
    string CorrelationId,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null);
