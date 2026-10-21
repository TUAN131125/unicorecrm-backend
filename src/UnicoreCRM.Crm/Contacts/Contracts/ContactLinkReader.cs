namespace UnicoreCRM.Crm.Contacts.Contracts;

public sealed record ContactLinkTarget(string Id, string? Label, bool Eligible);
public interface IContactLinkReader
{
    Task<ContactLinkTarget?> ReadAsync(string id, string requestId, string correlationId, CancellationToken ct);
    Task<IReadOnlyList<ContactLinkTarget>> SearchAsync(string search, string requestId, string correlationId, CancellationToken ct);
}
