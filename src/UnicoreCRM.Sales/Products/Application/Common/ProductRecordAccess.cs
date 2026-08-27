using UnicoreCRM.Sales.Products.Contracts;
using UnicoreCRM.Sales.Products.Domain;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Sales.Products.Application.Common;

/// <summary>
/// The authorization result every Products use case works from: the trusted Workspace plus the
/// AccessControl decision that governs this resource for this caller.
/// </summary>
internal sealed record ProductAccess(TrustedWorkspaceContext Trusted, RecordAccessAuthorization Authorization);

/// <summary>
/// Products-side enforcement of the AccessControl field-security decision. Products decides nothing
/// here: AccessControl has already reduced the policy to a per-field
/// <see cref="RecordFieldEnforcement"/>, and this type only applies it to the Products wire
/// vocabulary. The representation rules are the ones frozen for Support: a withheld optional field is
/// omitted, a withheld required field fails the operation closed, MASKED is enforced as withheld, and
/// READ_ONLY blocks writes.
/// </summary>
internal static class ProductFieldSecurity
{
    /// <summary>
    /// The field keys Products can enforce a policy on, mapped to whether the wire contract makes
    /// the field required. These are the <c>ProductDocument</c> property names, taken from that record so the
    /// vocabulary cannot drift from what Products actually projects. A policy naming any other key
    /// cannot be enforced and fails the operation closed rather than being silently ignored.
    /// </summary>
    internal static IReadOnlyDictionary<string, bool> EnforceableFields { get; } =
        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = true,
            ["sku"] = true,
            ["name"] = true,
            ["type"] = true,
            ["status"] = true,
            ["category"] = true,
            ["unit"] = true,
            ["unitPrice"] = true,
            ["taxRate"] = true,
            ["taxMode"] = true,
            ["billingCycle"] = true,
            ["isSubscription"] = true,
            ["isRenewable"] = true,
            ["tags"] = true,
            ["version"] = true,
            ["createdAt"] = true,
            ["updatedAt"] = true,
            ["description"] = false,
            ["costPrice"] = false,
            ["marginPercent"] = false,
            ["warrantyMonths"] = false,
            ["defaultContractMonths"] = false,
            ["archivedAt"] = false,
            ["archiveReason"] = false
        };

    internal static IReadOnlyList<string> FieldKeys { get; } = EnforceableFields.Keys.Order(StringComparer.Ordinal).ToArray();

    internal static ProductDocument Project(ProductDocument model, RecordAccessAuthorization access) =>
        model with
        {
            Description = access.CanRead("description") ? model.Description : null,
            CostPrice = access.CanRead("costPrice") ? model.CostPrice : null,
            MarginPercent = access.CanRead("marginPercent") ? model.MarginPercent : null,
            WarrantyMonths = access.CanRead("warrantyMonths") ? model.WarrantyMonths : null,
            DefaultContractMonths = access.CanRead("defaultContractMonths") ? model.DefaultContractMonths : null,
            ArchivedAt = access.CanRead("archivedAt") ? model.ArchivedAt : null,
            ArchiveReason = access.CanRead("archiveReason") ? model.ArchiveReason : null
        };

    internal static ProductOperationError? UnenforceablePolicy(RecordAccessAuthorization access) =>
        access.UnenforceableFieldKeys.Count == 0
            ? null
            : new ProductOperationError(
                "ACCESS_DENIED",
                403,
                "Access denied",
                "A field-security policy applies to a field this resource cannot withhold, so the request is refused rather than returning a value the policy forbids.");

    internal static ProductOperationError? GuardFieldWrite(RecordAccessAuthorization access, params string[] fieldKeys)
    {
        var blocked = fieldKeys.Where(fieldKey => !access.CanWrite(fieldKey)).Order(StringComparer.Ordinal).ToArray();
        return blocked.Length == 0
            ? null
            : new ProductOperationError(
                "ACCESS_DENIED",
                403,
                "Access denied",
                $"Field security does not permit writing: {string.Join(", ", blocked)}.");
    }
}

/// <summary>
/// The Products application boundary of the trusted authority chain: authenticated user -> requested
/// Workspace -> verified membership -> trusted CurrentWorkspace -> capability authorization ->
/// record scope -> field security -> Products use case.
///
/// <para>Everything beyond the capability check is decided by AccessControl through
/// <see cref="IRecordAccessEvaluator"/>. Products holds no scope rule and no field rule of its own.</para>
/// </summary>
internal sealed class ProductAuthorization(IRecordAccessEvaluator evaluator)
{
    internal const string ResourceKey = "products";

    internal async Task<ProductOperationResult<ProductAccess>> AuthorizeAsync(
        AccessRequirement requirement,
        ProductRequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        var authorization = await evaluator.AuthorizeResourceAsync(
            ResourceKey,
            requirement.Capability,
            ProductFieldSecurity.FieldKeys,
            new RecordAccessRequestContext(metadata.RequestId, metadata.CorrelationId),
            cancellationToken);

        if (authorization.TrustedWorkspace is not { } trusted)
        {
            return ProductOperationResult<ProductAccess>.Failure(
                authorization.Code == "WORKSPACE_MISMATCH" ? ProductErrors.WorkspaceMismatch() : ProductErrors.AccessDenied());
        }

        if (!authorization.IsAllowed)
            return ProductOperationResult<ProductAccess>.Failure(ProductErrors.AccessDenied());

        var unenforceable = ProductFieldSecurity.UnenforceablePolicy(authorization);
        if (unenforceable is not null)
            return ProductOperationResult<ProductAccess>.Failure(unenforceable);

        return ProductOperationResult<ProductAccess>.Success(new ProductAccess(trusted, authorization));
    }

    /// <summary>
    /// Enforces record scope against the Products-owned authoritative fact.
    /// Product carries no member-owner concept at all, so OWN scope denies every Product record.
    /// A record outside scope is reported as not found.
    /// </summary>
    /// <param name="writtenFieldKeys">
    /// The wire fields the command would change. They are checked only after record scope allows the
    /// record, so a hidden record is reported as missing rather than leaking a field-policy refusal.
    /// </param>
    internal async Task<ProductOperationError?> EnforceRecordAsync(
        ProductAccess access,
        Product record,
        string enforcementPoint,
        ProductRequestMetadata metadata,
        CancellationToken cancellationToken,
        params string[] writtenFieldKeys)
    {
        var decision = await evaluator.AuthorizeRecordAsync(
            access.Authorization,
            record.ProductId,
            Facts(record),
            enforcementPoint,
            new RecordAccessRequestContext(metadata.RequestId, metadata.CorrelationId),
            cancellationToken);
        if (!decision.IsAllowed)
            return ProductErrors.NotFound();
        return writtenFieldKeys.Length == 0
            ? null
            : ProductFieldSecurity.GuardFieldWrite(access.Authorization, writtenFieldKeys);
    }

    internal static RecordAccessFacts Facts(Product record)
    {
        // Product has no member-owner concept anywhere in its aggregate. Nothing - creator,
        // last editor or category - is substituted for one, so the record is reported with no owner
        // reference and OWN scope consequently denies every Product record. That is an
        // AUTHORITY_GAP, deliberately failing closed rather than inventing ownership.
        return RecordAccessFacts.Found(null);
    }
}
