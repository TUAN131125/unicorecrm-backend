using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Products.Contracts;
using UnicoreCRM.Sales.Products.Domain;

namespace UnicoreCRM.Sales.Products.Application.Common;

internal static class ProductCommandSupport
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static string ScopeKey(
        TrustedWorkspaceContext trusted,
        string operation,
        string targetId,
        string idempotencyKey) =>
        Hash($"{trusted.WorkspaceId}\n{operation}\n{trusted.MemberId}\n{targetId}\n{idempotencyKey}");

    internal static string Fingerprint<T>(T value) => Hash(JsonSerializer.Serialize(value, JsonOptions));

    internal static ProductOperationError? ReplayError(ProductIdempotencyRecord existing, string fingerprint) =>
        existing.Fingerprint == fingerprint ? null : ProductErrors.IdempotencyReused(existing.IdempotencyKey);

    internal static ProductMutationResponse Replay(ProductIdempotencyRecord record) =>
        (JsonSerializer.Deserialize<ProductMutationResponse>(record.ResponseJson, JsonOptions)
            ?? throw new InvalidOperationException("Stored Products idempotency response is invalid.")) with
        { Outcome = "REPLAYED" };

    /// <summary>
    /// Replays a stored Product Configuration mutation. The committed document, revision and ETag are
    /// answered from the stored evidence alone, so a replay stays byte-identical after the Workspace
    /// configuration has moved on, and the outcome is restated as REPLAYED rather than COMMITTED.
    /// </summary>
    internal static ProductConfigurationMutationResponse ReplayConfiguration(ProductIdempotencyRecord record) =>
        (JsonSerializer.Deserialize<ProductConfigurationMutationResponse>(record.ResponseJson, JsonOptions)
            ?? throw new InvalidOperationException("Stored Product Configuration idempotency response is invalid.")) with
        { Outcome = "REPLAYED" };

    internal static ProductBatchMutationResponse ReplayBatch(ProductIdempotencyRecord record) =>
        (JsonSerializer.Deserialize<ProductBatchMutationResponse>(record.ResponseJson, JsonOptions)
            ?? throw new InvalidOperationException("Stored Products batch idempotency response is invalid.")) with
        { Outcome = "REPLAYED" };

    internal static ProductMutationResponse RecordCommit(
        IProductsPersistence persistence,
        Product product,
        TrustedWorkspaceContext trusted,
        ProductCommandMetadata metadata,
        string operation,
        string eventType,
        string scopeKey,
        string targetId,
        string fingerprint,
        long? priorVersion,
        DateTimeOffset now)
    {
        var audit = new ProductAuditRecord(
            operation,
            trusted.WorkspaceId,
            trusted.MemberId,
            product.ProductId,
            metadata.RequestId,
            metadata.CorrelationId,
            "COMMITTED",
            priorVersion,
            product.Version,
            now);
        var message = new ProductOutboxMessage(
            eventType,
            product.ProductId,
            trusted.WorkspaceId,
            metadata.CorrelationId,
            JsonSerializer.Serialize(new { productId = product.ProductId, resourceVersion = product.Version }, JsonOptions),
            now);
        var response = new ProductMutationResponse(
            ProductIds.New("command"),
            metadata.CorrelationId,
            product.ProductId,
            "PRODUCT",
            product.Version,
            ProductProjection.Utc(now),
            "COMMITTED",
            new ProductMutationResult(ProductProjection.Document(product)),
            [],
            [message.EventId],
            [audit.AuditId]);
        persistence.AddAudit(audit);
        persistence.AddOutbox(message);
        persistence.AddIdempotency(new ProductIdempotencyRecord(
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
