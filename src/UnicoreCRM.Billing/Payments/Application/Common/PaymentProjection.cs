using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using UnicoreCRM.Billing.Payments.Contracts;
using UnicoreCRM.Billing.Payments.Domain;

namespace UnicoreCRM.Billing.Payments.Application.Common;

internal static partial class PaymentProjection
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private static readonly HashSet<string> PlanKinds = ["FULL_PAYMENT", "DEPOSIT_AND_BALANCE", "INSTALLMENT", "MILESTONE", "CUSTOM"];
    private static readonly HashSet<string> PlanStates = ["DRAFT", "ACTIVE", "SUPERSEDED", "CANCELLED", "COMPLETED"];
    private static readonly HashSet<string> BuyerTypes = ["CONTACT", "ORGANIZATION_ACCOUNT"];
    private static readonly HashSet<string> Purposes = ["FULL", "DEPOSIT", "BALANCE", "INSTALLMENT", "MILESTONE", "OTHER"];
    private static readonly HashSet<string> AmountRuleTypes = ["FIXED", "PERCENTAGE", "REMAINDER"];
    private static readonly HashSet<string> DueRuleTypes = ["FIXED_DATE", "EVENT_RELATIVE", "OPERATIONAL_PRECONDITION", "MILESTONE", "RECURRING_FINITE"];
    private static readonly HashSet<string> Events = ["ORDER_CONFIRMED", "INVOICE_ISSUED", "DELIVERY_CONFIRMED", "ACCEPTANCE_CONFIRMED"];
    private static readonly HashSet<string> DayBases = ["CALENDAR", "BUSINESS"];
    private static readonly HashSet<string> Operations = ["BOOKING", "DISPATCH", "COMPLETION"];
    private static readonly HashSet<string> Intervals = ["WEEKLY", "MONTHLY", "QUARTERLY"];
    private static readonly HashSet<string> Channels = ["BANK", "ONLINE_GATEWAY", "POS", "CARRIER", "OFFLINE", "EXTERNAL"];
    private static readonly HashSet<string> Gates = ["NONE", "BEFORE_BOOKING", "BEFORE_DISPATCH", "BEFORE_COMPLETION"];
    private static readonly HashSet<string> ScheduleStates = ["SCHEDULED", "NOT_DUE", "DUE", "PARTIAL", "SATISFIED", "OVERDUE", "VOIDED"];
    private static readonly HashSet<string> IntentStates = ["CREATED", "REQUIRES_ACTION", "PROCESSING", "SUCCEEDED", "FAILED", "CANCELLED", "EXPIRED"];
    private static readonly HashSet<string> IntentPurposes = ["DEPOSIT", "FULL_PAYMENT", "INSTALLMENT", "OVERDUE_REMINDER", "OTHER"];
    private static readonly HashSet<string> RecordKinds = ["PAYMENT", "REFUND"];
    private static readonly HashSet<string> RecordStates = ["CREATED", "PENDING", "PROCESSING", "SUCCEEDED", "FAILED", "CANCELLED", "EXPIRED", "REVERSED"];
    private static readonly HashSet<string> ReconciliationStates = ["UNRECONCILED", "MATCHED", "MISMATCH"];
    private static readonly HashSet<string> CodCustomerStates = ["NOT_REQUESTED", "REQUESTED", "COLLECTED", "FAILED"];
    private static readonly HashSet<string> CodMerchantStates = ["NOT_APPLICABLE", "PENDING", "REMITTED", "FAILED"];
    private static readonly HashSet<string> EvidenceTypes = ["PAYMENT_PROOF", "COD_REMITTANCE", "INVOICE_ISSUE_RESULT", "DELIVERY_POD", "RETURN_INSPECTION", "REFUND", "REPLACEMENT_DELIVERY", "OTHER"];
    private static readonly HashSet<string> EvidenceVerificationStates = ["UNVERIFIED", "VERIFIED", "REJECTED"];
    private static readonly HashSet<string> PaymentSourceTypes = ["PAYMENT_RECORD", "CUSTOMER_CREDIT"];
    private static readonly HashSet<string> AllocationStates = ["EFFECTIVE", "REVERSED"];
    private static readonly HashSet<string> RefundStates = ["CREATED", "PROCESSING", "SUCCEEDED", "FAILED", "CANCELLED"];
    private static readonly HashSet<string> CustomerCreditStates = ["AVAILABLE", "PARTIALLY_ALLOCATED", "ALLOCATED", "REVERSED"];

    internal static PaymentRecordDocument Record(PaymentRecord record)
    {
        EntityId(record.WorkspaceId, nameof(record.WorkspaceId));
        EntityId(record.PaymentRecordId, nameof(record.PaymentRecordId));
        Enum(record.BuyerType, BuyerTypes, nameof(record.BuyerType));
        EntityId(record.BuyerId, nameof(record.BuyerId));
        OptionalEntityId(record.OrderId, nameof(record.OrderId));
        OptionalEntityId(record.PaymentIntentId, nameof(record.PaymentIntentId));
        Enum(record.Kind, RecordKinds, nameof(record.Kind));
        Enum(record.State, RecordStates, nameof(record.State));
        Currency(record.Currency, nameof(record.Currency));
        Text(record.MethodCode, 1, 100, nameof(record.MethodCode));
        Enum(record.Channel, Channels, nameof(record.Channel));
        OptionalText(record.ProviderCode, 0, 100, nameof(record.ProviderCode));
        OptionalEntityId(record.RefundOfPaymentRecordId, nameof(record.RefundOfPaymentRecordId));
        OptionalEntityId(record.RefundOfCustomerCreditId, nameof(record.RefundOfCustomerCreditId));
        OptionalEntityId(record.RefundIntentId, nameof(record.RefundIntentId));
        OptionalText(record.ExternalReference, 0, 240, nameof(record.ExternalReference));
        Enum(record.ReconciliationState, ReconciliationStates, nameof(record.ReconciliationState));
        if (record.CodCustomerCollectionState is not null)
            Enum(record.CodCustomerCollectionState, CodCustomerStates, nameof(record.CodCustomerCollectionState));
        if (record.CodMerchantRemittanceState is not null)
            Enum(record.CodMerchantRemittanceState, CodMerchantStates, nameof(record.CodMerchantRemittanceState));
        if (record.ResourceVersion < 0)
            Invalid("PaymentRecord resourceVersion is outside the read contract.");

        IReadOnlyList<PaymentEvidenceItem>? evidence = null;
        if (record.EvidenceJson is not null)
        {
            evidence = Document<PaymentEvidenceItem[]>(record.EvidenceJson, "evidence");
            if (evidence.Count > 100)
                Invalid("evidence exceeds the read contract maximum.");
            foreach (var item in evidence)
                ValidateEvidence(item);
        }

        return new PaymentRecordDocument(
            record.PaymentRecordId,
            new PaymentBuyerReference(record.BuyerType, record.BuyerId),
            record.Kind,
            record.State,
            Money(record.Amount, record.Currency),
            record.MethodCode,
            record.Channel,
            Timestamp(record.OccurredAt),
            record.ReconciliationState,
            record.EffectiveForReceivables,
            record.ResourceVersion,
            Timestamp(record.CreatedAt),
            Timestamp(record.UpdatedAt))
        {
            WorkspaceId = record.WorkspaceId,
            OrderId = record.OrderId,
            IntentId = record.PaymentIntentId,
            ProviderCode = record.ProviderCode,
            RefundOfPaymentRecordId = record.RefundOfPaymentRecordId,
            RefundOfCustomerCreditId = record.RefundOfCustomerCreditId,
            RefundIntentId = record.RefundIntentId,
            ExternalReference = record.ExternalReference,
            Evidence = evidence,
            CodCustomerCollectionState = record.CodCustomerCollectionState,
            CodMerchantRemittanceState = record.CodMerchantRemittanceState
        };
    }

    internal static PaymentRecordDetailResponse RecordDetail(PaymentRecord record)
    {
        var allocations = Document<PaymentAllocationDocument[]>(record.AllocationsJson, "allocations");
        foreach (var allocation in allocations) ValidateAllocation(allocation);
        var refunds = Document<PaymentRefundIntentDocument[]>(record.RefundsJson, "refunds");
        foreach (var refund in refunds) ValidateRefund(refund);
        var customerCredits = Document<PaymentCustomerCreditDocument[]>(record.CustomerCreditsJson, "customerCredits");
        foreach (var credit in customerCredits) ValidateCustomerCredit(credit);
        Currency(record.UnallocatedCurrency, nameof(record.UnallocatedCurrency));
        Currency(record.RefundableCurrency, nameof(record.RefundableCurrency));

        return new PaymentRecordDetailResponse(
            Record(record),
            allocations,
            refunds,
            customerCredits,
            Money(record.UnallocatedAmount, record.UnallocatedCurrency),
            Money(record.RefundableAmount, record.RefundableCurrency));
    }

    internal static PaymentIntentDocument Intent(PaymentIntent intent)
    {
        EntityId(intent.WorkspaceId, nameof(intent.WorkspaceId));
        EntityId(intent.PaymentIntentId, nameof(intent.PaymentIntentId));
        Enum(intent.BuyerType, BuyerTypes, nameof(intent.BuyerType));
        EntityId(intent.BuyerId, nameof(intent.BuyerId));
        OptionalEntityId(intent.OrderId, nameof(intent.OrderId));
        Currency(intent.Currency, nameof(intent.Currency));
        Text(intent.MethodCode, 1, 100, nameof(intent.MethodCode));
        Text(intent.ProviderCode, 1, 100, nameof(intent.ProviderCode));
        Enum(intent.State, IntentStates, nameof(intent.State));
        OptionalText(intent.FailureCode, 0, 160, nameof(intent.FailureCode));
        if (intent.Purpose is not null)
            Enum(intent.Purpose, IntentPurposes, nameof(intent.Purpose));
        if (intent.CheckoutUrl is not null
            && (intent.CheckoutUrl.Length > 2000
                || !Uri.TryCreate(intent.CheckoutUrl, UriKind.Absolute, out _)))
            Invalid("CheckoutUrl is outside the read contract.");
        if (intent.ResourceVersion < 0)
            Invalid("PaymentIntent resourceVersion is outside the read contract.");

        var invoiceIds = EntityIdArray(intent.InvoiceIdsJson, "invoiceIds");
        var scheduleLineIds = EntityIdArray(intent.ScheduleLineIdsJson, "scheduleLineIds");

        return new PaymentIntentDocument(
            intent.PaymentIntentId,
            new PaymentBuyerReference(intent.BuyerType, intent.BuyerId),
            invoiceIds,
            scheduleLineIds,
            Money(intent.Amount, intent.Currency),
            intent.MethodCode,
            intent.ProviderCode,
            intent.State,
            Timestamp(intent.ExpiresAt),
            intent.ResourceVersion,
            Timestamp(intent.CreatedAt),
            Timestamp(intent.UpdatedAt))
        {
            WorkspaceId = intent.WorkspaceId,
            OrderId = intent.OrderId,
            CheckoutUrl = intent.CheckoutUrl,
            FailureCode = intent.FailureCode,
            Purpose = intent.Purpose
        };
    }

    internal static PaymentIntentStatusResponse IntentStatus(PaymentIntent intent)
    {
        EntityId(intent.PaymentIntentId, nameof(intent.PaymentIntentId));
        Enum(intent.State, IntentStates, nameof(intent.State));
        OptionalText(intent.FailureCode, 0, 160, nameof(intent.FailureCode));
        if (intent.ResourceVersion < 0)
            Invalid("PaymentIntent resourceVersion is outside the read contract.");

        return new PaymentIntentStatusResponse(
            intent.PaymentIntentId,
            intent.State,
            intent.ResourceVersion,
            Timestamp(intent.UpdatedAt))
        {
            FailureCode = intent.FailureCode
        };
    }

    internal static PaymentPlanDocument Plan(PaymentPlan plan)
    {
        EntityId(plan.WorkspaceId, nameof(plan.WorkspaceId));
        EntityId(plan.PaymentPlanId, nameof(plan.PaymentPlanId));
        EntityId(plan.OrderId, nameof(plan.OrderId));
        Enum(plan.BuyerType, BuyerTypes, nameof(plan.BuyerType));
        EntityId(plan.BuyerId, nameof(plan.BuyerId));
        Enum(plan.Kind, PlanKinds, nameof(plan.Kind));
        Enum(plan.State, PlanStates, nameof(plan.State));
        Currency(plan.Currency, nameof(plan.Currency));
        if (plan.EvidenceCount < 0 || plan.ResourceVersion < 0)
            Invalid("PaymentPlan counters or version are outside the read contract.");
        OptionalEntityId(plan.SupersedesPlanId, nameof(plan.SupersedesPlanId));
        OptionalEntityId(plan.SupersededByPlanId, nameof(plan.SupersededByPlanId));

        var agreement = Document<PaymentAgreementSnapshotDocument>(plan.AgreementSnapshotJson, "agreementSnapshot");
        ValidateAgreement(agreement);
        var scheduleLineIds = Document<string[]>(plan.ScheduleLineIdsJson, "scheduleLineIds");
        if (scheduleLineIds is null || scheduleLineIds.Distinct(StringComparer.Ordinal).Count() != scheduleLineIds.Length)
            Invalid("scheduleLineIds is invalid.");
        foreach (var id in scheduleLineIds)
            EntityId(id, "scheduleLineIds");

        return new PaymentPlanDocument(
            plan.PaymentPlanId,
            plan.OrderId,
            new PaymentBuyerReference(plan.BuyerType, plan.BuyerId),
            plan.Kind,
            plan.State,
            plan.Currency,
            agreement,
            scheduleLineIds,
            plan.EvidenceCount,
            plan.ResourceVersion,
            Timestamp(plan.CreatedAt),
            Timestamp(plan.UpdatedAt))
        {
            WorkspaceId = plan.WorkspaceId,
            SupersedesPlanId = plan.SupersedesPlanId,
            SupersededByPlanId = plan.SupersededByPlanId,
            ActivatedAt = OptionalTimestamp(plan.ActivatedAt),
            CompletedAt = OptionalTimestamp(plan.CompletedAt),
            CancelledAt = OptionalTimestamp(plan.CancelledAt)
        };
    }

    internal static PaymentScheduleLineDocument ScheduleLine(PaymentScheduleLine line)
    {
        EntityId(line.WorkspaceId, nameof(line.WorkspaceId));
        EntityId(line.PaymentScheduleLineId, nameof(line.PaymentScheduleLineId));
        EntityId(line.PaymentPlanId, nameof(line.PaymentPlanId));
        EntityId(line.OrderId, nameof(line.OrderId));
        Enum(line.BuyerType, BuyerTypes, nameof(line.BuyerType));
        EntityId(line.BuyerId, nameof(line.BuyerId));
        if (line.PaymentPlanVersion < 0 || line.ResourceVersion < 0 || line.Sequence < 1)
            Invalid("PaymentScheduleLine sequence or version is outside the read contract.");
        Text(line.Label, 1, 240, nameof(line.Label));
        Enum(line.Purpose, Purposes, nameof(line.Purpose));
        Currency(line.AmountCurrency, nameof(line.AmountCurrency));
        Currency(line.SatisfiedCurrency, nameof(line.SatisfiedCurrency));
        Currency(line.OutstandingCurrency, nameof(line.OutstandingCurrency));
        Enum(line.FulfillmentGate, Gates, nameof(line.FulfillmentGate));
        Enum(line.State, ScheduleStates, nameof(line.State));
        OptionalText(line.PreferredMethodCode, 0, 100, nameof(line.PreferredMethodCode));
        OptionalText(line.InvoicePolicyCode, 0, 100, nameof(line.InvoicePolicyCode));
        if (line.Channel is not null)
            Enum(line.Channel, Channels, nameof(line.Channel));

        var amountRule = Document<PaymentAmountRule>(line.AmountRuleJson, "amountRule");
        ValidateAmountRule(amountRule);
        var dueRule = Document<PaymentDueRule>(line.DueRuleJson, "dueRule");
        ValidateDueRule(dueRule);
        var allowedMethods = Document<string[]>(line.AllowedMethodCodesJson, "allowedMethodCodes");
        ValidateMethodCodes(allowedMethods);

        return new PaymentScheduleLineDocument(
            line.PaymentScheduleLineId,
            line.PaymentPlanId,
            line.PaymentPlanVersion,
            line.OrderId,
            new PaymentBuyerReference(line.BuyerType, line.BuyerId),
            line.Sequence,
            line.Label,
            line.Purpose,
            amountRule,
            Money(line.Amount, line.AmountCurrency),
            dueRule,
            allowedMethods,
            line.FulfillmentGate,
            line.State,
            Money(line.SatisfiedAmount, line.SatisfiedCurrency),
            Money(line.OutstandingAmount, line.OutstandingCurrency),
            line.ResourceVersion,
            Timestamp(line.CreatedAt),
            Timestamp(line.UpdatedAt))
        {
            WorkspaceId = line.WorkspaceId,
            ResolvedDueDate = line.ResolvedDueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            PreferredMethodCode = line.PreferredMethodCode,
            Channel = line.Channel,
            InvoicePolicyCode = line.InvoicePolicyCode
        };
    }

    private static void ValidateAgreement(PaymentAgreementSnapshotDocument agreement)
    {
        if (agreement is null || agreement.Version < 0)
            Invalid("agreementSnapshot version is invalid.");
        Enum(agreement.Kind, PlanKinds, "agreementSnapshot.kind");
        Currency(agreement.Currency, "agreementSnapshot.currency");
        if (agreement.Lines is null || agreement.Lines.Count is < 1 or > 100)
            Invalid("agreementSnapshot.lines is invalid.");
        foreach (var line in agreement.Lines)
        {
            if (line is null || line.Sequence < 1)
                Invalid("agreementSnapshot line sequence is invalid.");
            EntityId(line.Id, "agreementSnapshot.lines.id");
            Text(line.Label, 1, 240, "agreementSnapshot.lines.label");
            Enum(line.Purpose, Purposes, "agreementSnapshot.lines.purpose");
            ValidateAmountRule(line.AmountRule);
            ValidateMoney(line.PreviewAmount, "agreementSnapshot.lines.previewAmount");
            ValidateDueRule(line.DueRule);
            ValidateMethodCodes(line.AllowedMethodCodes);
            Enum(line.FulfillmentGate, Gates, "agreementSnapshot.lines.fulfillmentGate");
            OptionalText(line.PreferredMethodCode, 0, 100, "preferredMethodCode");
            OptionalText(line.InvoicePolicyCode, 0, 100, "invoicePolicyCode");
            if (line.Channel is not null)
                Enum(line.Channel, Channels, "channel");
        }
        if (agreement.AcceptedAt is not null)
            ParseTimestamp(agreement.AcceptedAt, "acceptedAt");
        OptionalEntityId(agreement.SourceQuoteId, "sourceQuoteId");
        OptionalText(agreement.PolicyVersion, 0, 160, "policyVersion");
    }

    private static void ValidateAmountRule(PaymentAmountRule rule)
    {
        if (rule is null)
            Invalid("amountRule is required.");
        Enum(rule.Type, AmountRuleTypes, "amountRule.type");
        if (rule.Amount is not null)
            ValidateMoney(rule.Amount, "amountRule.amount");
        if (rule.Percentage is not null && (rule.Percentage.Length > 40 || !PercentagePattern().IsMatch(rule.Percentage)))
            Invalid("amountRule.percentage is invalid.");
    }

    private static void ValidateDueRule(PaymentDueRule rule)
    {
        if (rule is null)
            Invalid("dueRule is required.");
        Enum(rule.Type, DueRuleTypes, "dueRule.type");
        OptionalDate(rule.Date, "dueRule.date");
        OptionalDate(rule.FirstDueDate, "dueRule.firstDueDate");
        if (rule.Event is not null) Enum(rule.Event, Events, "dueRule.event");
        if (rule.DayBasis is not null) Enum(rule.DayBasis, DayBases, "dueRule.dayBasis");
        if (rule.Operation is not null) Enum(rule.Operation, Operations, "dueRule.operation");
        if (rule.Interval is not null) Enum(rule.Interval, Intervals, "dueRule.interval");
        if (rule.OffsetDays is < -3650 or > 3650 || rule.LeadDays is < 0 or > 3650 || rule.Count is < 1 or > 120)
            Invalid("dueRule numeric value is invalid.");
        OptionalText(rule.MilestoneCode, 1, 100, "dueRule.milestoneCode");
    }

    private static void ValidateMethodCodes(IReadOnlyList<string> codes)
    {
        if (codes is null || codes.Count == 0 || codes.Distinct(StringComparer.Ordinal).Count() != codes.Count)
            Invalid("allowedMethodCodes is invalid.");
        foreach (var code in codes)
            Text(code, 1, 100, "allowedMethodCodes");
    }

    private static IReadOnlyList<string> EntityIdArray(string json, string name)
    {
        var values = Document<string[]>(json, name);
        if (values is null || values.Distinct(StringComparer.Ordinal).Count() != values.Length)
            Invalid($"{name} is invalid.");
        foreach (var value in values)
            EntityId(value, name);
        return values;
    }

    private static void ValidateEvidence(PaymentEvidenceItem item)
    {
        if (item is null) Invalid("evidence item is required.");
        EntityId(item.Id, "evidence.id");
        Enum(item.Type, EvidenceTypes, "evidence.type");
        ParseTimestamp(item.CapturedAt, "evidence.capturedAt");
        EntityId(item.CapturedBy, "evidence.capturedBy");
        Enum(item.VerificationState, EvidenceVerificationStates, "evidence.verificationState");
        ParseTimestamp(item.CreatedAt, "evidence.createdAt");
        OptionalText(item.FileName, 0, 500, "evidence.fileName");
        OptionalText(item.MimeType, 0, 200, "evidence.mimeType");
        OptionalText(item.ExternalReference, 0, 500, "evidence.externalReference");
        OptionalText(item.Notes, 0, 2000, "evidence.notes");
        if (item.Url is not null
            && (item.Url.Length > 2000
                || (item.Url.Length != 0 && !Uri.TryCreate(item.Url, UriKind.RelativeOrAbsolute, out _))))
            Invalid("evidence.url is invalid.");
    }

    private static void ValidateAllocation(PaymentAllocationDocument allocation)
    {
        if (allocation is null) Invalid("allocation is required.");
        EntityId(allocation.Id, "allocations.id");
        ValidateBuyer(allocation.BuyerRef, "allocations.buyerRef");
        EntityId(allocation.InvoiceId, "allocations.invoiceId");
        Enum(allocation.SourceType, PaymentSourceTypes, "allocations.sourceType");
        EntityId(allocation.SourceId, "allocations.sourceId");
        ValidateMoney(allocation.Amount, "allocations.amount");
        Enum(allocation.State, AllocationStates, "allocations.state");
        if (allocation.ResourceVersion < 0) Invalid("allocation resourceVersion is invalid.");
        ParseTimestamp(allocation.CreatedAt, "allocations.createdAt");
        OptionalEntityId(allocation.WorkspaceId, "allocations.workspaceId");
        OptionalEntityId(allocation.ScheduleLineId, "allocations.scheduleLineId");
        if (allocation.ReversedAt is not null) ParseTimestamp(allocation.ReversedAt, "allocations.reversedAt");
        OptionalText(allocation.ReversalReasonCode, 0, 100, "allocations.reversalReasonCode");
        OptionalText(allocation.ReversalReason, 0, 1000, "allocations.reversalReason");
        if (allocation.AuditEvidenceIds is not null)
            foreach (var id in allocation.AuditEvidenceIds) Text(id, 1, 160, "allocations.auditEvidenceIds");
    }

    private static void ValidateRefund(PaymentRefundIntentDocument refund)
    {
        if (refund is null) Invalid("refund is required.");
        EntityId(refund.Id, "refunds.id");
        ValidateBuyer(refund.BuyerRef, "refunds.buyerRef");
        if (refund.Source is null) Invalid("refunds.source is required.");
        Enum(refund.Source.Type, PaymentSourceTypes, "refunds.source.type");
        EntityId(refund.Source.Id, "refunds.source.id");
        ValidateMoney(refund.Amount, "refunds.amount");
        Enum(refund.State, RefundStates, "refunds.state");
        Text(refund.ReasonCode, 1, 100, "refunds.reasonCode");
        Text(refund.Reason, 1, 1000, "refunds.reason");
        if (refund.ResourceVersion < 0) Invalid("refund resourceVersion is invalid.");
        ParseTimestamp(refund.CreatedAt, "refunds.createdAt");
        ParseTimestamp(refund.UpdatedAt, "refunds.updatedAt");
        OptionalEntityId(refund.WorkspaceId, "refunds.workspaceId");
        OptionalEntityId(refund.SourceReturnId, "refunds.sourceReturnId");
        OptionalEntityId(refund.OrderId, "refunds.orderId");
        if (refund.InvoiceIds is not null) ValidateEntityIds(refund.InvoiceIds, "refunds.invoiceIds", true);
        OptionalEntityId(refund.RefundPaymentRecordId, "refunds.refundPaymentRecordId");
        OptionalText(refund.FailureCode, 0, 160, "refunds.failureCode");
        OptionalText(refund.ProviderCode, 1, 100, "refunds.providerCode");
        OptionalEntityId(refund.LatestProviderAttemptId, "refunds.latestProviderAttemptId");
    }

    private static void ValidateCustomerCredit(PaymentCustomerCreditDocument credit)
    {
        if (credit is null) Invalid("customerCredit is required.");
        EntityId(credit.Id, "customerCredits.id");
        ValidateBuyer(credit.BuyerRef, "customerCredits.buyerRef");
        EntityId(credit.SourcePaymentRecordId, "customerCredits.sourcePaymentRecordId");
        ValidateMoney(credit.OriginalAmount, "customerCredits.originalAmount");
        ValidateMoney(credit.AvailableAmount, "customerCredits.availableAmount");
        Enum(credit.State, CustomerCreditStates, "customerCredits.state");
        if (credit.ResourceVersion < 0) Invalid("customerCredit resourceVersion is invalid.");
        ParseTimestamp(credit.CreatedAt, "customerCredits.createdAt");
        ParseTimestamp(credit.UpdatedAt, "customerCredits.updatedAt");
        OptionalEntityId(credit.WorkspaceId, "customerCredits.workspaceId");
    }

    private static void ValidateBuyer(PaymentBuyerReference buyer, string name)
    {
        if (buyer is null) Invalid($"{name} is required.");
        Enum(buyer.Type, BuyerTypes, $"{name}.type");
        EntityId(buyer.Id, $"{name}.id");
    }

    private static void ValidateEntityIds(IReadOnlyList<string> values, string name, bool unique)
    {
        if (values is null || (unique && values.Distinct(StringComparer.Ordinal).Count() != values.Count))
            Invalid($"{name} is invalid.");
        foreach (var value in values) EntityId(value, name);
    }

    private static T Document<T>(string json, string name)
    {
        try
        {
            using var parsed = JsonDocument.Parse(json);
            RejectDuplicateProperties(parsed.RootElement);
            return JsonSerializer.Deserialize<T>(json, JsonOptions)
                ?? throw new JsonException($"{name} cannot be null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Persisted Payments {name} does not satisfy the admitted read contract.", exception);
        }
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    Invalid("Persisted Payments JSON contains a duplicate property.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) RejectDuplicateProperties(item);
        }
    }

    private static PaymentMoney Money(decimal value, string currency) =>
        new(value.ToString("0.######", CultureInfo.InvariantCulture), currency);

    private static void ValidateMoney(PaymentMoney value, string name)
    {
        if (value is null || !DecimalPattern().IsMatch(value.Amount))
            Invalid($"{name}.amount is invalid.");
        Currency(value.Currency, $"{name}.currency");
    }

    private static string Timestamp(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
    private static string? OptionalTimestamp(DateTimeOffset? value) => value is null ? null : Timestamp(value.Value);

    private static void ParseTimestamp(string value, string name)
    {
        if (!value.EndsWith('Z') || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))
            Invalid($"{name} is not a UTC timestamp.");
    }

    private static void OptionalDate(string? value, string name)
    {
        if (value is not null && !DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            Invalid($"{name} is invalid.");
    }

    private static void EntityId(string value, string name)
    {
        if (value is null || !EntityIdPattern().IsMatch(value)) Invalid($"{name} is invalid.");
    }

    private static void OptionalEntityId(string? value, string name)
    {
        if (value is not null) EntityId(value, name);
    }

    private static void Currency(string value, string name)
    {
        if (value is null || !CurrencyPattern().IsMatch(value)) Invalid($"{name} is invalid.");
    }

    private static void Enum(string value, HashSet<string> values, string name)
    {
        if (value is null || !values.Contains(value)) Invalid($"{name} is invalid.");
    }

    private static void Text(string value, int min, int max, string name)
    {
        if (value is null || value.Length < min || value.Length > max) Invalid($"{name} is invalid.");
    }

    private static void OptionalText(string? value, int min, int max, string name)
    {
        if (value is not null && (value.Length < min || value.Length > max)) Invalid($"{name} is invalid.");
    }

    [DoesNotReturn]
    private static void Invalid(string message) => throw new InvalidOperationException(message);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)] private static partial Regex EntityIdPattern();
    [GeneratedRegex("^[A-Z]{3}$", RegexOptions.CultureInvariant)] private static partial Regex CurrencyPattern();
    [GeneratedRegex("^-?(0|[1-9][0-9]*)(\\.[0-9]{1,6})?$", RegexOptions.CultureInvariant)] private static partial Regex DecimalPattern();
    [GeneratedRegex("^[0-9]+(\\.[0-9]+)?$", RegexOptions.CultureInvariant)] private static partial Regex PercentagePattern();
}
