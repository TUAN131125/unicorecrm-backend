using UnicoreCRM.Crm.Leads.Domain;

namespace UnicoreCRM.Crm.Leads.Application.Common;

internal interface ILeadKanbanPersistence
{
    Task<IReadOnlyList<Lead>> ListColumnAsync(string workspaceId, string? scopeOwnerMemberId,
        string? ownerId, string? assignmentState, bool canReadUnassigned, string column,
        LeadWorkState? workState, string? normalizedSearch, bool includePhoneSearch,
        DateTimeOffset? cursorUpdatedAt, string? cursorLeadId, int take, CancellationToken cancellationToken);
    Task<long> CountColumnAsync(string workspaceId, string? scopeOwnerMemberId,
        string? ownerId, string? assignmentState, bool canReadUnassigned, string column,
        LeadWorkState? workState, string? normalizedSearch, bool includePhoneSearch, CancellationToken cancellationToken);
}
