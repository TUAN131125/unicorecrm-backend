using System.Globalization;
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
    /// vocabulary cannot drift from what Products actually projects.
    ///
    /// <para>Two rules, frozen and distinct. A policy naming a key <b>outside</b> this vocabulary is
    /// not readable and not writable - the key fails closed and the public evaluation reports it
    /// HIDDEN - and does not by itself refuse the operation, because this owner never projects it.
    /// A policy naming a key <b>inside</b> this vocabulary that the representation being returned
    /// makes required cannot be honoured at all, and refuses the operation rather than returning a
    /// value the policy forbids.</para>
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

    internal static ProductOperationError? GuardFieldWrite(RecordAccessAuthorization access, params string[] fieldKeys) =>
        Refusal(fieldKeys.Where(fieldKey => !access.CanWrite(fieldKey)).ToList());

    /// <summary>
    /// Refuses a product replacement that would change a field the caller may not write. The check
    /// compares the requested profile against the stored aggregate, so replacing a field with the
    /// value it already holds is not a write and is not refused. Without this comparison a full
    /// replacement would either send every field through the write check - refusing unchanged
    /// READ_ONLY values - or send none, which is what let a READ_ONLY field be replaced.
    /// </summary>
    internal static ProductOperationError? GuardProfileWrite(
        RecordAccessAuthorization access,
        ProductProfile current,
        ProductProfile requested)
    {
        var currentValues = Values(current);
        var blocked = new List<string>();
        foreach (var pair in Values(requested))
        {
            if (!access.CanWrite(pair.Key) && !string.Equals(currentValues[pair.Key], pair.Value, StringComparison.Ordinal))
                blocked.Add(pair.Key);
        }
        return Refusal(blocked);
    }

    /// <summary>
    /// Refuses a creation that populates a field the caller may not write. Creation has no stored
    /// value to compare against, so every field the request actually sets counts as a write, and the
    /// fields the create contract makes mandatory always count.
    /// </summary>
    internal static ProductOperationError? GuardCreateWrite(RecordAccessAuthorization access, ProductProfile profile)
    {
        var blocked = new List<string>();
        foreach (var pair in Values(profile))
        {
            var written = RequiredCreateFields.Contains(pair.Key, StringComparer.Ordinal) || pair.Value.Length != 0;
            if (written && !access.CanWrite(pair.Key))
                blocked.Add(pair.Key);
        }
        return Refusal(blocked);
    }

    /// <summary>
    /// The create-contract fields a Product always carries a value for. A non-writable required
    /// create field fails the creation closed: there is no admitted representation of a Product
    /// created without a SKU, name, type, status, category, unit, price or tax terms.
    /// </summary>
    private static readonly string[] RequiredCreateFields =
        ["sku", "name", "type", "status", "category", "unit", "unitPrice", "taxRate", "taxMode", "billingCycle", "isSubscription", "isRenewable"];

    /// <summary>
    /// The profile as its wire field vocabulary, each value reduced to a canonical string so a change
    /// is decided by value and not by object identity. An empty string means the profile carries no
    /// value for that field.
    /// </summary>
    private static Dictionary<string, string> Values(ProductProfile profile) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["sku"] = profile.Sku,
            ["name"] = profile.Name,
            ["type"] = profile.Type,
            ["status"] = profile.Status,
            ["category"] = profile.Category,
            ["description"] = profile.Description ?? string.Empty,
            ["unit"] = profile.Unit,
            ["unitPrice"] = Money(profile.UnitPrice),
            ["costPrice"] = Money(profile.CostPrice),
            ["taxRate"] = profile.TaxRate,
            ["taxMode"] = profile.TaxMode,
            ["billingCycle"] = profile.BillingCycle,
            ["isSubscription"] = profile.IsSubscription ? "true" : "false",
            ["isRenewable"] = profile.IsRenewable ? "true" : "false",
            ["warrantyMonths"] = Number(profile.WarrantyMonths),
            ["defaultContractMonths"] = Number(profile.DefaultContractMonths),
            ["tags"] = string.Join(",", profile.Tags)
        };

    private static string Money(ProductMoneyValue? value) => value is null ? string.Empty : $"{value.Amount}|{value.Currency}";
    private static string Number(int? value) => value is null ? string.Empty : value.Value.ToString(CultureInfo.InvariantCulture);

    private static ProductOperationError? Refusal(List<string> blocked) =>
        blocked.Count == 0
            ? null
            : new ProductOperationError(
                "ACCESS_DENIED",
                403,
                "Access denied",
                $"Field security does not permit writing: {string.Join(", ", blocked.Order(StringComparer.Ordinal))}.");
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
            // Every Products operation returns the full ProductDocument, so the resource's own
            // required-ness governs and nothing is declared optional.
            RecordAccessRepresentation.Full,
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
    internal async Task<ProductOperationError?> EnforceRecordAsync(
        ProductAccess access,
        Product record,
        string enforcementPoint,
        ProductRequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        var decision = await evaluator.AuthorizeRecordAsync(
            access.Authorization,
            record.ProductId,
            Facts(record),
            enforcementPoint,
            new RecordAccessRequestContext(metadata.RequestId, metadata.CorrelationId),
            cancellationToken);
        return decision.IsAllowed ? null : ProductErrors.NotFound();
    }

    /// <summary>
    /// Authorizes the fields a command is about to write. It is deliberately separate from the
    /// record guard and is applied only on the new-execution path: record scope is current
    /// authorization and must gate a replay, whereas a replay performs no write at all and must not
    /// be refused for lacking permission to write what was already written.
    /// </summary>
    internal static ProductOperationError? EnforceFieldWrite(ProductAccess access, params string[] writtenFieldKeys) =>
        writtenFieldKeys.Length == 0
            ? null
            : ProductFieldSecurity.GuardFieldWrite(access.Authorization, writtenFieldKeys);

    internal static RecordAccessFacts Facts(Product record)
    {
        // Product has no member-owner concept anywhere in its aggregate. Nothing - creator,
        // last editor or category - is substituted for one, so the record is reported with no owner
        // reference and OWN scope consequently denies every Product record. That is an
        // AUTHORITY_GAP, deliberately failing closed rather than inventing ownership.
        return RecordAccessFacts.Found(null);
    }
}
