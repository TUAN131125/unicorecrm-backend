namespace UnicoreCRM.Crm.Contacts.Contracts;

public enum ContactSummaryReadStatus { Succeeded, AccessDenied, WorkspaceMismatch, InvalidReference, NotFound }
public sealed record ContactSummaryProjection(string ContactId, string? DisplayName, string? Status, string? JobTitle, string? RelationshipLevel, string? NeedSummary, long Version);
public sealed record ContactSummaryReadResult(ContactSummaryReadStatus Status, ContactSummaryProjection? Summary = null);
public interface IContactSummaryReader
{
    Task<ContactSummaryReadResult> ReadAsync(string contactId, string requestId, string correlationId, CancellationToken cancellationToken);
}
