using UnicoreCRM.Billing.Payments.Contracts;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Billing.Payments.Application.Common;

internal sealed record PaymentAccess(TrustedWorkspaceContext Trusted, RecordAccessAuthorization Authorization);

internal static class PaymentFieldSecurity
{
    private static IReadOnlyDictionary<string, bool> PaymentPlanFields { get; } =
        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = true,
            ["workspaceId"] = false,
            ["orderId"] = true,
            ["buyerRef"] = true,
            ["kind"] = true,
            ["state"] = true,
            ["currency"] = true,
            ["agreementSnapshot"] = true,
            ["scheduleLineIds"] = true,
            ["supersedesPlanId"] = false,
            ["supersededByPlanId"] = false,
            ["evidenceCount"] = true,
            ["resourceVersion"] = true,
            ["createdAt"] = true,
            ["updatedAt"] = true,
            ["activatedAt"] = false,
            ["completedAt"] = false,
            ["cancelledAt"] = false
        };

    private static IReadOnlyDictionary<string, bool> PaymentScheduleLineFields { get; } =
        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = true,
            ["workspaceId"] = false,
            ["orderId"] = true,
            ["buyerRef"] = true,
            ["state"] = true,
            ["resourceVersion"] = true,
            ["createdAt"] = true,
            ["updatedAt"] = true,
            ["planId"] = true,
            ["planVersion"] = true,
            ["sequence"] = true,
            ["label"] = true,
            ["purpose"] = true,
            ["amountRule"] = true,
            ["amount"] = true,
            ["dueRule"] = true,
            ["resolvedDueDate"] = false,
            ["allowedMethodCodes"] = true,
            ["preferredMethodCode"] = false,
            ["channel"] = false,
            ["fulfillmentGate"] = true,
            ["invoicePolicyCode"] = false,
            ["satisfiedAmount"] = true,
            ["outstandingAmount"] = true
        };

    private static IReadOnlyDictionary<string, bool> PaymentIntentFields { get; } =
        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = true,
            ["workspaceId"] = false,
            ["buyerRef"] = true,
            ["orderId"] = false,
            ["invoiceIds"] = true,
            ["scheduleLineIds"] = true,
            ["amount"] = true,
            ["methodCode"] = true,
            ["providerCode"] = true,
            ["state"] = true,
            ["checkoutUrl"] = false,
            ["expiresAt"] = true,
            ["failureCode"] = false,
            ["purpose"] = false,
            ["resourceVersion"] = true,
            ["createdAt"] = true,
            ["updatedAt"] = true
        };

    private static IReadOnlyDictionary<string, bool> PaymentIntentStatusFields { get; } =
        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = true,
            ["state"] = true,
            ["failureCode"] = false,
            ["resourceVersion"] = true,
            ["updatedAt"] = true
        };

    private static IReadOnlyDictionary<string, bool> PaymentRecordFields { get; } =
        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = true,
            ["workspaceId"] = false,
            ["buyerRef"] = true,
            ["orderId"] = false,
            ["intentId"] = false,
            ["kind"] = true,
            ["state"] = true,
            ["amount"] = true,
            ["methodCode"] = true,
            ["channel"] = true,
            ["providerCode"] = false,
            ["refundOfPaymentRecordId"] = false,
            ["refundOfCustomerCreditId"] = false,
            ["refundIntentId"] = false,
            ["occurredAt"] = true,
            ["externalReference"] = false,
            ["evidence"] = false,
            ["reconciliationState"] = true,
            ["codCustomerCollectionState"] = false,
            ["codMerchantRemittanceState"] = false,
            ["effectiveForReceivables"] = true,
            ["resourceVersion"] = true,
            ["createdAt"] = true,
            ["updatedAt"] = true
        };

    private static IReadOnlyDictionary<string, bool> PaymentRecordDetailFields { get; } =
        MergeFields(
            PaymentRecordFields,
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["record"] = true,
                ["allocations"] = true,
                ["refunds"] = true,
                ["customerCredits"] = true,
                ["unallocatedAmount"] = true,
                ["refundableAmount"] = true
            });

    internal static IReadOnlyDictionary<string, bool> EnforceableFields { get; } =
        MergeFields(PaymentPlanFields, PaymentScheduleLineFields, PaymentIntentFields, PaymentIntentStatusFields, PaymentRecordFields, PaymentRecordDetailFields);

    internal static IReadOnlyList<string> PaymentPlanFieldKeys { get; } =
        PaymentPlanFields.Keys.Order(StringComparer.Ordinal).ToArray();

    internal static IReadOnlyList<string> PaymentScheduleLineFieldKeys { get; } =
        PaymentScheduleLineFields.Keys.Order(StringComparer.Ordinal).ToArray();

    internal static IReadOnlyList<string> PaymentIntentFieldKeys { get; } =
        PaymentIntentFields.Keys.Order(StringComparer.Ordinal).ToArray();

    internal static IReadOnlyList<string> PaymentIntentStatusFieldKeys { get; } =
        PaymentIntentStatusFields.Keys.Order(StringComparer.Ordinal).ToArray();

    internal static IReadOnlyList<string> PaymentRecordFieldKeys { get; } =
        PaymentRecordFields.Keys.Order(StringComparer.Ordinal).ToArray();

    internal static IReadOnlyList<string> PaymentRecordDetailFieldKeys { get; } =
        PaymentRecordDetailFields.Keys.Order(StringComparer.Ordinal).ToArray();

    internal static RecordAccessRepresentation PaymentPlanRepresentation { get; } =
        RecordAccessRepresentation.Create(
            "payments.payment-plan.full",
            PaymentPlanFields.Where(field => !field.Value).Select(field => field.Key).ToArray());

    internal static RecordAccessRepresentation PaymentScheduleLineRepresentation { get; } =
        RecordAccessRepresentation.Create(
            "payments.payment-schedule-line.full",
            PaymentScheduleLineFields.Where(field => !field.Value).Select(field => field.Key).ToArray());

    internal static RecordAccessRepresentation PaymentIntentRepresentation { get; } =
        RecordAccessRepresentation.Create(
            "payments.payment-intent.full",
            PaymentIntentFields.Where(field => !field.Value).Select(field => field.Key).ToArray());

    internal static RecordAccessRepresentation PaymentIntentStatusRepresentation { get; } =
        RecordAccessRepresentation.Create(
            "payments.payment-intent.status",
            PaymentIntentStatusFields.Where(field => !field.Value).Select(field => field.Key).ToArray());

    internal static RecordAccessRepresentation PaymentRecordRepresentation { get; } =
        RecordAccessRepresentation.Create(
            "payments.payment-record.full",
            PaymentRecordFields.Where(field => !field.Value).Select(field => field.Key).ToArray());

    internal static RecordAccessRepresentation PaymentRecordDetailRepresentation { get; } =
        RecordAccessRepresentation.Create(
            "payments.payment-record.detail",
            PaymentRecordDetailFields.Where(field => !field.Value).Select(field => field.Key).ToArray());

    internal static PaymentOperationError? UnenforceablePolicy(RecordAccessAuthorization access) =>
        access.UnenforceableFieldKeys.Count == 0
            ? null
            : new PaymentOperationError("ACCESS_DENIED", 403, "Access denied");

    internal static PaymentPlanDocument Project(PaymentPlanDocument model, RecordAccessAuthorization access) => model with
    {
        WorkspaceId = Keep(access, "workspaceId", model.WorkspaceId),
        SupersedesPlanId = Keep(access, "supersedesPlanId", model.SupersedesPlanId),
        SupersededByPlanId = Keep(access, "supersededByPlanId", model.SupersededByPlanId),
        ActivatedAt = Keep(access, "activatedAt", model.ActivatedAt),
        CompletedAt = Keep(access, "completedAt", model.CompletedAt),
        CancelledAt = Keep(access, "cancelledAt", model.CancelledAt)
    };

    internal static PaymentScheduleLineDocument Project(PaymentScheduleLineDocument model, RecordAccessAuthorization access) => model with
    {
        WorkspaceId = Keep(access, "workspaceId", model.WorkspaceId),
        ResolvedDueDate = Keep(access, "resolvedDueDate", model.ResolvedDueDate),
        PreferredMethodCode = Keep(access, "preferredMethodCode", model.PreferredMethodCode),
        Channel = Keep(access, "channel", model.Channel),
        InvoicePolicyCode = Keep(access, "invoicePolicyCode", model.InvoicePolicyCode)
    };

    internal static PaymentIntentDocument Project(PaymentIntentDocument model, RecordAccessAuthorization access) => model with
    {
        WorkspaceId = Keep(access, "workspaceId", model.WorkspaceId),
        OrderId = Keep(access, "orderId", model.OrderId),
        CheckoutUrl = Keep(access, "checkoutUrl", model.CheckoutUrl),
        FailureCode = Keep(access, "failureCode", model.FailureCode),
        Purpose = Keep(access, "purpose", model.Purpose)
    };

    internal static PaymentIntentStatusResponse Project(PaymentIntentStatusResponse model, RecordAccessAuthorization access) => model with
    {
        FailureCode = Keep(access, "failureCode", model.FailureCode)
    };

    internal static PaymentRecordDocument Project(PaymentRecordDocument model, RecordAccessAuthorization access) => model with
    {
        WorkspaceId = Keep(access, "workspaceId", model.WorkspaceId),
        OrderId = Keep(access, "orderId", model.OrderId),
        IntentId = Keep(access, "intentId", model.IntentId),
        ProviderCode = Keep(access, "providerCode", model.ProviderCode),
        RefundOfPaymentRecordId = Keep(access, "refundOfPaymentRecordId", model.RefundOfPaymentRecordId),
        RefundOfCustomerCreditId = Keep(access, "refundOfCustomerCreditId", model.RefundOfCustomerCreditId),
        RefundIntentId = Keep(access, "refundIntentId", model.RefundIntentId),
        ExternalReference = Keep(access, "externalReference", model.ExternalReference),
        Evidence = Keep(access, "evidence", model.Evidence),
        CodCustomerCollectionState = Keep(access, "codCustomerCollectionState", model.CodCustomerCollectionState),
        CodMerchantRemittanceState = Keep(access, "codMerchantRemittanceState", model.CodMerchantRemittanceState)
    };

    internal static PaymentRecordDetailResponse Project(PaymentRecordDetailResponse model, RecordAccessAuthorization access) => model with
    {
        Record = Project(model.Record, access)
    };

    private static T? Keep<T>(RecordAccessAuthorization access, string field, T? value) =>
        access.CanRead(field) ? value : default;

    private static IReadOnlyDictionary<string, bool> MergeFields(
        params IReadOnlyDictionary<string, bool>[] representations)
    {
        var fields = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var representation in representations)
        {
            foreach (var field in representation)
            {
                fields[field.Key] = fields.TryGetValue(field.Key, out var required)
                    ? required || field.Value
                    : field.Value;
            }
        }
        return fields;
    }
}

internal sealed class PaymentAuthorization(IRecordAccessEvaluator evaluator)
{
    internal const string ResourceKey = "payments";

    internal Task<PaymentOperationResult<PaymentAccess>> AuthorizePaymentPlansAsync(
        PaymentRequestMetadata metadata,
        CancellationToken cancellationToken) =>
        AuthorizeAsync(
            PaymentCapabilities.PlanRead.Capability,
            PaymentFieldSecurity.PaymentPlanFieldKeys,
            PaymentFieldSecurity.PaymentPlanRepresentation,
            metadata,
            cancellationToken);

    internal Task<PaymentOperationResult<PaymentAccess>> AuthorizePaymentScheduleLinesAsync(
        PaymentRequestMetadata metadata,
        CancellationToken cancellationToken) =>
        AuthorizeAsync(
            PaymentCapabilities.PlanRead.Capability,
            PaymentFieldSecurity.PaymentScheduleLineFieldKeys,
            PaymentFieldSecurity.PaymentScheduleLineRepresentation,
            metadata,
            cancellationToken);

    internal Task<PaymentOperationResult<PaymentAccess>> AuthorizePaymentIntentsAsync(
        PaymentRequestMetadata metadata,
        CancellationToken cancellationToken) =>
        AuthorizeAsync(
            PaymentCapabilities.Read.Capability,
            PaymentFieldSecurity.PaymentIntentFieldKeys,
            PaymentFieldSecurity.PaymentIntentRepresentation,
            metadata,
            cancellationToken);

    internal Task<PaymentOperationResult<PaymentAccess>> AuthorizePaymentIntentStatusAsync(
        PaymentRequestMetadata metadata,
        CancellationToken cancellationToken) =>
        AuthorizeAsync(
            PaymentCapabilities.Read.Capability,
            PaymentFieldSecurity.PaymentIntentStatusFieldKeys,
            PaymentFieldSecurity.PaymentIntentStatusRepresentation,
            metadata,
            cancellationToken);

    internal Task<PaymentOperationResult<PaymentAccess>> AuthorizePaymentRecordsAsync(
        PaymentRequestMetadata metadata,
        CancellationToken cancellationToken) =>
        AuthorizeAsync(
            PaymentCapabilities.Read.Capability,
            PaymentFieldSecurity.PaymentRecordFieldKeys,
            PaymentFieldSecurity.PaymentRecordRepresentation,
            metadata,
            cancellationToken);

    internal Task<PaymentOperationResult<PaymentAccess>> AuthorizePaymentRecordDetailAsync(
        PaymentRequestMetadata metadata,
        CancellationToken cancellationToken) =>
        AuthorizeAsync(
            PaymentCapabilities.Read.Capability,
            PaymentFieldSecurity.PaymentRecordDetailFieldKeys,
            PaymentFieldSecurity.PaymentRecordDetailRepresentation,
            metadata,
            cancellationToken);

    internal async Task<PaymentOperationError?> EnforceRecordAsync(
        PaymentAccess access,
        string recordId,
        string enforcementPoint,
        PaymentRequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        var decision = await evaluator.AuthorizeRecordAsync(
            access.Authorization,
            recordId,
            RecordAccessFacts.Found(null),
            enforcementPoint,
            new RecordAccessRequestContext(metadata.RequestId, metadata.CorrelationId),
            cancellationToken);
        return decision.IsAllowed ? null : PaymentErrors.NotFound();
    }

    private async Task<PaymentOperationResult<PaymentAccess>> AuthorizeAsync(
        string capability,
        IReadOnlyList<string> fieldKeys,
        RecordAccessRepresentation representation,
        PaymentRequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        var authorization = await evaluator.AuthorizeResourceAsync(
            ResourceKey,
            capability,
            fieldKeys,
            representation,
            new RecordAccessRequestContext(metadata.RequestId, metadata.CorrelationId),
            cancellationToken);

        if (authorization.TrustedWorkspace is not { } trusted)
        {
            return PaymentOperationResult<PaymentAccess>.Failure(
                authorization.Code == "WORKSPACE_MISMATCH"
                    ? PaymentErrors.WorkspaceMismatch()
                    : PaymentErrors.AccessDenied());
        }
        if (!authorization.IsAllowed)
            return PaymentOperationResult<PaymentAccess>.Failure(PaymentErrors.AccessDenied());

        var fieldError = PaymentFieldSecurity.UnenforceablePolicy(authorization);
        return fieldError is null
            ? PaymentOperationResult<PaymentAccess>.Success(new PaymentAccess(trusted, authorization))
            : PaymentOperationResult<PaymentAccess>.Failure(fieldError);
    }
}
