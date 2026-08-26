using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Products.Application.Common;

namespace UnicoreCRM.Sales.Products.Contracts;

public static class ProductsEndpoints
{
    public static IEndpointRouteBuilder MapProductsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        MapGet(endpoints, "/products", ListProductsAsync, "listProducts");
        MapPost(endpoints, "/products", CreateProductAsync, "createProduct");
        MapPost(endpoints, "/products/archive-batch", ArchiveBatchAsync, "archiveProductsBatch");
        MapPost(endpoints, "/products/restore-batch", RestoreBatchAsync, "restoreProductsBatch");
        MapGet(endpoints, "/products/{productId}", GetProductAsync, "getProduct");
        MapPut(endpoints, "/products/{productId}", ReplaceProductAsync, "replaceProduct");
        MapPost(endpoints, "/products/{productId}/archive", ArchiveProductAsync, "archiveProduct");
        MapPost(endpoints, "/products/{productId}/restore", RestoreProductAsync, "restoreProduct");
        MapGet(endpoints, "/products/{productId}/availability", GetAvailabilityAsync, "getProductAvailability");
        MapGet(endpoints, "/products/{productId}/price-projection", GetPriceProjectionAsync, "getProductPriceProjection");
        return endpoints;
    }

    private static void MapGet(IEndpointRouteBuilder endpoints, string path, Delegate handler, string name) =>
        endpoints.MapGet(path, handler).RequireAuthorization().RequireTrustedWorkspace().WithName(name);

    private static void MapPost(IEndpointRouteBuilder endpoints, string path, Delegate handler, string name) =>
        endpoints.MapPost(path, handler).RequireAuthorization().RequireTrustedWorkspace().WithName(name);

    private static void MapPut(IEndpointRouteBuilder endpoints, string path, Delegate handler, string name) =>
        endpoints.MapPut(path, handler).RequireAuthorization().RequireTrustedWorkspace().WithName(name);

    private static async Task<IResult> ListProductsAsync(
        HttpContext context,
        Application.ListProducts.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!ProductsHttp.TryMetadata(context, false, false, out var metadata, out var error))
            return error!;
        var result = await handler.HandleAsync(
            new Application.ListProducts.Query(new(metadata!.RequestId, metadata.CorrelationId)),
            cancellationToken);
        return ProductsHttp.Result(result, metadata.CorrelationId);
    }

    private static async Task<IResult> GetProductAsync(
        string productId,
        HttpContext context,
        Application.GetProduct.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!ProductsHttp.TryMetadata(context, false, false, out var metadata, out var error))
            return error!;
        var result = await handler.HandleAsync(
            new Application.GetProduct.Query(productId, new(metadata!.RequestId, metadata.CorrelationId)),
            cancellationToken);
        return ProductsHttp.Result(result, metadata.CorrelationId);
    }

    private static async Task<IResult> GetAvailabilityAsync(
        string productId,
        HttpContext context,
        Application.GetProductAvailability.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!ProductsHttp.TryMetadata(context, false, true, out var metadata, out var error))
            return error!;
        var result = await handler.HandleAsync(
            new Application.GetProductAvailability.Query(
                productId,
                new(metadata!.RequestId, metadata.CorrelationId, metadata.ExpectedVersion)),
            cancellationToken);
        return ProductsHttp.Result(result, metadata.CorrelationId);
    }

    private static async Task<IResult> GetPriceProjectionAsync(
        string productId,
        HttpContext context,
        Application.GetProductPriceProjection.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!ProductsHttp.TryMetadata(context, false, true, out var metadata, out var error))
            return error!;
        var quantity = context.Request.Query["quantity"].ToString();
        var result = await handler.HandleAsync(
            new Application.GetProductPriceProjection.Query(
                productId,
                quantity.Length == 0 ? null : quantity,
                new(metadata!.RequestId, metadata.CorrelationId, metadata.ExpectedVersion)),
            cancellationToken);
        return ProductsHttp.Result(result, metadata.CorrelationId);
    }

    private static async Task<IResult> CreateProductAsync(
        HttpContext context,
        Application.CreateProduct.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!ProductsHttp.TryMetadata(context, true, false, out var metadata, out var error))
            return error!;
        var body = await ProductsHttp.ReadBodyAsync<CreateProductRequest>(context, metadata!.CorrelationId, cancellationToken);
        if (body.Error is not null)
            return body.Error;
        var result = await handler.HandleAsync(new(body.Value!, metadata), cancellationToken);
        return ProductsHttp.Result(result, metadata.CorrelationId, StatusCodes.Status201Created);
    }

    private static async Task<IResult> ReplaceProductAsync(
        string productId,
        HttpContext context,
        Application.ReplaceProduct.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!ProductsHttp.TryMetadata(context, true, true, out var metadata, out var error))
            return error!;
        var body = await ProductsHttp.ReadBodyAsync<ReplaceProductRequest>(context, metadata!.CorrelationId, cancellationToken);
        if (body.Error is not null)
            return body.Error;
        var result = await handler.HandleAsync(new(productId, body.Value!, metadata), cancellationToken);
        return ProductsHttp.Result(result, metadata.CorrelationId);
    }

    private static Task<IResult> ArchiveProductAsync(
        string productId,
        HttpContext context,
        Application.ArchiveProduct.Handler handler,
        CancellationToken cancellationToken) =>
        ExecuteVersionedAsync<ArchiveProductRequest>(
            productId,
            context,
            cancellationToken,
            (request, metadata) => handler.HandleAsync(new(productId, request, metadata), cancellationToken));

    private static Task<IResult> RestoreProductAsync(
        string productId,
        HttpContext context,
        Application.RestoreProduct.Handler handler,
        CancellationToken cancellationToken) =>
        ExecuteVersionedAsync<RestoreProductRequest>(
            productId,
            context,
            cancellationToken,
            (request, metadata) => handler.HandleAsync(new(productId, request, metadata), cancellationToken));

    private static async Task<IResult> ArchiveBatchAsync(
        HttpContext context,
        Application.ArchiveProductsBatch.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!ProductsHttp.TryMetadata(context, true, false, out var metadata, out var error))
            return error!;
        var body = await ProductsHttp.ReadBodyAsync<ArchiveProductsBatchRequest>(context, metadata!.CorrelationId, cancellationToken);
        if (body.Error is not null)
            return body.Error;
        var result = await handler.HandleAsync(new(body.Value!, metadata), cancellationToken);
        return ProductsHttp.Result(result, metadata.CorrelationId);
    }

    private static async Task<IResult> RestoreBatchAsync(
        HttpContext context,
        Application.RestoreProductsBatch.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!ProductsHttp.TryMetadata(context, true, false, out var metadata, out var error))
            return error!;
        var body = await ProductsHttp.ReadBodyAsync<RestoreProductsBatchRequest>(context, metadata!.CorrelationId, cancellationToken);
        if (body.Error is not null)
            return body.Error;
        var result = await handler.HandleAsync(new(body.Value!, metadata), cancellationToken);
        return ProductsHttp.Result(result, metadata.CorrelationId);
    }

    private static async Task<IResult> ExecuteVersionedAsync<TRequest>(
        string productId,
        HttpContext context,
        CancellationToken cancellationToken,
        Func<TRequest, ProductCommandMetadata, Task<ProductOperationResult<ProductMutationResponse>>> execute)
        where TRequest : class
    {
        if (!ProductsHttp.TryMetadata(context, true, true, out var metadata, out var error))
            return error!;
        var body = await ProductsHttp.ReadBodyAsync<TRequest>(context, metadata!.CorrelationId, cancellationToken);
        if (body.Error is not null)
            return body.Error;
        var result = await execute(body.Value!, metadata);
        return ProductsHttp.Result(result, metadata.CorrelationId);
    }
}

internal static class ProductsHttp
{
    internal static bool TryMetadata(
        HttpContext context,
        bool requireIdempotency,
        bool requireIfMatch,
        out ProductCommandMetadata? metadata,
        out IResult? error)
    {
        metadata = null;
        error = null;
        var requestId = context.Request.Headers["X-Request-Id"].ToString();
        var suppliedCorrelation = context.Request.Headers["X-Correlation-Id"].ToString();
        var correlationId = suppliedCorrelation.Length is >= 8 and <= 128 ? suppliedCorrelation : context.TraceIdentifier;
        var idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString();
        var ifMatch = context.Request.Headers.IfMatch.ToString();
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (requestId.Length is < 8 or > 128)
            fields["X-Request-Id"] = ["X-Request-Id must contain between 8 and 128 characters."];
        if (suppliedCorrelation.Length != 0 && suppliedCorrelation.Length is < 8 or > 128)
            fields["X-Correlation-Id"] = ["X-Correlation-Id must contain between 8 and 128 characters."];
        if (requireIdempotency && idempotencyKey.Length is < 8 or > 128)
            fields["Idempotency-Key"] = ["Idempotency-Key must contain between 8 and 128 characters."];
        long? expectedVersion = null;
        if (requireIfMatch && !TryExpectedVersion(ifMatch, out expectedVersion))
            fields["If-Match"] = ["If-Match must contain a quoted non-negative resource version."];
        if (fields.Count != 0)
        {
            error = Error(ProductErrors.Validation(fields, StatusCodes.Status400BadRequest), correlationId);
            return false;
        }

        context.Response.Headers["X-Correlation-Id"] = correlationId;
        metadata = new ProductCommandMetadata(requestId, correlationId, idempotencyKey, expectedVersion);
        return true;
    }

    internal static async Task<BodyRead<T>> ReadBodyAsync<T>(
        HttpContext context,
        string correlationId,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            var value = await context.Request.ReadFromJsonAsync<T>(cancellationToken);
            return value is null
                ? new(null, BodyError("A JSON request body is required.", correlationId))
                : new(value, null);
        }
        catch (JsonException)
        {
            return new(null, BodyError("The JSON request body does not match the contract.", correlationId));
        }
        catch (NotSupportedException)
        {
            return new(null, BodyError("A JSON request body is required.", correlationId));
        }
    }

    internal static IResult Result<T>(
        ProductOperationResult<T> result,
        string correlationId,
        int successStatus = StatusCodes.Status200OK) =>
        result.IsSuccess ? Results.Json(result.Value, statusCode: successStatus) : Error(result.Error!, correlationId);

    internal static IResult Error(ProductOperationError error, string correlationId) =>
        Results.Json(
            new ProductProblemDetails(
                $"urn:unicore:error:{error.Code.ToLowerInvariant()}",
                error.Title,
                error.Status,
                error.Code,
                false,
                correlationId,
                error.Detail,
                null,
                error.FieldErrors,
                error.BusinessBlockers,
                error.AggregateId,
                error.ExpectedVersion,
                error.CurrentVersion,
                error.IdempotencyKey),
            statusCode: error.Status,
            contentType: "application/problem+json");

    private static bool TryExpectedVersion(string supplied, out long? expectedVersion)
    {
        expectedVersion = null;
        var value = supplied.StartsWith("W/", StringComparison.Ordinal) ? supplied[2..] : supplied;
        if (value.Length < 3 || value[0] != '"' || value[^1] != '"')
            return false;
        if (!long.TryParse(value[1..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
            return false;
        expectedVersion = parsed;
        return true;
    }

    private static IResult BodyError(string message, string correlationId) =>
        Error(
            ProductErrors.Validation(
                new Dictionary<string, string[]> { ["body"] = [message] },
                StatusCodes.Status400BadRequest),
            correlationId);

    internal sealed record BodyRead<T>(T? Value, IResult? Error) where T : class;
}
