using System.Text.Json;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Sales.Products.Contracts;
using UnicoreCRM.Sales.Products.Domain;

namespace UnicoreCRM.Sales.Products.Application.Common;

internal enum ProductBatchMutationKind { Archive, Restore }

internal sealed class ProductBatchMutationExecution(
    ProductAuthorization authorization,
    IProductsPersistence persistence,
    TimeProvider timeProvider)
{
    internal async Task<ProductOperationResult<ProductBatchMutationResponse>> ExecuteAsync(
        IReadOnlyList<ProductVersionItem>? suppliedItems,
        string? suppliedReason,
        ProductCommandMetadata metadata,
        ProductBatchMutationKind kind,
        CancellationToken cancellationToken)
    {
        var specification = Specification(kind);
        var access = await authorization.AuthorizeAsync(
            specification.Requirement,
            metadata.CorrelationId,
            cancellationToken);
        if (!access.IsSuccess)
            return ProductOperationResult<ProductBatchMutationResponse>.Failure(access.Error!);

        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (suppliedItems is null || suppliedItems.Count == 0)
            fields["items"] = ["items must contain at least one Product."];
        else if (suppliedItems.Count > 500)
            fields["items"] = ["items cannot contain more than 500 Products."];

        var normalizedItems = new List<(string ProductId, long ExpectedVersion)>(suppliedItems?.Count ?? 0);
        if (suppliedItems is not null)
        {
            for (var index = 0; index < suppliedItems.Count; index++)
            {
                var item = suppliedItems[index];
                if (!ProductValidation.IsEntityId(item.ProductId))
                    fields[$"items[{index}].productId"] = ["productId is not a valid entity identifier."];
                if (item.ExpectedVersion is null || item.ExpectedVersion < 0)
                    fields[$"items[{index}].expectedVersion"] = ["expectedVersion must be a non-negative integer."];
                if (ProductValidation.IsEntityId(item.ProductId) && item.ExpectedVersion >= 0)
                    normalizedItems.Add((item.ProductId!, item.ExpectedVersion.Value));
            }
        }
        if (normalizedItems.Select(item => item.ProductId).Distinct(StringComparer.Ordinal).Count() != normalizedItems.Count)
            fields["items"] = ["items cannot contain duplicate Product identifiers."];

        var reason = kind == ProductBatchMutationKind.Archive
            ? ProductValidation.RequiredText(suppliedReason, "reason", 1000, fields)
            : ProductValidation.OptionalText(suppliedReason, "reason", 1000, fields);
        if (fields.Count != 0)
            return ProductOperationResult<ProductBatchMutationResponse>.Failure(ProductErrors.Validation(fields));

        var trusted = access.Value!;
        var fingerprint = ProductCommandSupport.Fingerprint(new { Items = normalizedItems, Reason = reason });
        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);
        var scopeKey = ProductCommandSupport.ScopeKey(trusted, specification.Operation, "WORKSPACE", metadata.IdempotencyKey);
        var existing = await persistence.FindIdempotencyAsync(scopeKey, cancellationToken);
        if (existing is not null)
        {
            var replayError = ProductCommandSupport.ReplayError(existing, fingerprint);
            return replayError is null
                ? ProductOperationResult<ProductBatchMutationResponse>.Success(ProductCommandSupport.ReplayBatch(existing))
                : ProductOperationResult<ProductBatchMutationResponse>.Failure(replayError);
        }

        var productIds = normalizedItems.Select(item => item.ProductId).ToArray();
        var products = await persistence.LoadProductsAsync(productIds, cancellationToken);
        if (products.Count != normalizedItems.Count)
            return ProductOperationResult<ProductBatchMutationResponse>.Failure(ProductErrors.NotFound());
        if (products.Any(product => !string.Equals(product.WorkspaceId, trusted.WorkspaceId, StringComparison.Ordinal)))
            return ProductOperationResult<ProductBatchMutationResponse>.Failure(ProductErrors.WorkspaceMismatch());

        var byId = products.ToDictionary(product => product.ProductId, StringComparer.Ordinal);
        foreach (var item in normalizedItems)
        {
            var product = byId[item.ProductId];
            if (product.Version != item.ExpectedVersion)
            {
                return ProductOperationResult<ProductBatchMutationResponse>.Failure(
                    ProductErrors.VersionConflict(product.ProductId, item.ExpectedVersion, product.Version));
            }
            if (kind == ProductBatchMutationKind.Archive && product.IsArchived)
                return ProductOperationResult<ProductBatchMutationResponse>.Failure(ProductErrors.ArchiveBlocked(product.ProductId));
            if (kind == ProductBatchMutationKind.Restore && !product.IsArchived)
                return ProductOperationResult<ProductBatchMutationResponse>.Failure(ProductErrors.RestoreBlocked(product.ProductId));
        }

        var now = timeProvider.GetUtcNow();
        var auditIds = new List<string>(products.Count);
        foreach (var item in normalizedItems)
        {
            var product = byId[item.ProductId];
            var priorVersion = product.Version;
            var changed = kind == ProductBatchMutationKind.Archive
                ? product.Archive(reason!, now)
                : product.Restore(now);
            if (!changed)
            {
                return ProductOperationResult<ProductBatchMutationResponse>.Failure(
                    kind == ProductBatchMutationKind.Archive
                        ? ProductErrors.ArchiveBlocked(product.ProductId)
                        : ProductErrors.RestoreBlocked(product.ProductId));
            }
            var audit = new ProductAuditRecord(
                specification.Operation,
                trusted.WorkspaceId,
                trusted.MemberId,
                product.ProductId,
                metadata.RequestId,
                metadata.CorrelationId,
                "COMMITTED",
                priorVersion,
                product.Version,
                now);
            persistence.AddAudit(audit);
            auditIds.Add(audit.AuditId);
        }

        var orderedProducts = normalizedItems.Select(item => byId[item.ProductId]).ToArray();
        var batchId = ProductIds.New("product_batch");
        var outbox = new ProductOutboxMessage(
            specification.EventType,
            batchId,
            trusted.WorkspaceId,
            metadata.CorrelationId,
            JsonSerializer.Serialize(
                new
                {
                    batchId,
                    products = orderedProducts.Select(product => new
                    {
                        productId = product.ProductId,
                        resourceVersion = product.Version
                    })
                },
                ProductCommandSupport.SerializationOptions),
            now);
        persistence.AddOutbox(outbox);
        var response = new ProductBatchMutationResponse(
            ProductIds.New("command"),
            metadata.CorrelationId,
            batchId,
            "PRODUCT",
            orderedProducts.Max(product => product.Version),
            ProductProjection.Utc(now),
            "COMMITTED",
            new ProductBatchMutationResult(orderedProducts.Select(ProductProjection.Document).ToArray()),
            [],
            [outbox.EventId],
            auditIds);
        persistence.AddIdempotency(new ProductIdempotencyRecord(
            scopeKey,
            trusted.WorkspaceId,
            specification.Operation,
            trusted.MemberId,
            "WORKSPACE",
            metadata.IdempotencyKey,
            fingerprint,
            JsonSerializer.Serialize(response, ProductCommandSupport.SerializationOptions),
            now));
        try
        {
            await persistence.SaveChangesAsync(cancellationToken);
        }
        catch (ProductsPersistenceConcurrencyException)
        {
            var first = normalizedItems[0];
            return ProductOperationResult<ProductBatchMutationResponse>.Failure(
                ProductErrors.VersionConflict(first.ProductId, first.ExpectedVersion, byId[first.ProductId].Version));
        }
        await transaction.CommitAsync(cancellationToken);
        return ProductOperationResult<ProductBatchMutationResponse>.Success(response);
    }

    private static BatchSpecification Specification(ProductBatchMutationKind kind) => kind switch
    {
        ProductBatchMutationKind.Archive => new(
            ProductCapabilities.Delete,
            "archiveProductsBatch",
            "PRODUCTS_ARCHIVED"),
        _ => new(
            ProductCapabilities.Edit,
            "restoreProductsBatch",
            "PRODUCTS_RESTORED")
    };

    private sealed record BatchSpecification(
        AccessRequirement Requirement,
        string Operation,
        string EventType);
}
