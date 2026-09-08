using UnicoreCRM.Crm.Contacts.Application.Common;
using UnicoreCRM.Crm.Contacts.Contracts;
using UnicoreCRM.Crm.Contacts.Domain;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Contacts.Application.CreateContact;

internal sealed record Command(CreateContactRequest Request, ContactCommandMetadata Metadata);

internal sealed class Handler(
    ContactAuthorization authorization,
    IContactsPersistence persistence,
    IWorkspaceMemberReferenceValidator memberValidator,
    TimeProvider timeProvider)
{
    internal async Task<ContactOperationResult<ContactMutationResponse>> HandleAsync(Command command, CancellationToken cancellationToken)
    {
        var requestMetadata = new ContactRequestMetadata(command.Metadata.RequestId, command.Metadata.CorrelationId);
        var access = await authorization.AuthorizeAsync(requestMetadata, ContactCapabilities.Create, cancellationToken);
        if (!access.IsSuccess) return ContactOperationResult<ContactMutationResponse>.Failure(access.Error!);
        ContactMutationValidation.TryProfile(command.Request, out var fullName, out var ownerId, out var profile, out var errors);
        if (errors.Count != 0) return ContactOperationResult<ContactMutationResponse>.Failure(ContactErrors.Validation(errors));

        var trusted = access.Value!.Trusted;
        var fingerprint = ContactMutationSupport.Fingerprint(new { fullName, ownerId, profile });
        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);
        var scopeKey = ContactMutationSupport.ScopeKey(trusted, "createContact", "WORKSPACE", command.Metadata.IdempotencyKey);
        var existing = await persistence.FindIdempotencyAsync(scopeKey, cancellationToken);
        if (existing is not null)
        {
            var replayError = ContactMutationSupport.ReplayError(existing, fingerprint);
            return replayError is null
                ? ContactOperationResult<ContactMutationResponse>.Success(ContactMutationSupport.Project(ContactMutationSupport.Replay(existing), access.Value))
                : ContactOperationResult<ContactMutationResponse>.Failure(replayError);
        }
        var writeError = ContactFieldSecurity.GuardWrite(access.Value.Authorization,
            "fullName", "ownerId", "salutation", "jobTitle", "department", "roleAtCompany", "workEmail",
            "personalEmail", "mobilePhone", "workPhone", "otherPhone", "zaloId", "facebook",
            "preferredContactChannel", "address", "source", "decisionRole", "relationshipLevel", "painPoint",
            "needSummary", "notes", "tags", "displayName");
        if (writeError is not null) return ContactOperationResult<ContactMutationResponse>.Failure(writeError);
        if (ownerId is not null && !await memberValidator.IsActiveMemberAsync(trusted.WorkspaceId, ownerId, cancellationToken))
            return ContactOperationResult<ContactMutationResponse>.Failure(ContactErrors.Validation(new Dictionary<string, string[]> { ["ownerId"] = ["ownerId must reference an active Workspace member."] }));

        var now = timeProvider.GetUtcNow();
        var contact = new Contact(trusted.WorkspaceId, ownerId, fullName!, "active", profile!, now);
        persistence.AddContact(contact);
        var response = ContactMutationSupport.RecordCommit(persistence, contact, trusted, command.Metadata,
            "createContact", "CONTACT_CREATED", scopeKey, "WORKSPACE", fingerprint, now);
        await persistence.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ContactOperationResult<ContactMutationResponse>.Success(ContactMutationSupport.Project(response, access.Value));
    }
}
