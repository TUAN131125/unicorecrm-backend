using System.Globalization;
using UnicoreCRM.Crm.Contacts.Contracts;
using UnicoreCRM.Crm.Contacts.Domain;

namespace UnicoreCRM.Crm.Contacts.Application.Common;

internal static class ContactProjection
{
    internal static ContactDocument Document(Contact contact) =>
        new(
            contact.ContactId,
            contact.WorkspaceId,
            contact.FullName,
            contact.Status,
            contact.Version,
            Timestamp(contact.CreatedAt),
            Timestamp(contact.UpdatedAt))
        {
            Salutation = contact.Profile.Salutation,
            JobTitle = contact.Profile.JobTitle,
            Department = contact.Profile.Department,
            RoleAtCompany = contact.Profile.RoleAtCompany,
            WorkEmail = contact.Profile.WorkEmail,
            PersonalEmail = contact.Profile.PersonalEmail,
            MobilePhone = contact.Profile.MobilePhone,
            WorkPhone = contact.Profile.WorkPhone,
            OtherPhone = contact.Profile.OtherPhone,
            ZaloId = contact.Profile.ZaloId,
            Facebook = contact.Profile.Facebook,
            PreferredContactChannel = contact.Profile.PreferredContactChannel,
            Address = contact.Profile.Address,
            AddressDetails = Address(contact.Profile.AddressDetails),
            Source = contact.Profile.Source,
            OwnerId = contact.OwnerId,
            Consent = Consent(contact.Profile.Consent),
            DoNotCall = contact.Profile.DoNotCall,
            DoNotEmail = contact.Profile.DoNotEmail,
            DoNotSms = contact.Profile.DoNotSms,
            DoNotZalo = contact.Profile.DoNotZalo,
            DoNotContact = contact.Profile.DoNotContact,
            DoNotContactReason = contact.Profile.DoNotContactReason,
            DecisionRole = contact.Profile.DecisionRole,
            RelationshipLevel = contact.Profile.RelationshipLevel,
            PainPoint = contact.Profile.PainPoint,
            NeedSummary = contact.Profile.NeedSummary,
            Notes = contact.Profile.Notes,
            Tags = contact.Profile.Tags,
            OrganizationRelationships = contact.Profile.OrganizationRelationships?.Select(Relationship).ToArray(),
            DisplayName = contact.Profile.DisplayName
        };

    private static PostalAddressDocument? Address(ContactPostalAddress? address) =>
        address is null
            ? null
            : new PostalAddressDocument(address.Line1)
            {
                Line2 = address.Line2,
                Ward = address.Ward,
                District = address.District,
                Province = address.Province,
                Country = address.Country,
                PostalCode = address.PostalCode,
                Formatted = address.Formatted
            };

    private static CommunicationConsentProfileDocument? Consent(ContactCommunicationConsentProfile? consent) =>
        consent is null
            ? null
            : new CommunicationConsentProfileDocument(
                consent.Current,
                consent.Ledger.Select(Ledger).ToArray(),
                Timestamp(consent.UpdatedAt))
            {
                LawfulBasis = consent.LawfulBasis
            };

    private static CommunicationConsentLedgerEntryDocument Ledger(ContactCommunicationConsentLedgerEntry item) =>
        new(item.Id, item.Channel, item.Decision, item.Source, Timestamp(item.OccurredAt))
        {
            ActorId = item.ActorId,
            Evidence = item.Evidence,
            ExpiresAt = item.ExpiresAt is null ? null : Timestamp(item.ExpiresAt.Value)
        };

    private static ContactOrganizationRelationshipDocument Relationship(ContactOrganizationRelationship item) =>
        new(
            item.Id,
            item.OrganizationAccountId,
            item.Role,
            item.IsPrimaryRepresentative,
            Timestamp(item.EffectiveFrom),
            Timestamp(item.CreatedAt))
        {
            RoleTitle = item.RoleTitle,
            Department = item.Department,
            DecisionRole = item.DecisionRole,
            EffectiveTo = item.EffectiveTo is null ? null : Timestamp(item.EffectiveTo.Value),
            CreatedBy = item.CreatedBy,
            UpdatedAt = item.UpdatedAt is null ? null : Timestamp(item.UpdatedAt.Value),
            UpdatedBy = item.UpdatedBy,
            EndedReason = item.EndedReason
        };

    private static string Timestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
}
