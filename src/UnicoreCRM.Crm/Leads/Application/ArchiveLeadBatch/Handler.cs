using System.Text.Json;
using UnicoreCRM.Crm.Leads.Application.Common;
using UnicoreCRM.Crm.Leads.Contracts;
using UnicoreCRM.Crm.Leads.Domain;

namespace UnicoreCRM.Crm.Leads.Application.ArchiveLeadBatch;

internal sealed record Command(ArchiveLeadBatchRequest Request, LeadCommandMetadata Metadata);

internal sealed class Handler(
    LeadAuthorization authorization,
    ILeadsPersistence persistence,
    TimeProvider timeProvider)
{
    internal async Task<LeadOperationResult<LeadBatchArchiveResponse>> HandleAsync(
        Command command,
        CancellationToken cancellationToken)
    {
        var metadata = new LeadRequestMetadata(command.Metadata.RequestId, command.Metadata.CorrelationId);
        var access = await authorization.AuthorizeAsync(LeadCapabilities.Delete, metadata, cancellationToken);
        if (!access.IsSuccess)
            return LeadOperationResult<LeadBatchArchiveResponse>.Failure(access.Error!);

        if (command.Request.Items is null || command.Request.Items.Count == 0)
            return LeadOperationResult<LeadBatchArchiveResponse>.Failure(LeadErrors.BatchEmpty());

        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (command.Request.Items.Count > 100)
            fields["items"] = ["items cannot contain more than 100 Leads."];

        var reason = string.IsNullOrWhiteSpace(command.Request.Reason) ? null : command.Request.Reason.Trim();
        if (reason?.Length > 2000)
            fields["reason"] = ["reason cannot contain more than 2000 characters."];

        var normalizedItems = new List<(string LeadId, long ExpectedVersion)>(command.Request.Items.Count);
        for (var index = 0; index < command.Request.Items.Count; index++)
        {
            var item = command.Request.Items[index];
            if (!LeadValidation.IsEntityId(item.LeadId))
                fields[$"items[{index}].leadId"] = ["leadId is not a valid entity identifier."];
            if (item.ExpectedVersion is null || item.ExpectedVersion < 0)
                fields[$"items[{index}].expectedVersion"] = ["expectedVersion must be a non-negative integer."];
            if (LeadValidation.IsEntityId(item.LeadId) && item.ExpectedVersion >= 0)
                normalizedItems.Add((item.LeadId!, item.ExpectedVersion.Value));
        }
        if (normalizedItems.Select(item => item.LeadId).Distinct(StringComparer.Ordinal).Count() != normalizedItems.Count)
            fields["items"] = ["items cannot contain duplicate Lead identifiers."];
        if (fields.Count != 0)
            return LeadOperationResult<LeadBatchArchiveResponse>.Failure(LeadErrors.Validation(fields));

        var trusted = access.Value!.Trusted;
        var fingerprint = LeadCommandSupport.Fingerprint(new { Items = normalizedItems, Reason = reason });
        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);

        var leadIds = normalizedItems.Select(item => item.LeadId).ToArray();
        var leads = await persistence.LoadLeadsAsync(trusted.WorkspaceId, leadIds, cancellationToken);
        if (leads.Count != normalizedItems.Count)
            return LeadOperationResult<LeadBatchArchiveResponse>.Failure(LeadErrors.NotFound());
        var byId = leads.ToDictionary(lead => lead.LeadId, StringComparer.Ordinal);

        foreach (var item in normalizedItems)
        {
            var denied = await authorization.EnforceRecordAsync(
                access.Value!, byId[item.LeadId], "archiveLeadBatch", metadata, cancellationToken);
            if (denied is not null)
                return LeadOperationResult<LeadBatchArchiveResponse>.Failure(denied);
        }

        var scopeKey = LeadCommandSupport.ScopeKey(
            trusted, "archiveLeadBatch", "WORKSPACE", command.Metadata);
        var existing = await persistence.FindIdempotencyAsync(scopeKey, cancellationToken);
        if (existing is not null)
        {
            var replayError = LeadCommandSupport.ReplayError(existing, fingerprint);
            return replayError is null
                ? LeadOperationResult<LeadBatchArchiveResponse>.Success(
                    Project(LeadCommandSupport.ReplayBatchArchive(existing), access.Value!))
                : LeadOperationResult<LeadBatchArchiveResponse>.Failure(replayError);
        }

        var fieldWriteError = LeadAuthorization.EnforceFieldWrite(
            access.Value!, "archivedAt", "archiveReason");
        if (fieldWriteError is not null)
            return LeadOperationResult<LeadBatchArchiveResponse>.Failure(fieldWriteError);

        foreach (var item in normalizedItems)
        {
            var lead = byId[item.LeadId];
            if (lead.Version != item.ExpectedVersion)
            {
                return LeadOperationResult<LeadBatchArchiveResponse>.Failure(
                    LeadErrors.BatchVersionConflict(lead.LeadId, item.ExpectedVersion, lead.Version));
            }
            if (lead.ArchivedAt is not null)
                return LeadOperationResult<LeadBatchArchiveResponse>.Failure(LeadErrors.AlreadyArchived(lead.LeadId));
        }

        var now = timeProvider.GetUtcNow();
        var actorId = command.Metadata.ActorId ?? trusted.MemberId;
        var auditIds = new List<string>(normalizedItems.Count);
        foreach (var item in normalizedItems)
        {
            var lead = byId[item.LeadId];
            var priorVersion = lead.Version;
            if (!lead.Archive(reason, now))
                return LeadOperationResult<LeadBatchArchiveResponse>.Failure(LeadErrors.AlreadyArchived(lead.LeadId));
            var audit = new LeadAuditRecord(
                "archiveLeadBatch",
                trusted.WorkspaceId,
                actorId,
                lead.LeadId,
                command.Metadata.RequestId,
                command.Metadata.CorrelationId,
                "COMMITTED",
                priorVersion,
                lead.Version,
                now,
                command.Metadata.ActorType,
                command.Metadata.DelegatedSubjectId,
                command.Metadata.SourceReference);
            persistence.AddAudit(audit);
            auditIds.Add(audit.AuditId);
        }

        var orderedLeads = normalizedItems.Select(item => byId[item.LeadId]).ToArray();
        var batchId = LeadIds.New("lead_batch");
        var outbox = new LeadOutboxMessage(
            "LEAD_BATCH_ARCHIVED",
            batchId,
            trusted.WorkspaceId,
            command.Metadata.CorrelationId,
            JsonSerializer.Serialize(
                new
                {
                    batchId,
                    leads = orderedLeads.Select(lead => new { leadId = lead.LeadId, resourceVersion = lead.Version })
                },
                LeadCommandSupport.SerializationOptions),
            now);
        persistence.AddOutbox(outbox);

        var response = new LeadBatchArchiveResponse(
            LeadIds.New("command"),
            command.Metadata.CorrelationId,
            batchId,
            "LEAD",
            orderedLeads.Max(lead => lead.Version),
            LeadProjection.Utc(now),
            "COMMITTED",
            new LeadBatchArchiveResult(orderedLeads.Select(LeadProjection.Document).ToArray()),
            [],
            [outbox.EventId],
            auditIds);
        persistence.AddIdempotency(new LeadIdempotencyRecord(
            scopeKey,
            trusted.WorkspaceId,
            "archiveLeadBatch",
            actorId,
            "WORKSPACE",
            command.Metadata.IdempotencyKey,
            fingerprint,
            JsonSerializer.Serialize(response, LeadCommandSupport.SerializationOptions),
            now));

        try
        {
            await persistence.SaveChangesAsync(cancellationToken);
        }
        catch (LeadsPersistenceConcurrencyException)
        {
            var first = normalizedItems[0];
            return LeadOperationResult<LeadBatchArchiveResponse>.Failure(
                LeadErrors.BatchVersionConflict(first.LeadId, first.ExpectedVersion, byId[first.LeadId].Version));
        }
        await transaction.CommitAsync(cancellationToken);
        return LeadOperationResult<LeadBatchArchiveResponse>.Success(Project(response, access.Value!));
    }

    private static LeadBatchArchiveResponse Project(LeadBatchArchiveResponse response, LeadAccess access) =>
        response with
        {
            Result = new LeadBatchArchiveResult(
                response.Result.Leads.Select(lead => LeadFieldSecurity.Project(lead, access.Authorization)).ToArray())
        };
}
