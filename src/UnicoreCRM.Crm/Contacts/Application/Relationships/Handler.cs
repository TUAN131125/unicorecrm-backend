using System.Globalization;
using System.Text.RegularExpressions;
using UnicoreCRM.Crm.Contacts.Application.Common;
using UnicoreCRM.Crm.Contacts.Contracts;
using UnicoreCRM.Crm.Contacts.Domain;
using UnicoreCRM.Crm.Customers.Contracts;
using UnicoreCRM.Crm.Organizations.Contracts;

namespace UnicoreCRM.Crm.Contacts.Application.Relationships;

internal sealed record CreateOrganizationCommand(string ContactId, CreateContactOrganizationRelationshipRequest Request, ContactCommandMetadata Metadata);
internal sealed record UpdateOrganizationCommand(string ContactId, string RelationshipId, UpdateContactOrganizationRelationshipRequest Request, ContactCommandMetadata Metadata);
internal sealed record EndOrganizationCommand(string ContactId, string RelationshipId, EndContactRelationshipRequest Request, ContactCommandMetadata Metadata);
internal sealed record CreateCustomerCommand(string ContactId, CreateContactCustomerRelationshipRequest Request, ContactCommandMetadata Metadata);
internal sealed record UpdateCustomerCommand(string ContactId, string RelationshipId, UpdateContactCustomerRelationshipRequest Request, ContactCommandMetadata Metadata);
internal sealed record EndCustomerCommand(string ContactId, string RelationshipId, EndContactRelationshipRequest Request, ContactCommandMetadata Metadata);

internal sealed partial class Handler(
    ContactAuthorization authorization,
    IContactsPersistence persistence,
    IOrganizationRelationshipTargetParticipant organizationTargets,
    ICustomerRelationshipTargetParticipant customerTargets,
    TimeProvider timeProvider)
{
    internal async Task<ContactOperationResult<ContactMutationResponse>> CreateOrganizationAsync(CreateOrganizationCommand command, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (!TryId(command.Request.OrganizationId, "organizationId", out var organizationId, out var error)
            || !TryRole(command.Request.Role, ContactRelationshipRoles.Organization, "role", out var role, out error)
            || !TryEffectiveFrom(command.Request.EffectiveFrom, now, out var effectiveFrom, out error))
            return ContactOperationResult<ContactMutationResponse>.Failure(error!);
        var prepared = await PrepareAsync(command.ContactId, command.Metadata, "createContactOrganizationRelationship", cancellationToken);
        if (!prepared.IsSuccess) return ContactOperationResult<ContactMutationResponse>.Failure(prepared.Error!);
        await using var transaction = prepared.Value!.Transaction;
        var target = await organizationTargets.ResolveVisibleAsync(prepared.Value.Trusted, [organizationId!], command.Metadata.RequestId, command.Metadata.CorrelationId, cancellationToken);
        if (!target.CanReadResource || !target.VisibleTargets.TryGetValue(organizationId!, out var label)) return ContactOperationResult<ContactMutationResponse>.Failure(ContactErrors.NotFound());
        var fingerprint = ContactMutationSupport.Fingerprint(new { command.ContactId, organizationId, role, command.Request.IsPrimaryAffiliation, command.Request.EffectiveFrom, command.Metadata.ExpectedVersion });
        var replay = await ReplayAsync(prepared.Value, command.Metadata, "createContactOrganizationRelationship", command.ContactId, fingerprint, cancellationToken);
        if (replay is not null) return replay;
        var contact = await LoadMutableContactAsync(prepared.Value, command.Metadata, cancellationToken);
        if (!contact.IsSuccess) return ContactOperationResult<ContactMutationResponse>.Failure(contact.Error!);
        if (await persistence.HasActiveOrganizationRelationshipAsync(prepared.Value.Trusted.WorkspaceId, command.ContactId, organizationId!, cancellationToken))
            return ContactOperationResult<ContactMutationResponse>.Failure(ContactErrors.RelationshipConflict("An active relationship already exists for this Contact and Organization."));
        if (command.Request.IsPrimaryAffiliation)
        {
            var prior = await persistence.LoadActivePrimaryOrganizationRelationshipAsync(prepared.Value.Trusted.WorkspaceId, command.ContactId, null, cancellationToken);
            prior?.ClearPrimary(prepared.Value.Trusted.MemberId, now);
        }
        var relationship = new ContactOrganizationRelationship(prepared.Value.Trusted.WorkspaceId, command.ContactId, organizationId!, role!, command.Request.IsPrimaryAffiliation, effectiveFrom, prepared.Value.Trusted.MemberId, now);
        persistence.AddOrganizationRelationship(relationship);
        contact.Value!.RecordRelationshipMutation(now);
        return await CommitAsync(prepared.Value, command.Metadata, contact.Value, "createContactOrganizationRelationship", "CONTACT_ORGANIZATION_RELATIONSHIP_CREATED", command.ContactId, fingerprint, now, ContactProjection.OrganizationRelationship(relationship, label), null, cancellationToken);
    }

    internal async Task<ContactOperationResult<ContactMutationResponse>> UpdateOrganizationAsync(UpdateOrganizationCommand command, CancellationToken cancellationToken)
    {
        if (command.Request.Role is null && command.Request.IsPrimaryAffiliation is null)
            return ContactOperationResult<ContactMutationResponse>.Failure(ContactErrors.Validation(new Dictionary<string, string[]> { ["body"] = ["At least one relationship field is required."] }));
        string? role = null;
        ContactOperationError? error = null;
        if (command.Request.Role is not null && !TryRole(command.Request.Role, ContactRelationshipRoles.Organization, "role", out role, out error))
            return ContactOperationResult<ContactMutationResponse>.Failure(error!);
        var now = timeProvider.GetUtcNow();
        var prepared = await PrepareAsync(command.ContactId, command.Metadata, "updateContactOrganizationRelationship", cancellationToken);
        if (!prepared.IsSuccess) return ContactOperationResult<ContactMutationResponse>.Failure(prepared.Error!);
        await using var transaction = prepared.Value!.Transaction;
        var relationship = await persistence.LoadOrganizationRelationshipAsync(prepared.Value.Trusted.WorkspaceId, command.ContactId, command.RelationshipId, cancellationToken);
        if (relationship is null) return ContactOperationResult<ContactMutationResponse>.Failure(ContactErrors.NotFound());
        var target = await organizationTargets.ResolveVisibleAsync(prepared.Value.Trusted, [relationship.OrganizationId], command.Metadata.RequestId, command.Metadata.CorrelationId, cancellationToken);
        if (!target.CanReadResource || !target.VisibleTargets.TryGetValue(relationship.OrganizationId, out var label)) return ContactOperationResult<ContactMutationResponse>.Failure(ContactErrors.NotFound());
        var fingerprint = ContactMutationSupport.Fingerprint(new { command.ContactId, command.RelationshipId, command.Request.Role, command.Request.IsPrimaryAffiliation, command.Metadata.ExpectedVersion });
        var replay = await ReplayAsync(prepared.Value, command.Metadata, "updateContactOrganizationRelationship", command.ContactId, fingerprint, cancellationToken);
        if (replay is not null) return replay;
        if (relationship.EffectiveTo is not null) return ContactOperationResult<ContactMutationResponse>.Failure(ContactErrors.NotFound());
        var contact = await LoadMutableContactAsync(prepared.Value, command.Metadata, cancellationToken);
        if (!contact.IsSuccess) return ContactOperationResult<ContactMutationResponse>.Failure(contact.Error!);
        var primary = command.Request.IsPrimaryAffiliation ?? relationship.IsPrimaryAffiliation;
        if (primary)
        {
            var prior = await persistence.LoadActivePrimaryOrganizationRelationshipAsync(prepared.Value.Trusted.WorkspaceId, command.ContactId, relationship.RelationshipId, cancellationToken);
            prior?.ClearPrimary(prepared.Value.Trusted.MemberId, now);
        }
        relationship.Update(role ?? relationship.Role, primary, prepared.Value.Trusted.MemberId, now);
        contact.Value!.RecordRelationshipMutation(now);
        return await CommitAsync(prepared.Value, command.Metadata, contact.Value, "updateContactOrganizationRelationship", "CONTACT_ORGANIZATION_RELATIONSHIP_UPDATED", command.ContactId, fingerprint, now, ContactProjection.OrganizationRelationship(relationship, label), null, cancellationToken);
    }

    internal async Task<ContactOperationResult<ContactMutationResponse>> EndOrganizationAsync(EndOrganizationCommand command, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (!TryReason(command.Request.EndedReason, out var reason, out var error)) return ContactOperationResult<ContactMutationResponse>.Failure(error!);
        var prepared = await PrepareAsync(command.ContactId, command.Metadata, "endContactOrganizationRelationship", cancellationToken);
        if (!prepared.IsSuccess) return ContactOperationResult<ContactMutationResponse>.Failure(prepared.Error!);
        await using var transaction = prepared.Value!.Transaction;
        var relationship = await persistence.LoadOrganizationRelationshipAsync(prepared.Value.Trusted.WorkspaceId, command.ContactId, command.RelationshipId, cancellationToken);
        if (relationship is null) return ContactOperationResult<ContactMutationResponse>.Failure(ContactErrors.NotFound());
        var target = await organizationTargets.ResolveVisibleAsync(prepared.Value.Trusted, [relationship.OrganizationId], command.Metadata.RequestId, command.Metadata.CorrelationId, cancellationToken);
        if (!target.CanReadResource || !target.VisibleTargets.TryGetValue(relationship.OrganizationId, out var label)) return ContactOperationResult<ContactMutationResponse>.Failure(ContactErrors.NotFound());
        var fingerprint = ContactMutationSupport.Fingerprint(new { command.ContactId, command.RelationshipId, reason, command.Request.EffectiveTo, command.Metadata.ExpectedVersion });
        var replay = await ReplayAsync(prepared.Value, command.Metadata, "endContactOrganizationRelationship", command.ContactId, fingerprint, cancellationToken);
        if (replay is not null) return replay;
        if (relationship.EffectiveTo is not null) return ContactOperationResult<ContactMutationResponse>.Failure(ContactErrors.NotFound());
        if (!TryEffectiveTo(command.Request.EffectiveTo, relationship.EffectiveFrom, now, out var effectiveTo, out error)) return ContactOperationResult<ContactMutationResponse>.Failure(error!);
        var contact = await LoadMutableContactAsync(prepared.Value, command.Metadata, cancellationToken);
        if (!contact.IsSuccess) return ContactOperationResult<ContactMutationResponse>.Failure(contact.Error!);
        relationship.End(effectiveTo, reason!, prepared.Value.Trusted.MemberId, now);
        contact.Value!.RecordRelationshipMutation(now);
        return await CommitAsync(prepared.Value, command.Metadata, contact.Value, "endContactOrganizationRelationship", "CONTACT_ORGANIZATION_RELATIONSHIP_ENDED", command.ContactId, fingerprint, now, ContactProjection.OrganizationRelationship(relationship, label), null, cancellationToken);
    }

    internal async Task<ContactOperationResult<ContactMutationResponse>> CreateCustomerAsync(CreateCustomerCommand command, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (!TryId(command.Request.CustomerId, "customerId", out var customerId, out var error)
            || !TryRole(command.Request.Role, ContactRelationshipRoles.Customer, "role", out var role, out error)
            || !TryEffectiveFrom(command.Request.EffectiveFrom, now, out var effectiveFrom, out error))
            return ContactOperationResult<ContactMutationResponse>.Failure(error!);
        var prepared = await PrepareAsync(command.ContactId, command.Metadata, "createContactCustomerRelationship", cancellationToken);
        if (!prepared.IsSuccess) return ContactOperationResult<ContactMutationResponse>.Failure(prepared.Error!);
        await using var transaction = prepared.Value!.Transaction;
        var target = await customerTargets.ResolveVisibleAsync(prepared.Value.Trusted, [customerId!], command.Metadata.RequestId, command.Metadata.CorrelationId, cancellationToken);
        if (!target.CanReadResource || !target.VisibleTargets.TryGetValue(customerId!, out var label)) return ContactOperationResult<ContactMutationResponse>.Failure(ContactErrors.NotFound());
        var fingerprint = ContactMutationSupport.Fingerprint(new { command.ContactId, customerId, role, command.Request.EffectiveFrom, command.Metadata.ExpectedVersion });
        var replay = await ReplayAsync(prepared.Value, command.Metadata, "createContactCustomerRelationship", command.ContactId, fingerprint, cancellationToken);
        if (replay is not null) return replay;
        var contact = await LoadMutableContactAsync(prepared.Value, command.Metadata, cancellationToken);
        if (!contact.IsSuccess) return ContactOperationResult<ContactMutationResponse>.Failure(contact.Error!);
        if (await persistence.HasActiveCustomerRelationshipAsync(prepared.Value.Trusted.WorkspaceId, command.ContactId, customerId!, cancellationToken))
            return ContactOperationResult<ContactMutationResponse>.Failure(ContactErrors.RelationshipConflict("An active relationship already exists for this Contact and Customer."));
        if (role == "primary_contact" && await persistence.HasOtherActivePrimaryCustomerRelationshipAsync(prepared.Value.Trusted.WorkspaceId, customerId!, null, cancellationToken))
            return ContactOperationResult<ContactMutationResponse>.Failure(ContactErrors.RelationshipConflict("The Customer already has an active primary_contact stakeholder."));
        var relationship = new ContactCustomerRelationship(prepared.Value.Trusted.WorkspaceId, command.ContactId, customerId!, role!, effectiveFrom, prepared.Value.Trusted.MemberId, now);
        persistence.AddCustomerRelationship(relationship);
        contact.Value!.RecordRelationshipMutation(now);
        return await CommitAsync(prepared.Value, command.Metadata, contact.Value, "createContactCustomerRelationship", "CONTACT_CUSTOMER_RELATIONSHIP_CREATED", command.ContactId, fingerprint, now, null, ContactProjection.CustomerRelationship(relationship, label), cancellationToken);
    }

    internal async Task<ContactOperationResult<ContactMutationResponse>> UpdateCustomerAsync(UpdateCustomerCommand command, CancellationToken cancellationToken)
    {
        if (!TryRole(command.Request.Role, ContactRelationshipRoles.Customer, "role", out var role, out var error)) return ContactOperationResult<ContactMutationResponse>.Failure(error!);
        var now = timeProvider.GetUtcNow();
        var prepared = await PrepareAsync(command.ContactId, command.Metadata, "updateContactCustomerRelationship", cancellationToken);
        if (!prepared.IsSuccess) return ContactOperationResult<ContactMutationResponse>.Failure(prepared.Error!);
        await using var transaction = prepared.Value!.Transaction;
        var relationship = await persistence.LoadCustomerRelationshipAsync(prepared.Value.Trusted.WorkspaceId, command.ContactId, command.RelationshipId, cancellationToken);
        if (relationship is null) return ContactOperationResult<ContactMutationResponse>.Failure(ContactErrors.NotFound());
        var target = await customerTargets.ResolveVisibleAsync(prepared.Value.Trusted, [relationship.CustomerId], command.Metadata.RequestId, command.Metadata.CorrelationId, cancellationToken);
        if (!target.CanReadResource || !target.VisibleTargets.TryGetValue(relationship.CustomerId, out var label)) return ContactOperationResult<ContactMutationResponse>.Failure(ContactErrors.NotFound());
        var fingerprint = ContactMutationSupport.Fingerprint(new { command.ContactId, command.RelationshipId, role, command.Metadata.ExpectedVersion });
        var replay = await ReplayAsync(prepared.Value, command.Metadata, "updateContactCustomerRelationship", command.ContactId, fingerprint, cancellationToken);
        if (replay is not null) return replay;
        if (relationship.EffectiveTo is not null) return ContactOperationResult<ContactMutationResponse>.Failure(ContactErrors.NotFound());
        var contact = await LoadMutableContactAsync(prepared.Value, command.Metadata, cancellationToken);
        if (!contact.IsSuccess) return ContactOperationResult<ContactMutationResponse>.Failure(contact.Error!);
        if (role == "primary_contact" && await persistence.HasOtherActivePrimaryCustomerRelationshipAsync(prepared.Value.Trusted.WorkspaceId, relationship.CustomerId, relationship.RelationshipId, cancellationToken))
            return ContactOperationResult<ContactMutationResponse>.Failure(ContactErrors.RelationshipConflict("The Customer already has an active primary_contact stakeholder."));
        relationship.Update(role!, prepared.Value.Trusted.MemberId, now);
        contact.Value!.RecordRelationshipMutation(now);
        return await CommitAsync(prepared.Value, command.Metadata, contact.Value, "updateContactCustomerRelationship", "CONTACT_CUSTOMER_RELATIONSHIP_UPDATED", command.ContactId, fingerprint, now, null, ContactProjection.CustomerRelationship(relationship, label), cancellationToken);
    }

    internal async Task<ContactOperationResult<ContactMutationResponse>> EndCustomerAsync(EndCustomerCommand command, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (!TryReason(command.Request.EndedReason, out var reason, out var error)) return ContactOperationResult<ContactMutationResponse>.Failure(error!);
        var prepared = await PrepareAsync(command.ContactId, command.Metadata, "endContactCustomerRelationship", cancellationToken);
        if (!prepared.IsSuccess) return ContactOperationResult<ContactMutationResponse>.Failure(prepared.Error!);
        await using var transaction = prepared.Value!.Transaction;
        var relationship = await persistence.LoadCustomerRelationshipAsync(prepared.Value.Trusted.WorkspaceId, command.ContactId, command.RelationshipId, cancellationToken);
        if (relationship is null) return ContactOperationResult<ContactMutationResponse>.Failure(ContactErrors.NotFound());
        var target = await customerTargets.ResolveVisibleAsync(prepared.Value.Trusted, [relationship.CustomerId], command.Metadata.RequestId, command.Metadata.CorrelationId, cancellationToken);
        if (!target.CanReadResource || !target.VisibleTargets.TryGetValue(relationship.CustomerId, out var label)) return ContactOperationResult<ContactMutationResponse>.Failure(ContactErrors.NotFound());
        var fingerprint = ContactMutationSupport.Fingerprint(new { command.ContactId, command.RelationshipId, reason, command.Request.EffectiveTo, command.Metadata.ExpectedVersion });
        var replay = await ReplayAsync(prepared.Value, command.Metadata, "endContactCustomerRelationship", command.ContactId, fingerprint, cancellationToken);
        if (replay is not null) return replay;
        if (relationship.EffectiveTo is not null) return ContactOperationResult<ContactMutationResponse>.Failure(ContactErrors.NotFound());
        if (!TryEffectiveTo(command.Request.EffectiveTo, relationship.EffectiveFrom, now, out var effectiveTo, out error)) return ContactOperationResult<ContactMutationResponse>.Failure(error!);
        var contact = await LoadMutableContactAsync(prepared.Value, command.Metadata, cancellationToken);
        if (!contact.IsSuccess) return ContactOperationResult<ContactMutationResponse>.Failure(contact.Error!);
        relationship.End(effectiveTo, reason!, prepared.Value.Trusted.MemberId, now);
        contact.Value!.RecordRelationshipMutation(now);
        return await CommitAsync(prepared.Value, command.Metadata, contact.Value, "endContactCustomerRelationship", "CONTACT_CUSTOMER_RELATIONSHIP_ENDED", command.ContactId, fingerprint, now, null, ContactProjection.CustomerRelationship(relationship, label), cancellationToken);
    }

    private async Task<ContactOperationResult<Prepared>> PrepareAsync(string contactId, ContactCommandMetadata metadata, string operation, CancellationToken cancellationToken)
    {
        if (!EntityIdPattern().IsMatch(contactId)) return ContactOperationResult<Prepared>.Failure(ContactErrors.NotFound());
        var request = new ContactRequestMetadata(metadata.RequestId, metadata.CorrelationId);
        var access = await authorization.AuthorizeAsync(request, ContactCapabilities.Update, cancellationToken);
        if (!access.IsSuccess) return ContactOperationResult<Prepared>.Failure(access.Error!);
        var transaction = await persistence.BeginSerializableAsync(cancellationToken);
        var guarded = await persistence.ReadContactAsync(access.Value!.Trusted.WorkspaceId, contactId, cancellationToken);
        if (guarded is null) { await transaction.DisposeAsync(); return ContactOperationResult<Prepared>.Failure(ContactErrors.NotFound()); }
        var denied = await authorization.EnforceRecordAsync(access.Value, guarded, operation, request, cancellationToken);
        if (denied is not null) { await transaction.DisposeAsync(); return ContactOperationResult<Prepared>.Failure(denied); }
        return ContactOperationResult<Prepared>.Success(new(access.Value, transaction, contactId));
    }

    private async Task<ContactOperationResult<ContactMutationResponse>?> ReplayAsync(Prepared prepared, ContactCommandMetadata metadata, string operation, string contactId, string fingerprint, CancellationToken cancellationToken)
    {
        var scope = ContactMutationSupport.ScopeKey(prepared.Trusted, operation, contactId, metadata.IdempotencyKey);
        var existing = await persistence.FindIdempotencyAsync(scope, cancellationToken);
        if (existing is null) return null;
        var error = ContactMutationSupport.ReplayError(existing, fingerprint);
        return error is null
            ? ContactOperationResult<ContactMutationResponse>.Success(ContactMutationSupport.Project(ContactMutationSupport.Replay(existing), prepared.Access))
            : ContactOperationResult<ContactMutationResponse>.Failure(error);
    }

    private async Task<ContactOperationResult<Contact>> LoadMutableContactAsync(Prepared prepared, ContactCommandMetadata metadata, CancellationToken cancellationToken)
    {
        var contact = await persistence.LoadContactAsync(prepared.Trusted.WorkspaceId, prepared.ContactId, cancellationToken);
        if (contact is null) return ContactOperationResult<Contact>.Failure(ContactErrors.NotFound());
        if (contact.ArchivedAt is not null) return ContactOperationResult<Contact>.Failure(ContactErrors.AlreadyArchived());
        var expected = metadata.ExpectedVersion!.Value;
        return contact.Version == expected ? ContactOperationResult<Contact>.Success(contact) : ContactOperationResult<Contact>.Failure(ContactErrors.VersionConflict(contact.ContactId, expected, contact.Version));
    }

    private async Task<ContactOperationResult<ContactMutationResponse>> CommitAsync(
        Prepared prepared, ContactCommandMetadata metadata, Contact contact, string operation, string eventType,
        string targetId, string fingerprint, DateTimeOffset now,
        ContactOrganizationRelationshipSummaryDocument? organizationRelationship,
        ContactCustomerRelationshipSummaryDocument? customerRelationship,
        CancellationToken cancellationToken)
    {
        var scope = ContactMutationSupport.ScopeKey(prepared.Trusted, operation, targetId, metadata.IdempotencyKey);
        var response = ContactMutationSupport.RecordCommit(persistence, contact, prepared.Trusted, metadata, operation,
            eventType, scope, targetId, fingerprint, now, organizationRelationship, customerRelationship);
        try { await persistence.SaveChangesAsync(cancellationToken); }
        catch (ContactsPersistenceConcurrencyException) { return ContactOperationResult<ContactMutationResponse>.Failure(ContactErrors.VersionConflict(contact.ContactId, metadata.ExpectedVersion!.Value, contact.Version)); }
        catch (ContactsRelationshipConflictException) { return ContactOperationResult<ContactMutationResponse>.Failure(ContactErrors.RelationshipConflict("The requested active relationship or primary invariant conflicts with current state.")); }
        await prepared.Transaction.CommitAsync(cancellationToken);
        return ContactOperationResult<ContactMutationResponse>.Success(ContactMutationSupport.Project(response, prepared.Access));
    }

    private static bool TryId(string? value, string field, out string? result, out ContactOperationError? error)
    {
        result = value?.Trim();
        error = result is not null && EntityIdPattern().IsMatch(result) ? null : ContactErrors.Validation(new Dictionary<string, string[]> { [field] = [$"{field} is invalid."] });
        return error is null;
    }

    private static bool TryRole(string? value, HashSet<string> allowed, string field, out string? result, out ContactOperationError? error)
    {
        result = value?.Trim().ToLowerInvariant();
        error = result is not null && allowed.Contains(result) ? null : ContactErrors.Validation(new Dictionary<string, string[]> { [field] = [$"{field} is not in the controlled vocabulary."] });
        return error is null;
    }

    private static bool TryReason(string? value, out string? result, out ContactOperationError? error)
    {
        result = value?.Trim();
        error = result is { Length: > 0 and <= 1000 } ? null : ContactErrors.Validation(new Dictionary<string, string[]> { ["endedReason"] = ["endedReason must contain between 1 and 1000 characters."] });
        return error is null;
    }

    private static bool TryEffectiveFrom(string? value, DateTimeOffset now, out DateTimeOffset result, out ContactOperationError? error)
    {
        result = now;
        if (value is not null && !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out result))
        { error = ContactErrors.Validation(new Dictionary<string, string[]> { ["effectiveFrom"] = ["effectiveFrom must be an ISO-8601 timestamp."] }); return false; }
        error = result <= now ? null : ContactErrors.Validation(new Dictionary<string, string[]> { ["effectiveFrom"] = ["effectiveFrom cannot be in the future."] });
        return error is null;
    }

    private static bool TryEffectiveTo(string? value, DateTimeOffset from, DateTimeOffset now, out DateTimeOffset result, out ContactOperationError? error)
    {
        result = now;
        if (value is not null && !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out result))
        { error = ContactErrors.Validation(new Dictionary<string, string[]> { ["effectiveTo"] = ["effectiveTo must be an ISO-8601 timestamp."] }); return false; }
        error = result >= from && result <= now ? null : ContactErrors.Validation(new Dictionary<string, string[]> { ["effectiveTo"] = ["effectiveTo must be between effectiveFrom and the current time."] });
        return error is null;
    }

    private sealed record Prepared(ContactAccess Access, IContactsTransaction Transaction, string ContactId)
    {
        internal UnicoreCRM.Platform.Workspace.Contracts.TrustedWorkspaceContext Trusted => Access.Trusted;
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityIdPattern();
}
