using UnicoreCRM.Crm.Leads.Contracts;

namespace UnicoreCRM.Integrations.Webhooks.Inbound.Application;

/// <summary>
/// Normalizes a verified provider payload into the canonical Leads create contract.
/// Owner identity comes from the integration binding, never from the payload, so a provider
/// cannot select the Lead owner.
/// </summary>
internal static class InboundLeadNormalization
{
    internal static CreateLeadRequest ToCreateLeadRequest(
        GenericLeadWebhookPayload payload,
        string delegatedMemberId) =>
        new()
        {
            DisplayName = payload.DisplayName,
            Source = payload.Source,
            OwnerId = delegatedMemberId,
            EstimatedValue = payload.EstimatedValue is null
                ? null
                : new Money(payload.EstimatedValue.Amount, payload.EstimatedValue.Currency),
            Email = payload.Email,
            Phone = payload.Phone,
            CompanyName = payload.CompanyName,
            Description = payload.Description
        };
}
