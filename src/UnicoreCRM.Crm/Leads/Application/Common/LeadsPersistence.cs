using UnicoreCRM.Crm.Leads.Domain;

namespace UnicoreCRM.Crm.Leads.Application.Common;

internal interface ILeadsPersistence
{
    Task<ILeadsTransaction> BeginSerializableAsync(CancellationToken cancellationToken);
    Task<Lead?> LoadLeadAsync(string workspaceId, string leadId, CancellationToken cancellationToken);
    Task<Lead?> ReadLeadAsync(string workspaceId, string leadId, CancellationToken cancellationToken);
    /// <param name="scopeOwnerMemberId">
    /// The AccessControl-resolved record-scope owner. When set, only leads owned by that member are
    /// in scope, and the predicate is part of the query rather than a post-filter.
    /// </param>
    Task<IReadOnlyList<Lead>> ListLeadsAsync(string workspaceId, string? scopeOwnerMemberId, CancellationToken cancellationToken);
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
