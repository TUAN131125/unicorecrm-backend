using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UnicoreCRM.Crm.Contacts.Contracts;
using UnicoreCRM.Crm.Contacts.Domain;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Contacts.Application.Common;

internal static class ContactMutationSupport
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static string Fingerprint<T>(T value) => Hash(JsonSerializer.Serialize(value, JsonOptions));

    internal static string ScopeKey(TrustedWorkspaceContext trusted, string operation, string targetId, string key) =>
        Hash($"{trusted.WorkspaceId}\n{operation}\n{trusted.MemberId}\n{targetId}\n{key}");

    internal static ContactOperationError? ReplayError(ContactIdempotencyRecord record, string fingerprint) =>
        record.Fingerprint == fingerprint ? null : ContactErrors.IdempotencyReused();

    internal static ContactMutationResponse Replay(ContactIdempotencyRecord record) =>
        (JsonSerializer.Deserialize<ContactMutationResponse>(record.ResponseJson, JsonOptions)
            ?? throw new InvalidOperationException("Stored Contact idempotency response is invalid.")) with
        { Outcome = "REPLAYED" };

    internal static ContactMutationResponse Project(ContactMutationResponse response, ContactAccess access) =>
        response with
        {
            Result = response.Result with
            {
                Contact = ContactFieldSecurity.Project(response.Result.Contact, access.Authorization)
            }
        };

    internal static ContactMutationResponse RecordCommit(
        IContactsPersistence persistence,
        Contact contact,
        TrustedWorkspaceContext trusted,
        ContactCommandMetadata metadata,
        string operation,
        string eventType,
        string scopeKey,
        string targetId,
        string fingerprint,
        DateTimeOffset now,
        ContactOrganizationRelationshipSummaryDocument? organizationRelationship = null,
        ContactCustomerRelationshipSummaryDocument? customerRelationship = null)
    {
        var audit = new ContactAuditRecord(operation, trusted.WorkspaceId, trusted.MemberId, contact.ContactId,
            metadata.RequestId, metadata.CorrelationId, "COMMITTED", contact.Version, now);
        object eventData = organizationRelationship is not null
            ? new { relationshipKind = "CONTACT_ORGANIZATION", changeType = RelationshipChange(eventType), contactId = contact.ContactId, relationshipId = organizationRelationship.RelationshipId, targetType = "ORGANIZATION", targetId = organizationRelationship.Target.RecordId, contactVersion = contact.Version }
            : customerRelationship is not null
                ? new { relationshipKind = "CONTACT_CUSTOMER", changeType = RelationshipChange(eventType), contactId = contact.ContactId, relationshipId = customerRelationship.RelationshipId, targetType = "CUSTOMER", targetId = customerRelationship.Target.RecordId, contactVersion = contact.Version }
                : new { contactId = contact.ContactId, changeType = ContactChange(eventType), resourceVersion = contact.Version };
        var message = new ContactOutboxMessage(eventType, contact.ContactId, trusted.WorkspaceId,
            metadata.CorrelationId, JsonSerializer.Serialize(eventData, JsonOptions), now);
        var response = new ContactMutationResponse(ContactIds.New("command"), metadata.CorrelationId,
            contact.ContactId, "CONTACT", contact.Version, ContactProjection.TimestampValue(now), "COMMITTED",
            new ContactMutationResult(ContactProjection.Document(contact))
            {
                OrganizationRelationship = organizationRelationship,
                CustomerRelationship = customerRelationship
            }, [], [message.EventId], [audit.AuditId]);
        persistence.AddAudit(audit);
        persistence.AddOutbox(message);
        persistence.AddIdempotency(new ContactIdempotencyRecord(scopeKey, trusted.WorkspaceId, operation,
            trusted.MemberId, targetId, metadata.IdempotencyKey, fingerprint,
            JsonSerializer.Serialize(response, JsonOptions), now));
        return response;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string ContactChange(string eventType) => eventType switch
    { "CONTACT_CREATED" => "CREATED", "CONTACT_UPDATED" => "UPDATED", "CONTACT_ARCHIVED" => "ARCHIVED", _ => eventType };

    private static string RelationshipChange(string eventType) => eventType.EndsWith("_CREATED", StringComparison.Ordinal)
        ? "CREATED" : eventType.EndsWith("_UPDATED", StringComparison.Ordinal) ? "UPDATED" : "ENDED";
}
