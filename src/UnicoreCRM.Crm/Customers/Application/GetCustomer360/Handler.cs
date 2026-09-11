using System.Text.RegularExpressions;
using UnicoreCRM.Crm.Contacts.Contracts;
using UnicoreCRM.Crm.Customers.Application.Common;
using UnicoreCRM.Crm.Customers.Contracts;
using UnicoreCRM.Crm.Customers.Domain;
using UnicoreCRM.Crm.Organizations.Contracts;

namespace UnicoreCRM.Crm.Customers.Application.GetCustomer360;

internal sealed record Query(string CustomerId, CustomerRequestMetadata Metadata);
internal sealed partial class Handler(CustomerAuthorization authorization, ICustomersPersistence persistence,
    IContactCustomerSubjectParticipant contacts, IOrganizationCustomerSubjectParticipant organizations,
    ICustomerStakeholderReadParticipant stakeholders, TimeProvider timeProvider)
{
    internal async Task<CustomerOperationResult<Customer360ReadModel>> HandleAsync(Query query, CancellationToken cancellationToken)
    {
        var access = await authorization.AuthorizeAsync(query.Metadata, cancellationToken);
        if (!access.IsSuccess) return CustomerOperationResult<Customer360ReadModel>.Failure(access.Error!);
        if (!EntityIdPattern().IsMatch(query.CustomerId)) return CustomerOperationResult<Customer360ReadModel>.Failure(CustomerErrors.NotFound());
        var customer = await persistence.ReadCustomerAsync(access.Value!.Trusted.WorkspaceId, query.CustomerId, cancellationToken);
        if (customer is null) return CustomerOperationResult<Customer360ReadModel>.Failure(CustomerErrors.NotFound());
        var denied = await authorization.EnforceRecordAsync(access.Value, customer, "getCustomer360", query.Metadata, cancellationToken);
        if (denied is not null) return CustomerOperationResult<Customer360ReadModel>.Failure(denied);

        Customer360Identity? identity;
        if (customer.RelationshipType == "CONTACT")
        {
            var subject = await contacts.ResolveVisibleAsync(access.Value.Trusted, customer.RelationshipId,
                query.Metadata.RequestId, query.Metadata.CorrelationId, cancellationToken);
            identity = subject is null ? null : new(subject.DisplayName) { ContactId = subject.ContactId, Email = subject.Email, Phone = subject.Phone };
        }
        else
        {
            var subject = await organizations.ResolveVisibleAsync(access.Value.Trusted, customer.RelationshipId,
                query.Metadata.RequestId, query.Metadata.CorrelationId, cancellationToken);
            identity = subject is null ? null : new(subject.DisplayName) { OrganizationId = subject.OrganizationId, Email = subject.Email, Phone = subject.Phone };
        }
        if (identity is null) return CustomerOperationResult<Customer360ReadModel>.Failure(CustomerErrors.NotFound());

        var stakeholderRows = await stakeholders.ReadVisibleAsync(access.Value.Trusted, customer.CustomerId,
            query.Metadata.RequestId, query.Metadata.CorrelationId, cancellationToken);
        var stakeholderDocuments = stakeholderRows.Select(x => new CustomerStakeholderContactDocument(x.RelationshipId,
            x.ContactId, x.DisplayName, x.Role, CustomerProjection.TimestampValue(x.EffectiveFrom))
            { EffectiveTo = x.EffectiveTo is null ? null : CustomerProjection.TimestampValue(x.EffectiveTo.Value) }).ToArray();
        identity = identity with { PrimaryContactId = stakeholderRows.FirstOrDefault(x => x.EffectiveTo is null && x.Role == "primary_contact")?.ContactId };

        var actions = new List<string>();
        if (customer.Status != "ARCHIVED")
        {
            if (await CanMutateAsync(customer, query.Metadata, CustomerCapabilities.Edit, "getCustomer360.allowedActions.edit", cancellationToken)) actions.Add("updateCustomer");
            if (await CanMutateAsync(customer, query.Metadata, CustomerCapabilities.Archive, "getCustomer360.allowedActions.archive", cancellationToken)) actions.Add("archiveCustomer");
        }
        persistence.AddReadAudit(new CustomerReadAuditRecord("getCustomer360", access.Value.Trusted.WorkspaceId,
            access.Value.Trusted.MemberId, customer.CustomerId, query.Metadata.RequestId, query.Metadata.CorrelationId,
            customer.Version, timeProvider.GetUtcNow()));
        await persistence.SaveChangesAsync(cancellationToken);
        var document = CustomerFieldSecurity.Project(CustomerProjection.Document(customer), access.Value.Authorization);
        return CustomerOperationResult<Customer360ReadModel>.Success(new(document, identity,
            new Dictionary<string, object>(), [], stakeholderDocuments, actions, customer.Version,
            CustomerProjection.TimestampValue(timeProvider.GetUtcNow())));
    }

    private async Task<bool> CanMutateAsync(Customer customer, CustomerRequestMetadata metadata,
        UnicoreCRM.Platform.AccessControl.Contracts.AccessRequirement requirement, string point, CancellationToken cancellationToken)
    {
        var access = await authorization.AuthorizeAsync(metadata, requirement, cancellationToken);
        return access.IsSuccess && await authorization.EnforceRecordAsync(access.Value!, customer, point, metadata, cancellationToken) is null;
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)] private static partial Regex EntityIdPattern();
}
