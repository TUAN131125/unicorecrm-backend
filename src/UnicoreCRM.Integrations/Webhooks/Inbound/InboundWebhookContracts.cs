using System.Text.Json.Serialization;

namespace UnicoreCRM.Integrations.Webhooks.Inbound;

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

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class GenericLeadWebhookPayload
{
    public string? DisplayName { get; init; }
    public string? Source { get; init; }
    public GenericWebhookMoney? EstimatedValue { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? CompanyName { get; init; }
    public string? Description { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record GenericWebhookMoney(string? Amount, string? Currency);

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
