using System.Text.RegularExpressions;
using UnicoreCRM.Crm.Contacts.Application.Common;
using UnicoreCRM.Crm.Contacts.Contracts;
using UnicoreCRM.Crm.Contacts.Domain;
using UnicoreCRM.Crm.Customers.Contracts;
using UnicoreCRM.Crm.Organizations.Contracts;

namespace UnicoreCRM.Crm.Contacts.Application.GetRelationshipSummary;

internal sealed record Query(string ContactId, ContactRequestMetadata Metadata);

internal sealed partial class Handler(
    ContactAuthorization authorization,
    IContactsPersistence persistence,
    IOrganizationRelationshipTargetParticipant organizationTargets,
    ICustomerRelationshipTargetParticipant customerTargets,
    TimeProvider timeProvider)
{
    internal async Task<ContactOperationResult<ContactRelationshipSummaryDocument>> HandleAsync(Query query, CancellationToken cancellationToken)
    {
        var access = await authorization.AuthorizeAsync(query.Metadata, cancellationToken);
        if (!access.IsSuccess) return ContactOperationResult<ContactRelationshipSummaryDocument>.Failure(access.Error!);
        if (!EntityIdPattern().IsMatch(query.ContactId)) return ContactOperationResult<ContactRelationshipSummaryDocument>.Failure(ContactErrors.NotFound());
        var contact = await persistence.ReadContactAsync(access.Value!.Trusted.WorkspaceId, query.ContactId, cancellationToken);
        if (contact is null) return ContactOperationResult<ContactRelationshipSummaryDocument>.Failure(ContactErrors.NotFound());
        var denied = await authorization.EnforceRecordAsync(access.Value, contact, "getContactRelationshipSummary", query.Metadata, cancellationToken);
        if (denied is not null) return ContactOperationResult<ContactRelationshipSummaryDocument>.Failure(denied);

        var organizations = await persistence.ReadOrganizationRelationshipsAsync(access.Value.Trusted.WorkspaceId, contact.ContactId, cancellationToken);
        var customers = await persistence.ReadCustomerRelationshipsAsync(access.Value.Trusted.WorkspaceId, contact.ContactId, cancellationToken);
        var organizationResolution = await organizationTargets.ResolveVisibleAsync(access.Value.Trusted,
            organizations.Select(item => item.OrganizationId).Distinct(StringComparer.Ordinal).ToArray(), query.Metadata.RequestId, query.Metadata.CorrelationId, cancellationToken);
        var customerResolution = await customerTargets.ResolveVisibleAsync(access.Value.Trusted,
            customers.Select(item => item.CustomerId).Distinct(StringComparer.Ordinal).ToArray(), query.Metadata.RequestId, query.Metadata.CorrelationId, cancellationToken);

        var visibleOrganizations = organizationResolution.CanReadResource
            ? organizations.Where(item => organizationResolution.VisibleTargets.ContainsKey(item.OrganizationId)).ToArray()
            : [];
        var visibleCustomers = customerResolution.CanReadResource
            ? customers.Where(item => customerResolution.VisibleTargets.ContainsKey(item.CustomerId)).ToArray()
            : [];
        var organizationDocuments = visibleOrganizations.Select(item => ContactProjection.OrganizationRelationship(item, organizationResolution.VisibleTargets[item.OrganizationId])).ToArray();
        var customerDocuments = visibleCustomers.Select(item => ContactProjection.CustomerRelationship(item, customerResolution.VisibleTargets[item.CustomerId])).ToArray();
        var activeOrganizationIds = visibleOrganizations.Where(item => item.EffectiveTo is null).Select(item => item.OrganizationId).Distinct(StringComparer.Ordinal).ToArray();
        var activeCustomerIds = visibleCustomers.Where(item => item.EffectiveTo is null).Select(item => item.CustomerId).Distinct(StringComparer.Ordinal).ToArray();
        var linked = activeOrganizationIds.Select(id => new ContactRelationshipTargetDocument("organizations", id) { Label = organizationResolution.VisibleTargets[id] })
            .Concat(activeCustomerIds.Select(id => new ContactRelationshipTargetDocument("customers", id) { Label = customerResolution.VisibleTargets[id] })).ToArray();

        var allowed = new List<string>();
        if (contact.ArchivedAt is null)
        {
            var update = await authorization.AuthorizeAsync(query.Metadata, ContactCapabilities.Update, cancellationToken);
            if (update.IsSuccess)
            {
                if (organizationResolution.CanReadResource) allowed.Add("createContactOrganizationRelationship");
                if (visibleOrganizations.Any(item => item.EffectiveTo is null))
                {
                    allowed.Add("updateContactOrganizationRelationship");
                    allowed.Add("endContactOrganizationRelationship");
                }
                if (customerResolution.CanReadResource) allowed.Add("createContactCustomerRelationship");
                if (visibleCustomers.Any(item => item.EffectiveTo is null))
                {
                    allowed.Add("updateContactCustomerRelationship");
                    allowed.Add("endContactCustomerRelationship");
                }
            }
        }

        var now = timeProvider.GetUtcNow();
        persistence.AddReadAudit(new ContactReadAuditRecord("getContactRelationshipSummary", access.Value.Trusted.WorkspaceId,
            access.Value.Trusted.MemberId, contact.ContactId, query.Metadata.RequestId, query.Metadata.CorrelationId, contact.Version, now));
        await persistence.SaveChangesAsync(cancellationToken);
        return ContactOperationResult<ContactRelationshipSummaryDocument>.Success(new(
            ContactFieldSecurity.Project(ContactProjection.Document(contact), access.Value.Authorization), activeOrganizationIds,
            activeCustomerIds, linked, new(), allowed, organizationDocuments, customerDocuments, contact.Version,
            ContactProjection.TimestampValue(now)));
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityIdPattern();
}
