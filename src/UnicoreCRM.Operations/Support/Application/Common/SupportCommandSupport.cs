using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UnicoreCRM.Operations.Support.Contracts;
using UnicoreCRM.Operations.Support.Domain;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Operations.Support.Application.Common;

/// <summary>
/// Shared Support command evidence: idempotency scoping and replay, plus the single place
/// that writes the Support-owned audit, outbox and idempotency records for a committed
/// SupportCase mutation.
/// </summary>
internal static class SupportCommandSupport
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static string ScopeKey(
        TrustedWorkspaceContext trusted,
        string operation,
        string targetId,
        string idempotencyKey) =>
        Hash($"{trusted.WorkspaceId}\n{operation}\n{trusted.MemberId}\n{targetId}\n{idempotencyKey}");

    /// <summary>Fingerprints stable client business intent only.</summary>
    internal static string Fingerprint<T>(T value) => Hash(JsonSerializer.Serialize(value, JsonOptions));

    internal static SupportOperationError? ReplayError(SupportIdempotencyRecord existing, string fingerprint) =>
        existing.Fingerprint == fingerprint ? null : SupportErrors.IdempotencyReused(existing.IdempotencyKey);

    internal static SupportCaseMutationResponse Replay(SupportIdempotencyRecord record) =>
        (JsonSerializer.Deserialize<SupportCaseMutationResponse>(record.ResponseJson, JsonOptions)
            ?? throw new InvalidOperationException("Stored Support idempotency response is invalid.")) with
        { Outcome = "REPLAYED" };

    internal static SupportCaseMutationResponse RecordCommit(
        ISupportPersistence persistence,
        SupportCase supportCase,
        TrustedWorkspaceContext trusted,
        SupportCommandMetadata metadata,
        string operation,
        string eventType,
        string scopeKey,
        string targetId,
        string fingerprint,
        long? priorVersion,
        DateTimeOffset now)
    {
        var audit = new SupportAuditRecord(
            operation,
            trusted.WorkspaceId,
            trusted.MemberId,
            supportCase.CaseId,
            metadata.RequestId,
            metadata.CorrelationId,
            "COMMITTED",
            priorVersion,
            supportCase.Version,
            now);
        var message = new SupportOutboxMessage(
            eventType,
            supportCase.CaseId,
            trusted.WorkspaceId,
            metadata.CorrelationId,
            JsonSerializer.Serialize(new { caseId = supportCase.CaseId, resourceVersion = supportCase.Version }, JsonOptions),
            now);
        var response = new SupportCaseMutationResponse(
            SupportIds.New("command"),
            metadata.CorrelationId,
            supportCase.CaseId,
            "SUPPORT_CASE",
            supportCase.Version,
            SupportProjection.Utc(now),
            "COMMITTED",
            new SupportCaseMutationResult(SupportProjection.Case(supportCase)),
            [],
            [message.EventId],
            [audit.AuditId]);
        persistence.AddAudit(audit);
        persistence.AddOutbox(message);
        persistence.AddIdempotency(new SupportIdempotencyRecord(
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

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
