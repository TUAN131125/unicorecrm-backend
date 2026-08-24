using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UnicoreCRM.Crm.Leads.Contracts;
using UnicoreCRM.Crm.Leads.Domain;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Leads.Application.Common;

internal static class LeadCommandSupport
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static string ScopeKey(
        TrustedWorkspaceContext trusted,
        string operation,
        string targetId,
        LeadCommandMetadata metadata) =>
        Hash($"{trusted.WorkspaceId}\n{operation}\n{metadata.ActorId ?? trusted.MemberId}\n{targetId}\n{metadata.IdempotencyKey}");

    internal static string Fingerprint<T>(T value) => Hash(JsonSerializer.Serialize(value, JsonOptions));

    internal static LeadOperationError? ReplayError(LeadIdempotencyRecord existing, string fingerprint) =>
        existing.Fingerprint == fingerprint ? null : LeadErrors.IdempotencyReused(existing.IdempotencyKey);

    internal static LeadMutationResponse Replay(LeadIdempotencyRecord record) =>
        (JsonSerializer.Deserialize<LeadMutationResponse>(record.ResponseJson, JsonOptions)
            ?? throw new InvalidOperationException("Stored Leads idempotency response is invalid.")) with
        { Outcome = "REPLAYED" };

    internal static LeadMutationResponse RecordCommit(
        ILeadsPersistence persistence,
        Lead lead,
        TrustedWorkspaceContext trusted,
        LeadCommandMetadata metadata,
        string operation,
        string eventType,
        string scopeKey,
        string targetId,
        string fingerprint,
        long? priorVersion,
        DateTimeOffset now)
    {
        var actorId = metadata.ActorId ?? trusted.MemberId;
        var audit = new LeadAuditRecord(
            operation,
            trusted.WorkspaceId,
            actorId,
            lead.LeadId,
            metadata.RequestId,
            metadata.CorrelationId,
            "COMMITTED",
            priorVersion,
            lead.Version,
            now,
            metadata.ActorType,
            metadata.DelegatedSubjectId,
            metadata.SourceReference);
        var message = new LeadOutboxMessage(
            eventType,
            lead.LeadId,
            trusted.WorkspaceId,
            metadata.CorrelationId,
            JsonSerializer.Serialize(new { leadId = lead.LeadId, resourceVersion = lead.Version }, JsonOptions),
            now);
        var response = new LeadMutationResponse(
            LeadIds.New("command"),
            metadata.CorrelationId,
            lead.LeadId,
            "LEAD",
            lead.Version,
            LeadProjection.Utc(now),
            "COMMITTED",
            LeadProjection.Document(lead),
            [],
            [message.EventId],
            [audit.AuditId]);
        persistence.AddAudit(audit);
        persistence.AddOutbox(message);
        persistence.AddIdempotency(new LeadIdempotencyRecord(
            scopeKey,
            trusted.WorkspaceId,
            operation,
            actorId,
            targetId,
            metadata.IdempotencyKey,
            fingerprint,
            JsonSerializer.Serialize(response, JsonOptions),
            now));
        return response;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
