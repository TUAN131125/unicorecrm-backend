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
        MapGet(endpoints, "/products/configuration/types", ListProductConfigurationTypesAsync, "listProductConfigurationTypes");
        MapPatch(endpoints, "/products/configuration/types/{typeId}", UpdateProductConfigurationTypeAsync, "updateProductConfigurationType");
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

    // createProductConfigurationType and deleteProductConfigurationType stay BLOCKED and are
    // deliberately not mapped, so POST and DELETE on the configuration paths reach no handler.
    private static void MapPatch(IEndpointRouteBuilder endpoints, string path, Delegate handler, string name) =>
        endpoints.MapPatch(path, handler).RequireAuthorization().RequireTrustedWorkspace().WithName(name);

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

    private static async Task<IResult> ListProductConfigurationTypesAsync(
        HttpContext context,
        Application.ListProductConfigurationTypes.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!ProductsHttp.TryMetadata(context, false, false, out var metadata, out var error))
            return error!;
        var result = await handler.HandleAsync(
            new Application.ListProductConfigurationTypes.Query(new(metadata!.RequestId, metadata.CorrelationId)),
            cancellationToken);
        if (result.IsSuccess)
        {
            // A strong validator carrying the revision verbatim. If-Match uses strong comparison, so
            // a weak tag could not serve the concurrency role the contract assigns this version, and
            // an unquoted value is not a valid entity-tag at all.
            context.Response.Headers.ETag =
                "\"" + result.Value!.Revision.ToString(CultureInfo.InvariantCulture) + "\"";
        }

        return ProductsHttp.Result(result, metadata.CorrelationId);
    }

    private static async Task<IResult> UpdateProductConfigurationTypeAsync(
        string typeId,
        HttpContext context,
        Application.UpdateProductConfigurationType.Handler handler,
        CancellationToken cancellationToken)
    {
        // Idempotency and If-Match are both required, and both are transport validation: a missing or
        // malformed header is answered 400 by the shared helper and never reaches the domain. This is
        // the identical helper every other Products command uses, so the global If-Match behaviour is
        // unchanged.
        if (!ProductsHttp.TryMetadata(context, true, true, out var metadata, out var error))
            return error!;
        // Body validation is domain validation for this operation, so a malformed body is 422 rather
        // than the 400 the Products default uses. That difference is scoped to this call site.
        var body = await ProductsHttp.ReadBodyAsync<UpdateProductConfigurationTypeRequest>(
            context,
            metadata!.CorrelationId,
            cancellationToken,
            StatusCodes.Status422UnprocessableEntity);
        if (body.Error is not null)
            return body.Error;
        var result = await handler.HandleAsync(new(typeId, body.Value!, metadata), cancellationToken);
        if (result.IsSuccess)
        {
            // The same strong validator encoding the GET emits, carrying the post-command document
            // revision verbatim. A no-op leaves it byte-identical, which is exactly what the strong
            // comparison If-Match uses requires.
            context.Response.Headers.ETag =
                "\"" + result.Value!.Version.ToString(CultureInfo.InvariantCulture) + "\"";
        }

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

    /// <param name="errorStatus">
    /// The status a malformed or absent body is reported with. It defaults to the established
    /// Products behaviour and is overridden only where the operation's frozen contract classifies the
    /// body as domain validation, so no existing operation changes.
    /// </param>
    internal static async Task<BodyRead<T>> ReadBodyAsync<T>(
        HttpContext context,
        string correlationId,
        CancellationToken cancellationToken,
        int errorStatus = StatusCodes.Status400BadRequest)
        where T : class
    {
        try
        {
            var value = await context.Request.ReadFromJsonAsync<T>(cancellationToken);
            return value is null
                ? new(null, BodyError("A JSON request body is required.", correlationId, errorStatus))
                : new(value, null);
        }
        catch (JsonException)
        {
            return new(null, BodyError("The JSON request body does not match the contract.", correlationId, errorStatus));
        }
        catch (NotSupportedException)
        {
            return new(null, BodyError("A JSON request body is required.", correlationId, errorStatus));
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

    private static IResult BodyError(string message, string correlationId, int status) =>
        Error(
            ProductErrors.Validation(
                new Dictionary<string, string[]> { ["body"] = [message] },
                status),
            correlationId);

    internal sealed record BodyRead<T>(T? Value, IResult? Error) where T : class;
}
