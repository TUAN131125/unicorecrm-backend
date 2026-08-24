using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UnicoreCRM.Crm.Deals.Contracts;
using UnicoreCRM.Crm.Deals.Domain;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Deals.Application.Common;

internal static class DealCommandSupport
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static string ScopeKey(
        TrustedWorkspaceContext trusted,
        string operation,
        string targetId,
        string idempotencyKey) =>
        Hash($"{trusted.WorkspaceId}\n{operation}\n{trusted.MemberId}\n{targetId}\n{idempotencyKey}");

    internal static string Fingerprint<T>(T value) => Hash(JsonSerializer.Serialize(value, JsonOptions));

    internal static DealOperationError? ReplayError(DealIdempotencyRecord existing, string fingerprint) =>
        existing.Fingerprint == fingerprint ? null : DealErrors.IdempotencyReused(existing.IdempotencyKey);

    internal static DealMutationResponse Replay(DealIdempotencyRecord record) =>
        (JsonSerializer.Deserialize<DealMutationResponse>(record.ResponseJson, JsonOptions)
            ?? throw new InvalidOperationException("Stored Deals idempotency response is invalid.")) with
        { Outcome = "REPLAYED" };

    internal static DealBatchMutationResponse ReplayBatch(DealIdempotencyRecord record) =>
        (JsonSerializer.Deserialize<DealBatchMutationResponse>(record.ResponseJson, JsonOptions)
            ?? throw new InvalidOperationException("Stored Deals batch idempotency response is invalid.")) with
        { Outcome = "REPLAYED" };

    internal static DealMutationResponse RecordCommit(
        IDealsPersistence persistence,
        Deal deal,
        TrustedWorkspaceContext trusted,
        DealCommandMetadata metadata,
        string operation,
        string eventType,
        string scopeKey,
        string targetId,
        string fingerprint,
        long? priorVersion,
        DateTimeOffset now)
    {
        var audit = new DealAuditRecord(
            operation,
            trusted.WorkspaceId,
            trusted.MemberId,
            deal.DealId,
            metadata.RequestId,
            metadata.CorrelationId,
            "COMMITTED",
            priorVersion,
            deal.Version,
            now);
        var message = new DealOutboxMessage(
            eventType,
            deal.DealId,
            trusted.WorkspaceId,
            metadata.CorrelationId,
            JsonSerializer.Serialize(new { dealId = deal.DealId, resourceVersion = deal.Version }, JsonOptions),
            now);
        var response = new DealMutationResponse(
            DealIds.New("command"),
            metadata.CorrelationId,
            deal.DealId,
            "DEAL",
            deal.Version,
            DealProjection.Utc(now),
            "COMMITTED",
            new DealMutationResult(DealProjection.Document(deal)),
            [],
            [message.EventId],
            [audit.AuditId]);
        persistence.AddAudit(audit);
        persistence.AddOutbox(message);
        persistence.AddIdempotency(new DealIdempotencyRecord(
            scopeKey,
            trusted.WorkspaceId,
            operation,
            trusted.MemberId,
            targetId,
            metadata.IdempotencyKey,
            fingerprint,
            JsonSerializer.Serialize(response, JsonOptions),
            now));
        return response;
    }

    internal static JsonSerializerOptions SerializationOptions => JsonOptions;

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
