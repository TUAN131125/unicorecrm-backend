using System.Globalization;
using UnicoreCRM.Crm.Organizations.Contracts;
using UnicoreCRM.Crm.Organizations.Domain;

namespace UnicoreCRM.Crm.Organizations.Application.Common;

internal static class OrganizationProjection
{
    internal static OrganizationDocument Document(Organization organization) =>
        new(
            organization.OrganizationId,
            organization.WorkspaceId,
            organization.DisplayName,
            organization.Status,
            organization.Version,
            Timestamp(organization.CreatedAt),
            Timestamp(organization.UpdatedAt))
        {
            LegalName = organization.Profile.LegalName,
            TaxCode = organization.Profile.TaxCode,
            Domain = organization.Profile.Domain,
            Website = organization.Profile.Website,
            Industry = organization.Profile.Industry,
            SizeBand = organization.Profile.SizeBand,
            EmployeeCount = organization.Profile.EmployeeCount,
            AnnualRevenue = organization.Profile.AnnualRevenue,
            Email = organization.Profile.Email,
            Phone = organization.Profile.Phone,
            Address = organization.Profile.Address,
            AddressDetails = Address(organization.Profile.AddressDetails),
            Source = organization.Profile.Source,
            OwnerId = organization.Profile.OwnerId,
            PrimaryContactId = organization.Profile.PrimaryContactId,
            ContactRefs = organization.Profile.ContactRefs,
            RelationshipLevel = organization.Profile.RelationshipLevel,
            Notes = organization.Profile.Notes,
            ExternalRef = organization.Profile.ExternalRef
        };

    private static OrganizationPostalAddressDocument? Address(OrganizationPostalAddress? address) =>
        address is null
            ? null
            : new OrganizationPostalAddressDocument(address.Line1)
            {
                Line2 = address.Line2,
                Ward = address.Ward,
                District = address.District,
                Province = address.Province,
                Country = address.Country,
                PostalCode = address.PostalCode,
                Formatted = address.Formatted
            };

    private static string Timestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
}
