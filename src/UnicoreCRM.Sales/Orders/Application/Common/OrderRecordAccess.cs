using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Orders.Contracts;
using UnicoreCRM.Sales.Orders.Domain;

namespace UnicoreCRM.Sales.Orders.Application.Common;

internal sealed record OrderAccess(TrustedWorkspaceContext Trusted, RecordAccessAuthorization Authorization);

internal static class OrderFieldSecurity
{
    internal static IReadOnlyDictionary<string, bool> EnforceableFields { get; } =
        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = true,
            ["orderNumber"] = true,
            ["orderDate"] = true,
            ["buyerRef"] = true,
            ["contactId"] = false,
            ["sourceLeadId"] = false,
            ["sourceQuoteId"] = false,
            ["sourceQuoteNumber"] = false,
            ["sourceDealId"] = false,
            ["state"] = true,
            ["lineItems"] = true,
            ["adjustments"] = false,
            ["subtotal"] = true,
            ["discountTotal"] = true,
            ["taxTotal"] = true,
            ["grandTotal"] = true,
            ["currency"] = true,
            ["confirmedAt"] = false,
            ["completedAt"] = false,
            ["cancelledAt"] = false,
            ["expectedDeliveryDate"] = false,
            ["recipientName"] = false,
            ["recipientPhone"] = false,
            ["recipientEmail"] = false,
            ["shippingAddress"] = false,
            ["ownerId"] = false,
            ["notes"] = false,
            ["creditPolicyEvaluation"] = false,
            ["actions"] = true,
            ["archivedAt"] = false,
            ["archiveReason"] = false,
            ["resourceVersion"] = true,
            ["createdAt"] = true,
            ["updatedAt"] = true,
            ["creditApproval"] = false
        };

    internal static IReadOnlyList<string> FieldKeys { get; } =
        EnforceableFields.Keys.Order(StringComparer.Ordinal).ToArray();

    internal static OrderOperationError? UnenforceablePolicy(RecordAccessAuthorization access) =>
        access.UnenforceableFieldKeys.Count == 0
            ? null
            : new OrderOperationError(
                "ACCESS_DENIED",
                403,
                "Access denied",
                "A field-security policy applies to a required Order field, so the request is refused rather than returning a value the policy forbids.");

    internal static OrderReadModel Project(OrderReadModel model, RecordAccessAuthorization access) =>
        model with
        {
            ContactId = Keep(access, "contactId", model.ContactId),
            SourceLeadId = Keep(access, "sourceLeadId", model.SourceLeadId),
            SourceQuoteId = Keep(access, "sourceQuoteId", model.SourceQuoteId),
            SourceQuoteNumber = Keep(access, "sourceQuoteNumber", model.SourceQuoteNumber),
            SourceDealId = Keep(access, "sourceDealId", model.SourceDealId),
            Adjustments = Keep(access, "adjustments", model.Adjustments),
            ConfirmedAt = Keep(access, "confirmedAt", model.ConfirmedAt),
            CompletedAt = Keep(access, "completedAt", model.CompletedAt),
            CancelledAt = Keep(access, "cancelledAt", model.CancelledAt),
            ExpectedDeliveryDate = Keep(access, "expectedDeliveryDate", model.ExpectedDeliveryDate),
            RecipientName = Keep(access, "recipientName", model.RecipientName),
            RecipientPhone = Keep(access, "recipientPhone", model.RecipientPhone),
            RecipientEmail = Keep(access, "recipientEmail", model.RecipientEmail),
            ShippingAddress = Keep(access, "shippingAddress", model.ShippingAddress),
            OwnerId = Keep(access, "ownerId", model.OwnerId),
            Notes = Keep(access, "notes", model.Notes),
            CreditPolicyEvaluation = Keep(access, "creditPolicyEvaluation", model.CreditPolicyEvaluation),
            ArchivedAt = Keep(access, "archivedAt", model.ArchivedAt),
            ArchiveReason = Keep(access, "archiveReason", model.ArchiveReason),
            CreditApproval = Keep(access, "creditApproval", model.CreditApproval)
        };

    private static T? Keep<T>(RecordAccessAuthorization access, string fieldKey, T? value) =>
        access.CanRead(fieldKey) ? value : default;
}

internal sealed class OrderAuthorization(IRecordAccessEvaluator evaluator)
{
    internal const string ResourceKey = "orders";

    internal async Task<OrderOperationResult<OrderAccess>> AuthorizeAsync(
        OrderRequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        var authorization = await evaluator.AuthorizeResourceAsync(
            ResourceKey,
            OrderCapabilities.Read.Capability,
            OrderFieldSecurity.FieldKeys,
            RecordAccessRepresentation.Full,
            new RecordAccessRequestContext(metadata.RequestId, metadata.CorrelationId),
            cancellationToken);

        if (authorization.TrustedWorkspace is not { } trusted)
        {
            return OrderOperationResult<OrderAccess>.Failure(
                authorization.Code == "WORKSPACE_MISMATCH"
                    ? OrderErrors.WorkspaceMismatch()
                    : OrderErrors.AccessDenied());
        }
        if (!authorization.IsAllowed)
            return OrderOperationResult<OrderAccess>.Failure(OrderErrors.AccessDenied());

        var unenforceable = OrderFieldSecurity.UnenforceablePolicy(authorization);
        return unenforceable is null
            ? OrderOperationResult<OrderAccess>.Success(new OrderAccess(trusted, authorization))
            : OrderOperationResult<OrderAccess>.Failure(unenforceable);
    }

    internal async Task<OrderOperationError?> EnforceRecordAsync(
        OrderAccess access,
        Order order,
        string enforcementPoint,
        OrderRequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        var decision = await evaluator.AuthorizeRecordAsync(
            access.Authorization,
            order.OrderId,
            Facts(order),
            enforcementPoint,
            new RecordAccessRequestContext(metadata.RequestId, metadata.CorrelationId),
            cancellationToken);
        return decision.IsAllowed ? null : OrderErrors.NotFound();
    }

    // ownerId is a wire field, not an authoritative AccessControl record-owner fact.
    internal static RecordAccessFacts Facts(Order order) => RecordAccessFacts.Found(null);
}
