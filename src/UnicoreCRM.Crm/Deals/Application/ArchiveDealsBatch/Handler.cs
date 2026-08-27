using System.Text.Json;
using UnicoreCRM.Crm.Deals.Application.Common;
using UnicoreCRM.Crm.Deals.Contracts;
using UnicoreCRM.Crm.Deals.Domain;

namespace UnicoreCRM.Crm.Deals.Application.ArchiveDealsBatch;

internal sealed record Command(ArchiveDealsBatchRequest Request, DealCommandMetadata Metadata);

internal sealed class Handler(
    DealAuthorization authorization,
    IDealsPersistence persistence,
    TimeProvider timeProvider)
{
    internal async Task<DealOperationResult<DealBatchMutationResponse>> HandleAsync(
        Command command,
        CancellationToken cancellationToken)
    {
        var metadata = new DealRequestMetadata(command.Metadata.RequestId, command.Metadata.CorrelationId);
        var access = await authorization.AuthorizeAsync(DealCapabilities.Bulk, metadata, cancellationToken);
        if (!access.IsSuccess)
            return DealOperationResult<DealBatchMutationResponse>.Failure(access.Error!);

        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (command.Request.Items is null || command.Request.Items.Count == 0)
            return DealOperationResult<DealBatchMutationResponse>.Failure(DealErrors.BatchEmpty());
        if (command.Request.Items.Count > 250)
            fields["items"] = ["items cannot contain more than 250 Deals."];
        var reason = DealValidation.RequiredText(command.Request.Reason, "reason", 500, fields);
        var normalizedItems = new List<(string DealId, long ExpectedVersion)>(command.Request.Items.Count);
        for (var index = 0; index < command.Request.Items.Count; index++)
        {
            var item = command.Request.Items[index];
            if (!DealValidation.IsEntityId(item.DealId))
                fields[$"items[{index}].dealId"] = ["dealId is not a valid entity identifier."];
            if (item.ExpectedVersion is null || item.ExpectedVersion < 0)
                fields[$"items[{index}].expectedVersion"] = ["expectedVersion must be a non-negative integer."];
            if (DealValidation.IsEntityId(item.DealId) && item.ExpectedVersion >= 0)
                normalizedItems.Add((item.DealId!, item.ExpectedVersion.Value));
        }
        if (normalizedItems.Select(item => item.DealId).Distinct(StringComparer.Ordinal).Count() != normalizedItems.Count)
            fields["items"] = ["items cannot contain duplicate Deal identifiers."];
        if (fields.Count != 0)
            return DealOperationResult<DealBatchMutationResponse>.Failure(DealErrors.Validation(fields));

        var trusted = access.Value!.Trusted;
        var fingerprint = DealCommandSupport.Fingerprint(new { Items = normalizedItems, Reason = reason });
        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);
        var scopeKey = DealCommandSupport.ScopeKey(trusted, "archiveDealsBatch", "WORKSPACE", command.Metadata.IdempotencyKey);
        var existing = await persistence.FindIdempotencyAsync(scopeKey, cancellationToken);
        if (existing is not null)
        {
            var replayError = DealCommandSupport.ReplayError(existing, fingerprint);
            return replayError is null
                ? DealOperationResult<DealBatchMutationResponse>.Success(DealCommandSupport.ReplayBatch(existing))
                : DealOperationResult<DealBatchMutationResponse>.Failure(replayError);
        }

        var dealIds = normalizedItems.Select(item => item.DealId).ToArray();
        var deals = await persistence.LoadDealsAsync(trusted.WorkspaceId, dealIds, cancellationToken);
        if (deals.Count != normalizedItems.Count)
            return DealOperationResult<DealBatchMutationResponse>.Failure(DealErrors.NotFound());
        var byId = deals.ToDictionary(deal => deal.DealId, StringComparer.Ordinal);

        // Every record the batch names is authorized individually. That is one decision per named
        // record, not an N+1 over a list: the caller enumerated these records, and the resource
        // authorization above still happened exactly once for the whole request.
        foreach (var item in normalizedItems)
        {
            var denied = await authorization.EnforceRecordAsync(
                access.Value!, byId[item.DealId], "archiveDealsBatch", metadata, cancellationToken,
                "archivedAt", "archiveReason");
            if (denied is not null)
                return DealOperationResult<DealBatchMutationResponse>.Failure(denied);
        }

        foreach (var item in normalizedItems)
        {
            var deal = byId[item.DealId];
            if (deal.Version != item.ExpectedVersion)
            {
                return DealOperationResult<DealBatchMutationResponse>.Failure(
                    DealErrors.BatchVersionConflict(deal.DealId, item.ExpectedVersion, deal.Version));
            }
            if (deal.IsArchived)
                return DealOperationResult<DealBatchMutationResponse>.Failure(DealErrors.LifecycleConflict(deal.DealId));
        }

        var now = timeProvider.GetUtcNow();
        var auditIds = new List<string>(deals.Count);
        foreach (var item in normalizedItems)
        {
            var deal = byId[item.DealId];
            var priorVersion = deal.Version;
            if (!deal.Archive(reason!, now))
                return DealOperationResult<DealBatchMutationResponse>.Failure(DealErrors.LifecycleConflict(deal.DealId));
            var audit = new DealAuditRecord(
                "archiveDealsBatch",
                trusted.WorkspaceId,
                trusted.MemberId,
                deal.DealId,
                command.Metadata.RequestId,
                command.Metadata.CorrelationId,
                "COMMITTED",
                priorVersion,
                deal.Version,
                now);
            persistence.AddAudit(audit);
            auditIds.Add(audit.AuditId);
        }

        var orderedDeals = normalizedItems.Select(item => byId[item.DealId]).ToArray();
        var batchId = DealIds.New("deal_batch");
        var outbox = new DealOutboxMessage(
            "DEALS_ARCHIVED_BATCH",
            batchId,
            trusted.WorkspaceId,
            command.Metadata.CorrelationId,
            JsonSerializer.Serialize(
                new
                {
                    batchId,
                    deals = orderedDeals.Select(deal => new { dealId = deal.DealId, resourceVersion = deal.Version })
                },
                DealCommandSupport.SerializationOptions),
            now);
        persistence.AddOutbox(outbox);
        var response = new DealBatchMutationResponse(
            DealIds.New("command"),
            command.Metadata.CorrelationId,
            batchId,
            "DEAL",
            orderedDeals.Max(deal => deal.Version),
            DealProjection.Utc(now),
            "COMMITTED",
            new DealBatchMutationResult(orderedDeals.Select(DealProjection.Document).ToArray()),
            [],
            [outbox.EventId],
            auditIds);
        persistence.AddIdempotency(new DealIdempotencyRecord(
            scopeKey,
            trusted.WorkspaceId,
            "archiveDealsBatch",
            trusted.MemberId,
            "WORKSPACE",
            command.Metadata.IdempotencyKey,
            fingerprint,
            JsonSerializer.Serialize(response, DealCommandSupport.SerializationOptions),
            now));
        try
        {
            await persistence.SaveChangesAsync(cancellationToken);
        }
        catch (DealsPersistenceConcurrencyException)
        {
            var first = normalizedItems[0];
            return DealOperationResult<DealBatchMutationResponse>.Failure(
                DealErrors.BatchVersionConflict(first.DealId, first.ExpectedVersion, byId[first.DealId].Version));
        }
        await transaction.CommitAsync(cancellationToken);
        return DealOperationResult<DealBatchMutationResponse>.Success(response);
    }
}
