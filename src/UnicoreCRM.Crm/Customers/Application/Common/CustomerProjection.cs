using System.Globalization;
using UnicoreCRM.Crm.Customers.Contracts;
using UnicoreCRM.Crm.Customers.Domain;

namespace UnicoreCRM.Crm.Customers.Application.Common;

internal static class CustomerProjection
{
    internal static CustomerDocument Document(Customer customer) =>
        new(
            customer.CustomerId,
            customer.WorkspaceId,
            customer.CustomerCode,
            customer.Type,
            new RelationshipRefDocument(customer.RelationshipType, customer.RelationshipId),
            customer.Status,
            customer.Health,
            Timestamp(customer.FirstPurchaseAt),
            Timestamp(customer.LastPurchaseAt),
            customer.Version,
            Timestamp(customer.CreatedAt),
            Timestamp(customer.UpdatedAt))
        {
            CalculatedHealth = customer.Profile.CalculatedHealth,
            ManualHealthOverride = customer.Profile.ManualHealthOverride,
            OnboardingStatus = customer.Profile.OnboardingStatus,
            OnboardingCompletedAt = Timestamp(customer.Profile.OnboardingCompletedAt),
            CreatedFromEvidenceId = customer.Profile.CreatedFromEvidenceId,
            ConversionPolicyVersion = customer.Profile.ConversionPolicyVersion,
            ConversionCorrelationId = customer.Profile.ConversionCorrelationId,
            SourceSystem = customer.Profile.SourceSystem,
            ExternalCustomerRef = customer.Profile.ExternalCustomerRef,
            Tier = customer.Profile.Tier,
            ServiceLevel = customer.Profile.ServiceLevel,
            CareCadenceDays = customer.Profile.CareCadenceDays,
            CareOwnerId = customer.Profile.CareOwnerId,
            Segment = customer.Profile.Segment,
            Tags = customer.Profile.Tags,
            NextCareAt = Timestamp(customer.Profile.NextCareAt),
            LastCareAt = Timestamp(customer.Profile.LastCareAt)
        };

    private static string Timestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static string? Timestamp(DateTimeOffset? value) =>
        value is null ? null : Timestamp(value.Value);
}
