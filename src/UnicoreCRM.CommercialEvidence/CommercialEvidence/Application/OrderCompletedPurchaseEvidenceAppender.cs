using UnicoreCRM.CommercialEvidence.CommercialEvidence.Contracts;
using UnicoreCRM.CommercialEvidence.CommercialEvidence.Domain;

namespace UnicoreCRM.CommercialEvidence.CommercialEvidence.Application;

internal sealed class OrderCompletedPurchaseEvidenceAppender(
    ICommercialEvidencePersistence persistence,
    IPurchaseEvidenceIdGenerator idGenerator,
    ICommercialEvidencePolicyVersionProvider policyVersionProvider,
    TimeProvider timeProvider) : IOrderCompletedPurchaseEvidenceAppender
{
    private const int MaximumAggregateIdAllocationAttempts = 8;

    public async Task<AppendPurchaseEvidenceResult> AppendAsync(
        AppendOrderCompletedPurchaseEvidenceIntent intent,
        CancellationToken cancellationToken)
    {
        CommercialEvidenceValidation.Validate(intent);
        var workspaceId = intent.TrustedWorkspace.WorkspaceId;
        var buyerRefType = CommercialEvidenceValidation.PersistedBuyerRefType(intent.BuyerRef.Type);
        var occurredAt = intent.OccurredAt.ToUniversalTime();

        var existing = await persistence.FindOriginalByOrderSourceAsync(
            workspaceId,
            intent.OrderId,
            cancellationToken);
        if (existing is not null)
            return ExistingResult(existing, intent.OrderId, buyerRefType, intent.BuyerRef.Id, occurredAt);

        var policyVersion = policyVersionProvider.Current;
        if (string.IsNullOrWhiteSpace(policyVersion) || policyVersion.Length > 128)
            throw new InvalidOperationException("The CommercialEvidence policy version provider returned an invalid value.");

        for (var attempt = 0; attempt < MaximumAggregateIdAllocationAttempts; attempt++)
        {
            var evidenceId = idGenerator.NewEvidenceId();
            CommercialEvidenceValidation.ValidateEvidenceId(evidenceId);
            var evidence = new PurchaseEvidence(
                workspaceId,
                evidenceId,
                CommercialEvidenceVocabulary.OrderCompleted,
                buyerRefType,
                intent.BuyerRef.Id,
                CommercialEvidenceVocabulary.Order,
                null,
                intent.OrderId,
                occurredAt,
                policyVersion,
                intent.CorrelationId);
            var audit = new CommercialEvidenceAuditRecord(
                idGenerator.NewAuditId(),
                workspaceId,
                evidenceId,
                CommercialEvidenceVocabulary.OriginalAppend,
                intent.CorrelationId,
                timeProvider.GetUtcNow(),
                policyVersion);
            persistence.Add(evidence, audit);

            try
            {
                await persistence.SaveChangesAsync(cancellationToken);
                return new(PurchaseEvidenceAppendOutcome.Appended, evidenceId);
            }
            catch (CommercialEvidenceUniqueConflictException exception)
                when (exception.Conflict == CommercialEvidenceUniqueConflict.AggregateIdentity)
            {
                persistence.ClearTrackedChanges();
            }
            catch (CommercialEvidenceUniqueConflictException exception)
                when (exception.Conflict == CommercialEvidenceUniqueConflict.SourceIdentity)
            {
                persistence.ClearTrackedChanges();
                var winner = await persistence.FindOriginalByOrderSourceAsync(
                    workspaceId,
                    intent.OrderId,
                    cancellationToken)
                    ?? throw new InvalidOperationException(
                        "The source uniqueness boundary reported a winner that could not be resolved.",
                        exception);
                return ExistingResult(winner, intent.OrderId, buyerRefType, intent.BuyerRef.Id, occurredAt);
            }
        }

        throw new InvalidOperationException("CommercialEvidence could not allocate a unique evidenceId.");
    }

    private static AppendPurchaseEvidenceResult ExistingResult(
        PurchaseEvidence existing,
        string orderId,
        string buyerRefType,
        string buyerRefId,
        DateTimeOffset occurredAt)
    {
        var matches = string.Equals(existing.EvidenceType, CommercialEvidenceVocabulary.OrderCompleted, StringComparison.Ordinal)
            && string.Equals(existing.BuyerRefType, buyerRefType, StringComparison.Ordinal)
            && string.Equals(existing.BuyerRefId, buyerRefId, StringComparison.Ordinal)
            && string.Equals(existing.SourceType, CommercialEvidenceVocabulary.Order, StringComparison.Ordinal)
            && existing.SourceSystem is null
            && string.Equals(existing.SourceId, orderId, StringComparison.Ordinal)
            && existing.OccurredAt.Equals(occurredAt);
        return new(
            matches ? PurchaseEvidenceAppendOutcome.Replayed : PurchaseEvidenceAppendOutcome.Conflict,
            existing.EvidenceId);
    }
}
