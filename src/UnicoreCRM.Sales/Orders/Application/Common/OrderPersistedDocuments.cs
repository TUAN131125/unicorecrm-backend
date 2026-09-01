using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using UnicoreCRM.Sales.Orders.Contracts;

namespace UnicoreCRM.Sales.Orders.Application.Common;

internal static partial class OrderPersistedDocuments
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly string[] LineProperties =
    [
        "id", "productId", "skuSnapshot", "productNameSnapshot", "productTypeSnapshot",
        "descriptionSnapshot", "quantity", "unitPrice", "discountRate", "taxRate", "taxMode",
        "billingCycleSnapshot", "lineSubtotal", "lineDiscountAmount", "lineTaxAmount", "lineTotal"
    ];

    private static readonly string[] RequiredLineProperties =
    [
        "id", "productId", "productNameSnapshot", "quantity", "unitPrice", "discountRate",
        "taxMode", "lineSubtotal", "lineDiscountAmount", "lineTaxAmount", "lineTotal"
    ];

    private static readonly string[] AdjustmentProperties = ["id", "label", "type", "calculation", "value", "amount"];
    private static readonly string[] ShippingProperties = ["line1", "line2", "ward", "district", "city", "country", "postalCode"];
    private static readonly string[] CreditPolicyProperties = ["status", "blockerCodes", "policyVersion", "evaluatedAt"];
    private static readonly string[] CreditApprovalProperties =
    [
        "id", "state", "amount", "policyVersion", "orderResourceVersion",
        "paymentPlanResourceVersion", "resourceVersion"
    ];

    private static readonly HashSet<string> ErrorCodes = new(
        """
        ACCESS_DENIED
        ACCOUNT_ALREADY_EXISTS
        ACCOUNT_SUSPENDED
        ALLOCATION_RESIDUAL_CONFLICT
        AUTHENTICATION_REQUIRED
        COD_COLLECTION_EVIDENCE_REQUIRED
        COD_COLLECTION_REQUIRED_BEFORE_REMITTANCE
        COD_EVIDENCE_INVALID
        CONTRACT_OPERATION_BLOCKED
        CONTRACT_VIOLATION
        CREDIT_APPROVAL_ALREADY_CONSUMED
        CREDIT_APPROVAL_BINDING_MISMATCH
        CREDIT_APPROVAL_NOT_FOUND
        CREDIT_APPROVAL_REASON_REQUIRED
        CREDIT_APPROVAL_REQUIRED
        CREDIT_APPROVAL_STATE_CONFLICT
        CREDIT_APPROVAL_SUPERSEDED
        CURRENCY_MISMATCH
        DEAL_BATCH_EMPTY
        DEAL_BATCH_VERSION_CONFLICT
        DEAL_INVALID_STAGE_TRANSITION
        DEAL_LOSS_REASON_REQUIRED
        DEAL_OWNER_NOT_ASSIGNABLE
        DEAL_PROGRESSIVE_PROFILE_INCOMPLETE
        DEAL_RECYCLE_DATE_REQUIRED
        DEAL_STAGE_INACTIVE
        DEAL_STAGE_NOT_FOUND
        DEAL_TERMINAL_TRANSITION_REQUIRES_OUTCOME
        DEAL_WIN_EVIDENCE_INVALID
        DEAL_WIN_EVIDENCE_REQUIRED
        DEAL_WON_TRANSITION_BLOCKED
        DUPLICATE_BUSINESS_KEY
        DUPLICATE_EXTERNAL_REFERENCE
        EMAIL_NOT_VERIFIED
        FIELD_VALIDATION_FAILED
        FULFILLMENT_EVIDENCE_REQUIRED
        IDEMPOTENCY_KEY_REUSED
        IDEMPOTENCY_REQUEST_IN_PROGRESS
        INTEGRATION_UNAVAILABLE
        INTERNAL_ERROR
        INVALID_CREDENTIALS
        INVITATION_ALREADY_PENDING
        INVITATION_INVALID
        INVITATION_NOT_PENDING
        INVOICE_CREDIT_EXCEEDS_REMAINING
        INVOICE_CREDIT_REASON_REQUIRED
        INVOICE_DELIVERY_CHANNEL_UNAVAILABLE
        INVOICE_DELIVERY_FAILED
        INVOICE_DISCARD_BLOCKED
        INVOICE_HAS_CREDIT_NOTES
        INVOICE_HAS_EFFECTIVE_ALLOCATIONS
        INVOICE_ISSUE_IN_PROGRESS
        INVOICE_ISSUE_RETRY_NOT_ALLOWED
        INVOICE_NOT_CREDITABLE
        INVOICE_NOT_DRAFT
        INVOICE_NOT_ISSUED
        INVOICE_VOID_NOT_ALLOWED
        LAST_WORKSPACE_ADMINISTRATOR
        LEAD_ALREADY_ANONYMIZED
        LEAD_ALREADY_ARCHIVED
        LEAD_ANONYMIZATION_NOT_ALLOWED
        LEAD_ASSIGNMENT_REASON_REQUIRED
        LEAD_BATCH_ASSIGNMENT_CONFLICT
        LEAD_BATCH_DISQUALIFICATION_CONFLICT
        LEAD_BATCH_EMPTY
        LEAD_BATCH_TRANSITION_CONFLICT
        LEAD_BATCH_VERSION_CONFLICT
        LEAD_CONSENT_DECISION_INVALID
        LEAD_CONSENT_EXPIRY_INVALID
        LEAD_CONSENT_SOURCE_REQUIRED
        LEAD_DIRECT_SALE_INPUT_INVALID
        LEAD_DISQUALIFICATION_EVIDENCE_REQUIRED
        LEAD_DUPLICATE_CLUSTER_INVALID
        LEAD_DUPLICATE_MERGE_SOURCE_REQUIRED
        LEAD_DUPLICATE_RECORD_NOT_FOUND
        LEAD_DUPLICATE_RELATIONSHIP_CONFLICT
        LEAD_DUPLICATE_REVIEW_REASON_REQUIRED
        LEAD_DUPLICATE_VERSION_CONFLICT
        LEAD_EXPORT_EMPTY
        LEAD_EXPORT_SCOPE_DENIED
        LEAD_EXPORT_UNAVAILABLE
        LEAD_FOLLOW_UP_DATE_INVALID
        LEAD_FOLLOW_UP_NOTE_REQUIRED
        LEAD_HANDOVER_REASON_REQUIRED
        LEAD_HANDOVER_TASK_SCOPE_DENIED
        LEAD_HANDOVER_TASK_VERSION_CONFLICT
        LEAD_IMPORT_BATCH_TOO_LARGE
        LEAD_IMPORT_CHECKSUM_REQUIRED
        LEAD_IMPORT_DUPLICATE_CONTACT
        LEAD_IMPORT_EMPTY
        LEAD_INVALID_TRANSITION
        LEAD_NURTURE_INPUT_INVALID
        LEAD_OPPORTUNITY_INPUT_INVALID
        LEAD_OWNER_NOT_ASSIGNABLE
        LEAD_PROGRESSIVE_PROFILE_INCOMPLETE
        LEAD_QUALIFICATION_DOWNSTREAM_CAPABILITY_REQUIRED
        LEAD_QUALIFICATION_DOWNSTREAM_MODULE_DISABLED
        LEAD_QUALIFICATION_RELATIONSHIP_INVALID
        LEAD_QUEUE_CLAIM_CONFLICT
        LEAD_REOPEN_NOT_ALLOWED
        LEAD_RETENTION_REASON_REQUIRED
        LEAD_TAG_REQUIRED
        LIFECYCLE_CONFLICT
        MEMBER_ALREADY_EXISTS
        MFA_EXPIRED
        MFA_INVALID
        MFA_LOCKED
        MONEY_INVALID
        ORDER_ARCHIVE_BLOCKED
        ORDER_CANCELLATION_BLOCKED
        ORDER_COMMERCIAL_IMMUTABLE
        ORDER_COMPLETION_BLOCKED
        ORDER_CONFIRMATION_BLOCKED
        ORDER_DELIVERY_EVIDENCE_INVALID
        ORDER_DRAFT_PRICING_INVALID
        ORDER_DRAFT_SOURCE_QUOTE_NOT_ALLOWED
        ORDER_DUPLICATION_BLOCKED
        ORDER_LIFECYCLE_CONFLICT
        ORDER_PRICING_FAILED
        PASSWORD_POLICY_VIOLATION
        PAYMENT_ALLOCATION_EXCEEDS_AVAILABLE
        PAYMENT_DELIVERY_CHANNEL_UNAVAILABLE
        PAYMENT_DELIVERY_FAILED
        PAYMENT_EVIDENCE_REQUIRED
        PAYMENT_GATE_BLOCKED
        PAYMENT_INTENT_ALREADY_TERMINAL
        PAYMENT_INTENT_RETRY_NOT_ALLOWED
        PAYMENT_INTENT_VERSION_CONFLICT
        PAYMENT_METHOD_CURRENCY_UNSUPPORTED
        PAYMENT_METHOD_DISABLED
        PAYMENT_OPERATION_BLOCKED
        PAYMENT_PLAN_FINANCIAL_EVIDENCE_EXISTS
        PAYMENT_PLAN_LINE_INVALID
        PAYMENT_PLAN_TOTAL_MISMATCH
        PAYMENT_PLAN_VERSION_CONFLICT
        PAYMENT_PROVIDER_UNAVAILABLE
        PAYMENT_RECORD_RECONCILIATION_BLOCKED
        PAYMENT_REQUEST_DELIVERY_BLOCKED
        PAYMENT_SOURCE_NOT_EFFECTIVE
        PRODUCT_ARCHIVED
        PRODUCT_ARCHIVE_BLOCKED
        PRODUCT_PRICING_INVALID
        PRODUCT_RESTORE_BLOCKED
        PRODUCT_SKU_CONFLICT
        PRODUCT_UNAVAILABLE
        QUOTE_ACCEPTANCE_BLOCKED
        QUOTE_APPROVAL_NOT_PENDING
        QUOTE_APPROVAL_REQUIRED
        QUOTE_APPROVAL_STALE
        QUOTE_ARCHIVE_BLOCKED
        QUOTE_BATCH_ATOMICITY_FAILED
        QUOTE_DELIVERY_EVIDENCE_INVALID
        QUOTE_DRAFT_IMMUTABLE
        QUOTE_EXPIRATION_BLOCKED
        QUOTE_ORDER_ALREADY_CONVERTED
        QUOTE_ORDER_CONVERSION_BLOCKED
        QUOTE_PRICING_FAILED
        QUOTE_REJECTION_BLOCKED
        QUOTE_REVISION_BLOCKED
        QUOTE_SEND_BLOCKED
        RATE_LIMITED
        RECONCILIATION_BLOCKED
        REFUND_AMOUNT_EXCEEDS_AVAILABLE
        REFUND_ATTEMPT_NOT_FOUND
        REFUND_ATTEMPT_STATE_CONFLICT
        REFUND_CANCELLATION_NOT_SUPPORTED
        REFUND_CANCELLATION_PENDING
        REFUND_CANCELLATION_REASON_REQUIRED
        REFUND_CANCELLATION_REJECTED
        REFUND_OPERATION_BLOCKED
        REFUND_PROVIDER_REFERENCE_CONFLICT
        REFUND_RECOVERY_MANUAL_REVIEW_REQUIRED
        REFUND_RETRY_NOT_ALLOWED
        REQUEST_CANCELLED
        REQUEST_TIMEOUT
        RESOURCE_NOT_FOUND
        RESOURCE_SCOPE_DENIED
        RETURN_CREDIT_EXCEEDS_ISSUED_INVOICE_VALUE
        RETURN_CUSTOMER_CREDIT_ALLOCATION_MANUAL_REVIEW_REQUIRED
        RETURN_CUSTOMER_CREDIT_POLICY_REQUIRED
        RETURN_DELIVERY_EVIDENCE_REQUIRED
        RETURN_INELIGIBLE_OVERRIDE_REQUIRED
        RETURN_QUANTITY_EXCEEDS_REMAINING
        RETURN_REFUND_EXCEEDS_EFFECTIVE_PAYMENT_ALLOCATIONS
        RETURN_REFUND_SAGA_MANUAL_REVIEW_REQUIRED
        RETURN_RESOLUTION_EVIDENCE_REQUIRED
        RETURN_SAGA_MANUAL_REVIEW_REQUIRED
        RETURN_STATE_CONFLICT
        ROLE_INACTIVE
        ROLE_IN_USE
        ROLE_NAME_CONFLICT
        SESSION_EXPIRED
        SESSION_REVOKED
        SHIPPING_BOOKING_STATE_CONFLICT
        SHIPPING_PROVIDER_AUTHENTICATION_FAILED
        SHIPPING_PROVIDER_RATE_LIMITED
        SHIPPING_PROVIDER_REJECTED
        SHIPPING_PROVIDER_RESPONSE_INVALID
        SHIPPING_PROVIDER_TIMEOUT
        SHIPPING_PROVIDER_UNAVAILABLE
        SUPPORT_CASE_INVALID_TRANSITION
        TASK_CANCELLATION_REASON_REQUIRED
        TASK_INVALID_TRANSITION
        TASK_OUTCOME_REQUIRED
        TOKEN_EXPIRED
        TOKEN_INVALID
        UNKNOWN_BUSINESS_STATUS
        VALIDATION_FAILED
        VERSION_CONFLICT
        WORKSPACE_MISMATCH
        """.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        StringComparer.Ordinal);

    internal static IReadOnlyList<OrderLineReadModel> LineItems(string json) =>
        Read<IReadOnlyList<OrderLineReadModel>>(json, "lineItems", ValidateLineItems);

    internal static OrderReadActions Actions(string json) =>
        Read<OrderReadActions>(json, "actions", ValidateActions);

    internal static IReadOnlyList<OrderCommercialAdjustmentReadModel>? Adjustments(string? json) =>
        json is null ? null : Read<IReadOnlyList<OrderCommercialAdjustmentReadModel>>(json, "adjustments", ValidateAdjustments);

    internal static OrderShippingAddressReadModel? ShippingAddress(string? json) =>
        json is null ? null : Read<OrderShippingAddressReadModel>(json, "shippingAddress", ValidateShippingAddress);

    internal static OrderCreditPolicyEvaluationReadModel? CreditPolicyEvaluation(string? json) =>
        json is null ? null : Read<OrderCreditPolicyEvaluationReadModel>(json, "creditPolicyEvaluation", ValidateCreditPolicyEvaluation);

    internal static OrderCreditApprovalSummaryReadModel? CreditApproval(string? json) =>
        json is null ? null : Read<OrderCreditApprovalSummaryReadModel>(json, "creditApproval", ValidateCreditApproval);

    private static T Read<T>(string json, string field, Action<JsonElement> validate)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            validate(document.RootElement);
            return document.RootElement.Deserialize<T>(JsonOptions) ?? throw Invalid(field);
        }
        catch (JsonException)
        {
            throw Invalid(field);
        }
    }

    private static void ValidateLineItems(JsonElement element)
    {
        Array(element, "lineItems", minimumCount: 1);
        foreach (var line in element.EnumerateArray())
        {
            Shape(line, "lineItems", LineProperties, RequiredLineProperties);
            EntityId(line, "id", "lineItems");
            EntityId(line, "productId", "lineItems");
            String(line, "productNameSnapshot", "lineItems", 1, 300);
            OptionalString(line, "skuSnapshot", "lineItems", 0, 120);
            OptionalString(line, "productTypeSnapshot", "lineItems", 0, 120);
            OptionalString(line, "descriptionSnapshot", "lineItems", 0, 2000);
            Decimal(line, "quantity", "lineItems");
            Money(line.GetProperty("unitPrice"), "lineItems");
            Percentage(line, "discountRate", "lineItems");
            if (line.TryGetProperty("taxRate", out var taxRate))
                Percentage(taxRate, "lineItems");
            Enum(line, "taxMode", "lineItems", "EXCLUSIVE", "INCLUSIVE", "NONE");
            OptionalString(line, "billingCycleSnapshot", "lineItems", 0, 120);
            Money(line.GetProperty("lineSubtotal"), "lineItems");
            Money(line.GetProperty("lineDiscountAmount"), "lineItems");
            Money(line.GetProperty("lineTaxAmount"), "lineItems");
            Money(line.GetProperty("lineTotal"), "lineItems");
        }
    }

    private static void ValidateActions(JsonElement element)
    {
        Shape(element, "actions", ["confirm", "cancel"], ["confirm", "cancel"]);
        Action(element.GetProperty("confirm"), "actions");
        Action(element.GetProperty("cancel"), "actions");
    }

    private static void Action(JsonElement element, string field)
    {
        Shape(element, field, ["allowed", "blockerCodes"], ["allowed", "blockerCodes"]);
        var allowed = element.GetProperty("allowed");
        if (allowed.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw Invalid(field);
        BlockerCodes(element.GetProperty("blockerCodes"), field);
    }

    private static void ValidateAdjustments(JsonElement element)
    {
        Array(element, "adjustments");
        foreach (var adjustment in element.EnumerateArray())
        {
            Shape(adjustment, "adjustments", AdjustmentProperties, AdjustmentProperties);
            EntityId(adjustment, "id", "adjustments");
            String(adjustment, "label", "adjustments", 1, 200);
            Enum(adjustment, "type", "adjustments",
                "TAX", "DISCOUNT", "FEE", "SHIPPING", "VOUCHER", "PROMOTION", "SERVICE_FEE",
                "INSTALLATION_FEE", "CONSULTATION_FEE", "SURCHARGE");
            Enum(adjustment, "calculation", "adjustments", "PERCENTAGE", "FIXED_AMOUNT");
            Decimal(adjustment, "value", "adjustments");
            Money(adjustment.GetProperty("amount"), "adjustments");
        }
    }

    private static void ValidateShippingAddress(JsonElement element)
    {
        Shape(element, "shippingAddress", ShippingProperties, ["line1", "city"]);
        String(element, "line1", "shippingAddress", 1, 300);
        String(element, "city", "shippingAddress", 1, 120);
        OptionalString(element, "line2", "shippingAddress", 0, 300);
        OptionalString(element, "ward", "shippingAddress", 0, 120);
        OptionalString(element, "district", "shippingAddress", 0, 120);
        OptionalString(element, "country", "shippingAddress", 0, 120);
        OptionalString(element, "postalCode", "shippingAddress", 0, 30);
    }

    private static void ValidateCreditPolicyEvaluation(JsonElement element)
    {
        Shape(element, "creditPolicyEvaluation", CreditPolicyProperties, ["status", "blockerCodes"]);
        Enum(element, "status", "creditPolicyEvaluation", "NOT_REQUIRED", "APPROVAL_REQUIRED");
        BlockerCodes(element.GetProperty("blockerCodes"), "creditPolicyEvaluation");
        OptionalString(element, "policyVersion", "creditPolicyEvaluation", 0, 160);
        if (element.TryGetProperty("evaluatedAt", out var evaluatedAt))
            UtcDateTime(evaluatedAt, "creditPolicyEvaluation");
    }

    private static void ValidateCreditApproval(JsonElement element)
    {
        Shape(element, "creditApproval", CreditApprovalProperties, CreditApprovalProperties);
        EntityId(element, "id", "creditApproval");
        Enum(element, "state", "creditApproval", "REQUESTED", "APPROVED", "REJECTED", "REVOKED", "CONSUMED", "SUPERSEDED");
        Money(element.GetProperty("amount"), "creditApproval");
        String(element, "policyVersion", "creditApproval", 0, 160);
        ResourceVersion(element.GetProperty("orderResourceVersion"), "creditApproval");
        ResourceVersion(element.GetProperty("paymentPlanResourceVersion"), "creditApproval");
        ResourceVersion(element.GetProperty("resourceVersion"), "creditApproval");
    }

    private static void Money(JsonElement element, string field)
    {
        Shape(element, field, ["amount", "currency"], ["amount", "currency"]);
        Decimal(element, "amount", field);
        String(element, "currency", field, 3, 3, CurrencyCodePattern());
    }

    private static void BlockerCodes(JsonElement element, string field)
    {
        Array(element, field);
        foreach (var blockerCode in element.EnumerateArray())
        {
            if (blockerCode.ValueKind != JsonValueKind.String
                || blockerCode.GetString() is not { } value
                || !ErrorCodes.Contains(value))
            {
                throw Invalid(field);
            }
        }
    }

    private static void Shape(JsonElement element, string field, string[] allowed, string[] required)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw Invalid(field);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name))
                throw Invalid(field);
        }
        if (required.Any(property => !seen.Contains(property)))
            throw Invalid(field);
    }

    private static void Array(JsonElement element, string field, int minimumCount = 0)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() < minimumCount)
            throw Invalid(field);
    }

    private static void EntityId(JsonElement element, string property, string field) =>
        String(element, property, field, 1, 128, EntityIdPattern());

    private static void Decimal(JsonElement element, string property, string field) =>
        String(element, property, field, 1, int.MaxValue, DecimalAmountPattern());

    private static void Percentage(JsonElement element, string property, string field) =>
        Percentage(element.GetProperty(property), field);

    private static void Percentage(JsonElement element, string field) =>
        String(element, field, 1, int.MaxValue, PercentageRatePattern());

    private static void Enum(JsonElement element, string property, string field, params string[] values)
    {
        var value = String(element.GetProperty(property), field, 0, int.MaxValue);
        if (!values.Contains(value, StringComparer.Ordinal))
            throw Invalid(field);
    }

    private static void ResourceVersion(JsonElement element, string field)
    {
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt64(out var value) || value < 0)
            throw Invalid(field);
    }

    private static void UtcDateTime(JsonElement element, string field)
    {
        var value = String(element, field, 1, int.MaxValue);
        if (!UtcDateTimePattern().IsMatch(value)
            || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            throw Invalid(field);
        }
    }

    private static void OptionalString(
        JsonElement element,
        string property,
        string field,
        int minimumLength,
        int maximumLength)
    {
        if (element.TryGetProperty(property, out var value))
            String(value, field, minimumLength, maximumLength);
    }

    private static string String(
        JsonElement element,
        string property,
        string field,
        int minimumLength,
        int maximumLength,
        Regex? pattern = null) =>
        String(element.GetProperty(property), field, minimumLength, maximumLength, pattern);

    private static string String(
        JsonElement element,
        string field,
        int minimumLength,
        int maximumLength,
        Regex? pattern = null)
    {
        if (element.ValueKind != JsonValueKind.String || element.GetString() is not { } value
            || value.Length < minimumLength || value.Length > maximumLength
            || (pattern is not null && !pattern.IsMatch(value)))
        {
            throw Invalid(field);
        }
        return value;
    }

    private static InvalidOperationException Invalid(string field) =>
        new($"Persisted Order {field} is invalid.");

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityIdPattern();

    [GeneratedRegex("^-?(0|[1-9][0-9]*)(\\.[0-9]{1,6})?$", RegexOptions.CultureInvariant)]
    private static partial Regex DecimalAmountPattern();

    [GeneratedRegex("^(?:(?:0|[1-9][0-9]?)(?:\\.[0-9]{1,6})?|100(?:\\.0{1,6})?)$", RegexOptions.CultureInvariant)]
    private static partial Regex PercentageRatePattern();

    [GeneratedRegex("^[A-Z]{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex CurrencyCodePattern();

    [GeneratedRegex("^\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}(?:\\.\\d+)?Z$", RegexOptions.CultureInvariant)]
    private static partial Regex UtcDateTimePattern();
}
