using UnicoreCRM.Crm.Contacts.Application.Common;
using UnicoreCRM.Crm.Contacts.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Contacts.Application.UpdateContact;

internal sealed record Command(string ContactId, UpdateContactRequest Request, ContactCommandMetadata Metadata);

internal sealed class Handler(
    ContactAuthorization authorization,
    IContactsPersistence persistence,
    IWorkspaceMemberReferenceValidator memberValidator,
    TimeProvider timeProvider)
{
    internal async Task<ContactOperationResult<ContactMutationResponse>> HandleAsync(Command command, CancellationToken cancellationToken)
    {
        var requestMetadata = new ContactRequestMetadata(command.Metadata.RequestId, command.Metadata.CorrelationId);
        var access = await authorization.AuthorizeAsync(requestMetadata, ContactCapabilities.Update, cancellationToken);
        if (!access.IsSuccess) return ContactOperationResult<ContactMutationResponse>.Failure(access.Error!);
        ContactMutationValidation.TryProfile(command.Request, out var fullName, out var ownerId, out var profile, out var errors);
        if (errors.Count != 0) return ContactOperationResult<ContactMutationResponse>.Failure(ContactErrors.Validation(errors));
        var trusted = access.Value!.Trusted;
        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);
        var guarded = await persistence.ReadContactAsync(trusted.WorkspaceId, command.ContactId, cancellationToken);
        if (guarded is null) return ContactOperationResult<ContactMutationResponse>.Failure(ContactErrors.NotFound());
        var guardError = await authorization.EnforceRecordAsync(access.Value, guarded, "updateContact", requestMetadata, cancellationToken);
        if (guardError is not null) return ContactOperationResult<ContactMutationResponse>.Failure(guardError);
        var fingerprint = ContactMutationSupport.Fingerprint(new { command.ContactId, fullName, ownerId, profile, command.Metadata.ExpectedVersion });
        var scopeKey = ContactMutationSupport.ScopeKey(trusted, "updateContact", command.ContactId, command.Metadata.IdempotencyKey);
        var existing = await persistence.FindIdempotencyAsync(scopeKey, cancellationToken);
        if (existing is not null)
        {
            var replayError = ContactMutationSupport.ReplayError(existing, fingerprint);
            return replayError is null ? ContactOperationResult<ContactMutationResponse>.Success(ContactMutationSupport.Replay(existing)) : ContactOperationResult<ContactMutationResponse>.Failure(replayError);
        }
        if (ownerId is not null && !await memberValidator.IsActiveMemberAsync(trusted.WorkspaceId, ownerId, cancellationToken))
            return ContactOperationResult<ContactMutationResponse>.Failure(ContactErrors.Validation(new Dictionary<string, string[]> { ["ownerId"] = ["ownerId must reference an active Workspace member."] }));
        var contact = await persistence.LoadContactAsync(trusted.WorkspaceId, command.ContactId, cancellationToken);
        if (contact is null) return ContactOperationResult<ContactMutationResponse>.Failure(ContactErrors.NotFound());
        if (contact.ArchivedAt is not null) return ContactOperationResult<ContactMutationResponse>.Failure(ContactErrors.AlreadyArchived());
        var expected = command.Metadata.ExpectedVersion!.Value;
        if (contact.Version != expected) return ContactOperationResult<ContactMutationResponse>.Failure(ContactErrors.VersionConflict(contact.ContactId, expected, contact.Version));
        var now = timeProvider.GetUtcNow();
        contact.Update(ownerId, fullName!, profile!, now);
        var response = ContactMutationSupport.RecordCommit(persistence, contact, trusted, command.Metadata,
            "updateContact", "CONTACT_UPDATED", scopeKey, contact.ContactId, fingerprint, now);
        await persistence.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ContactOperationResult<ContactMutationResponse>.Success(response);
    }
}
