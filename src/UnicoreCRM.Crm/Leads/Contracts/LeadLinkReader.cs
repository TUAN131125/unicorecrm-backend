namespace UnicoreCRM.Crm.Leads.Contracts;

// Owner-authorized projections for manual references. No workflow anchors or CustomerRef.
public sealed record LeadLinkTarget(string Id, string? Label, bool Eligible, string? SuccessorContactId);
public interface ILeadLinkReader
{
    Task<LeadLinkTarget?> ReadAsync(string id, string requestId, string correlationId, CancellationToken ct);
    Task<IReadOnlyList<LeadLinkTarget>> SearchAsync(string search, string requestId, string correlationId, CancellationToken ct);
}
