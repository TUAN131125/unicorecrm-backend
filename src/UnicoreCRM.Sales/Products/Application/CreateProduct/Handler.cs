using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Products.Application.Common;
using UnicoreCRM.Sales.Products.Contracts;
using UnicoreCRM.Sales.Products.Domain;

namespace UnicoreCRM.Sales.Products.Application.CreateProduct;

internal sealed record Command(CreateProductRequest Request, ProductCommandMetadata Metadata);

internal sealed class Handler(
    ProductAuthorization authorization,
    IProductsPersistence persistence,
    IEffectiveWorkspaceBaseCurrencyReader currencyReader,
    TimeProvider timeProvider)
{
    internal async Task<ProductOperationResult<ProductMutationResponse>> HandleAsync(
        Command command,
        CancellationToken cancellationToken)
    {
        var metadata = new ProductRequestMetadata(command.Metadata.RequestId, command.Metadata.CorrelationId);
        var access = await authorization.AuthorizeAsync(ProductCapabilities.Create, metadata, cancellationToken);
        if (!access.IsSuccess)
            return ProductOperationResult<ProductMutationResponse>.Failure(access.Error!);

        ProductValidation.TryProfile(
            command.Request,
            out var profile,
            out var fields,
            out var pricingFields);
        if (fields.Count != 0)
            return ProductOperationResult<ProductMutationResponse>.Failure(ProductErrors.Validation(fields));
        if (pricingFields.Count != 0)
            return ProductOperationResult<ProductMutationResponse>.Failure(ProductErrors.PricingInvalid(pricingFields));

        var trusted = access.Value!.Trusted;
        var fingerprint = ProductCommandSupport.Fingerprint(new { Profile = profile });
        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);
        var scopeKey = ProductCommandSupport.ScopeKey(trusted, "createProduct", "WORKSPACE", command.Metadata.IdempotencyKey);
        var existing = await persistence.FindIdempotencyAsync(scopeKey, cancellationToken);
        if (existing is not null)
        {
            // Answered from stored evidence alone, and projected through the caller's current field
            // policy, so stored evidence cannot leak a value the policy now withholds.
            var replayError = ProductCommandSupport.ReplayError(existing, fingerprint);
            return replayError is null
                ? ProductOperationResult<ProductMutationResponse>.Success(Project(ProductCommandSupport.Replay(existing), access.Value!))
                : ProductOperationResult<ProductMutationResponse>.Failure(replayError);
        }

        // Creation is a resource-level question, so no record scope applies, but field security
        // still does: a field the caller may not write must not be written on the way in either. It
        // follows the replay branch, so a committed creation stays replayable after a field turns
        // READ_ONLY or HIDDEN - the replay writes nothing.
        var createWriteError = ProductFieldSecurity.GuardCreateWrite(access.Value!.Authorization, profile!);
        if (createWriteError is not null)
            return ProductOperationResult<ProductMutationResponse>.Failure(createWriteError);

        var currency = await currencyReader.FindAsync(trusted.WorkspaceId, cancellationToken);
        if (currency is null)
        {
            return ProductOperationResult<ProductMutationResponse>.Failure(ProductErrors.PricingInvalid(
                new Dictionary<string, string[]> { ["unitPrice.currency"] = ["Effective Workspace base currency is unavailable."] }));
        }
        var currencyFields = ProductValidation.ValidateEffectiveCurrency(profile!, currency.BaseCurrency);
        if (currencyFields.Count != 0)
            return ProductOperationResult<ProductMutationResponse>.Failure(ProductErrors.PricingInvalid(currencyFields));

        // Workspace type eligibility. It reads inside this command's serializable transaction, never
        // through the public configuration read operation, so the command neither acquires a
        // studio.read requirement nor leaves a window in which a configuration change could commit
        // between the check and the Product write. It follows the replay branch, so a creation that
        // already committed stays replayable after its type is later deactivated.
        var configuration = await persistence.LoadProductConfigurationForCommandAsync(
            trusted.WorkspaceId, cancellationToken);
        var eligibilityError = ProductTypeEligibility.Evaluate(configuration, profile!.Type, null);
        if (eligibilityError is not null)
            return ProductOperationResult<ProductMutationResponse>.Failure(eligibilityError);

        if (await persistence.SkuExistsAsync(trusted.WorkspaceId, profile.NormalizedSku, null, cancellationToken))
            return ProductOperationResult<ProductMutationResponse>.Failure(ProductErrors.SkuConflict());

        // The command is about to commit on the strength of this configuration revision, so that
        // revision becomes trusted state: a later rollback below it must be detectable. The raise is
        // monotonic and shares this transaction, so it rolls back with any later failure and a
        // rejected command establishes no trust.
        await persistence.RaiseProductConfigurationTrustAsync(
            trusted.WorkspaceId, configuration.Revision, cancellationToken);

        var now = timeProvider.GetUtcNow();
        var product = new Product(trusted.WorkspaceId, profile, now);
        persistence.AddProduct(product);
        var response = ProductCommandSupport.RecordCommit(
            persistence,
            product,
            trusted,
            command.Metadata,
            "createProduct",
            "PRODUCT_CREATED",
            scopeKey,
            "WORKSPACE",
            fingerprint,
            null,
            now);
        try
        {
            await persistence.SaveChangesAsync(cancellationToken);
        }
        catch (ProductsPersistenceUniqueException)
        {
            return ProductOperationResult<ProductMutationResponse>.Failure(ProductErrors.SkuConflict());
        }
        await transaction.CommitAsync(cancellationToken);
        return ProductOperationResult<ProductMutationResponse>.Success(Project(response, access.Value!));
    }

    private static ProductMutationResponse Project(ProductMutationResponse response, ProductAccess access) =>
        response with
        {
            Result = new ProductMutationResult(ProductFieldSecurity.Project(response.Result.Product, access.Authorization))
        };
}
