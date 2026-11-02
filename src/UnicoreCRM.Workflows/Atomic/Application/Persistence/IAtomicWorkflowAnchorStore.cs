using UnicoreCRM.Workflows.Atomic.Domain;

namespace UnicoreCRM.Workflows.Atomic.Application.Persistence;

internal sealed record WorkflowRecoveryCandidate(string ScopeKey, string WorkspaceId, string CorrelationId);

/// <summary>
/// Persists only Atomic workflow anchors in one scoped session. Progress writes require the exact
/// tracked anchor returned by a load or create; participant business state stays behind owner ports.
/// Completion writes commit the anchor and its integration event together.
/// </summary>
internal interface IAtomicWorkflowAnchorStore
{
    Task<IAsyncDisposable> LockQualificationAsync(string workspaceId, string leadId, CancellationToken ct);
    Task<bool> HasOtherOpportunityIntentAsync(string workspaceId, string leadId, string scopeKey, CancellationToken ct);
    Task<LeadQualificationAnchor?> FindQualificationAsync(string scopeKey, CancellationToken ct);
    Task<LeadQualificationAnchor> GetQualificationAsync(string scopeKey, CancellationToken ct);
    Task CreateQualificationAsync(LeadQualificationAnchor anchor, CancellationToken ct);
    Task PersistQualificationAsync(LeadQualificationAnchor anchor, CancellationToken ct);

    Task<LeadHandoverAnchor?> FindHandoverReceiptAsync(string scopeKey, CancellationToken ct);
    Task<LeadHandoverAnchor> GetHandoverReceiptAsync(string commandId, string leadId, CancellationToken ct);
    Task<LeadHandoverAnchor?> FindHandoverForUpdateAsync(string scopeKey, CancellationToken ct);
    Task<LeadHandoverAnchor> GetHandoverForUpdateAsync(string scopeKey, CancellationToken ct);
    Task<IReadOnlyList<WorkflowRecoveryCandidate>> FindDueHandoversAsync(DateTimeOffset now, CancellationToken ct);
    Task CreateHandoverAsync(LeadHandoverAnchor anchor, CancellationToken ct);
    Task PersistHandoverAsync(LeadHandoverAnchor anchor, CancellationToken ct);
    Task PersistHandoverCompletionAsync(LeadHandoverAnchor anchor, WorkflowIntegrationOutboxMessage message, CancellationToken ct);

    Task<LeadCustomerConversionAnchor?> FindConversionReceiptAsync(string scopeKey, CancellationToken ct);
    Task<LeadCustomerConversionAnchor?> FindConversionWinnerAsync(string workspaceId, string leadId, CancellationToken ct);
    Task<LeadCustomerConversionAnchor?> FindConversionForUpdateAsync(string scopeKey, CancellationToken ct);
    Task<IReadOnlyList<WorkflowRecoveryCandidate>> FindDueConversionsAsync(DateTimeOffset now, CancellationToken ct);
    Task CreateConversionAsync(LeadCustomerConversionAnchor anchor, CancellationToken ct);
    Task PersistConversionAsync(LeadCustomerConversionAnchor anchor, CancellationToken ct);
    Task PersistConversionCompletionAsync(LeadCustomerConversionAnchor anchor, WorkflowIntegrationOutboxMessage message, CancellationToken ct);

    // Used at the existing retry/reload boundaries, never as a foreign-owner rollback.
    void DiscardPendingAnchorChanges();

    // Preserve the original errors, including participant failures, without provider types in Application.
    bool IsUpdateFailure(Exception exception);
    bool IsConcurrencyConflict(Exception exception);
    bool IsUniqueInsertRace(Exception exception);
    bool IsProviderContention(Exception exception);
}
