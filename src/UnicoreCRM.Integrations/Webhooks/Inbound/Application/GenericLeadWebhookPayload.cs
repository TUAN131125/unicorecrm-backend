using System.Text.Json.Serialization;

namespace UnicoreCRM.Integrations.Webhooks.Inbound.Application;

/// <summary>
/// Wire shape accepted from the generic-signed-json provider. Unmapped members are rejected so an
/// unexpected provider field cannot silently reach normalization.
/// </summary>
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
