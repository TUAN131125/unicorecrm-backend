using UnicoreCRM.Crm.Contacts.Application.Common;
using UnicoreCRM.Crm.Contacts.Contracts;

namespace UnicoreCRM.Crm.Contacts.Application.ArchiveContact;

internal sealed record Command(string ContactId, ArchiveContactRequest Request, ContactCommandMetadata Metadata);

internal sealed class Handler(ContactAuthorization authorization, IContactsPersistence persistence, TimeProvider timeProvider)
{
    internal async Task<ContactOperationResult<ContactMutationResponse>> HandleAsync(Command command, CancellationToken cancellationToken)
    {
        var requestMetadata = new ContactRequestMetadata(command.Metadata.RequestId, command.Metadata.CorrelationId);
        var access = await authorization.AuthorizeAsync(requestMetadata, ContactCapabilities.Archive, cancellationToken);
        if (!access.IsSuccess) return ContactOperationResult<ContactMutationResponse>.Failure(access.Error!);
        var trusted = access.Value!.Trusted;
        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);
        var guarded = await persistence.ReadContactAsync(trusted.WorkspaceId, command.ContactId, cancellationToken);
        if (guarded is null) return ContactOperationResult<ContactMutationResponse>.Failure(ContactErrors.NotFound());
        var guardError = await authorization.EnforceRecordAsync(access.Value, guarded, "archiveContact", requestMetadata, cancellationToken);
        if (guardError is not null) return ContactOperationResult<ContactMutationResponse>.Failure(guardError);
        var fingerprint = ContactMutationSupport.Fingerprint(new { command.ContactId, command.Metadata.ExpectedVersion });
        var scopeKey = ContactMutationSupport.ScopeKey(trusted, "archiveContact", command.ContactId, command.Metadata.IdempotencyKey);
        var existing = await persistence.FindIdempotencyAsync(scopeKey, cancellationToken);
        if (existing is not null)
        {
            var replayError = ContactMutationSupport.ReplayError(existing, fingerprint);
            return replayError is null ? ContactOperationResult<ContactMutationResponse>.Success(ContactMutationSupport.Project(ContactMutationSupport.Replay(existing), access.Value)) : ContactOperationResult<ContactMutationResponse>.Failure(replayError);
        }
        var writeError = ContactFieldSecurity.GuardWrite(access.Value.Authorization, "status", "archivedAt");
        if (writeError is not null) return ContactOperationResult<ContactMutationResponse>.Failure(writeError);
        var contact = await persistence.LoadContactAsync(trusted.WorkspaceId, command.ContactId, cancellationToken);
        if (contact is null) return ContactOperationResult<ContactMutationResponse>.Failure(ContactErrors.NotFound());
        if (contact.ArchivedAt is not null) return ContactOperationResult<ContactMutationResponse>.Failure(ContactErrors.AlreadyArchived());
        var expected = command.Metadata.ExpectedVersion!.Value;
        if (contact.Version != expected) return ContactOperationResult<ContactMutationResponse>.Failure(ContactErrors.VersionConflict(contact.ContactId, expected, contact.Version));
        var now = timeProvider.GetUtcNow();
        contact.Archive(now);
        var response = ContactMutationSupport.RecordCommit(persistence, contact, trusted, command.Metadata,
            "archiveContact", "CONTACT_ARCHIVED", scopeKey, contact.ContactId, fingerprint, now);
        try
        {
            await persistence.SaveChangesAsync(cancellationToken);
        }
        catch (ContactsPersistenceConcurrencyException)
        {
            return ContactOperationResult<ContactMutationResponse>.Failure(ContactErrors.VersionConflict(contact.ContactId, expected, contact.Version));
        }
        await transaction.CommitAsync(cancellationToken);
        return ContactOperationResult<ContactMutationResponse>.Success(ContactMutationSupport.Project(response, access.Value));
    }
}
