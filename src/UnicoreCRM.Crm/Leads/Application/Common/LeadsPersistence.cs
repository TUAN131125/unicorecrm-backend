using UnicoreCRM.Crm.Leads.Domain;

namespace UnicoreCRM.Crm.Leads.Application.Common;

internal interface ILeadsPersistence
{
    Task<ILeadsTransaction> BeginSerializableAsync(CancellationToken cancellationToken);
    Task<Lead?> LoadLeadAsync(string workspaceId, string leadId, CancellationToken cancellationToken);
    Task<Lead?> ReadLeadAsync(string workspaceId, string leadId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Lead>> ListLeadsAsync(string workspaceId, CancellationToken cancellationToken);
    Task<LeadIdempotencyRecord?> FindIdempotencyAsync(string scopeKey, CancellationToken cancellationToken);
    void AddLead(Lead lead);
    void AddIdempotency(LeadIdempotencyRecord record);
    void AddAudit(LeadAuditRecord audit);
    void AddOutbox(LeadOutboxMessage message);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

internal interface ILeadsTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}

internal sealed class LeadsPersistenceConcurrencyException(Exception innerException)
    : Exception("The Lead resource changed concurrently.", innerException);
